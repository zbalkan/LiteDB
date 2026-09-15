using System;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace LiteDB.Benchmarks.Benchmarks
{
    /// <summary>
    /// Measures point reads after reopening the database. This workload is kept
    /// separate from steady-state reads because hosted-runner I/O variance is much higher.
    /// </summary>
    [MemoryDiagnoser]
    public class ComparativeReopenBenchmarks
    {
        private const int DocumentCount = 20_000;
        private const int ReadsPerInvoke = 1_024;

        private string _filename;
        private LiteDatabase _database;
        private ILiteCollection<ComparativeDocument> _collection;
        private int[] _randomIds;

        [GlobalSetup]
        public void Setup()
        {
            _filename = Path.Combine(Path.GetTempPath(), "litedb-comparative-reopen-" + Guid.NewGuid() + ".db");

            using (var database = OpenDatabase())
            {
                var collection = database.GetCollection<ComparativeDocument>("docs");
                collection.InsertBulk(Enumerable.Range(1, DocumentCount).Select(CreateDocument));
                collection.EnsureIndex("age_idx", x => x.Age);
                database.Checkpoint();
            }

            _randomIds = Enumerable.Range(0, ReadsPerInvoke)
                .Select(i => ((i * 7_919) % DocumentCount) + 1)
                .ToArray();
        }

        [IterationSetup]
        public void ReopenBeforeIteration()
        {
            _database?.Dispose();
            _database = OpenDatabase();
            _collection = _database.GetCollection<ComparativeDocument>("docs");
        }

        [Benchmark(OperationsPerInvoke = ReadsPerInvoke)]
        public int RandomPointLookupAfterReopen()
        {
            var checksum = 0;
            for (var i = 0; i < _randomIds.Length; i++)
            {
                var document = _collection.FindById(_randomIds[i]);
                if (document == null) throw new InvalidOperationException("Seeded benchmark document is missing.");
                checksum += document.Age;
            }
            return checksum;
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _database?.Dispose();
            DeleteDatabaseFiles(_filename);
        }

        private LiteDatabase OpenDatabase()
        {
            return new LiteDatabase(new ConnectionString(_filename)
            {
                CacheSize = 64L * 1024L * 1024L,
                TransactionPageLimit = 1_000
            });
        }

        private static ComparativeDocument CreateDocument(int id)
        {
            return new ComparativeDocument
            {
                Id = id,
                Age = id % 90,
                Payload = new string((char)('a' + id % 26), 256)
            };
        }

        private static void DeleteDatabaseFiles(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return;

            var directory = Path.GetDirectoryName(filename) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(filename);
            var extension = Path.GetExtension(filename);

            DeleteIfExists(filename);
            DeleteIfExists(Path.Combine(directory, stem + "-log" + extension));
            DeleteIfExists(Path.Combine(directory, stem + "-tmp" + extension));
        }

        private static void DeleteIfExists(string filename)
        {
            if (File.Exists(filename)) File.Delete(filename);
        }

        public sealed class ComparativeDocument
        {
            public int Id { get; set; }
            public int Age { get; set; }
            public string Payload { get; set; }
        }
    }
}
