using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace LiteDB.Benchmarks.Benchmarks
{
    /// <summary>
    /// Stable read workloads shared by all comparative performance branches.
    /// Keep the data shape and access sequence unchanged between branches.
    /// </summary>
    [MemoryDiagnoser]
    public class ComparativeReadBenchmarks
    {
        private const int DocumentCount = 20_000;
        private const int ReadsPerInvoke = 1_024;

        private string _filename;
        private LiteDatabase _database;
        private ILiteCollection<ComparativeDocument> _collection;
        private int[] _randomIds;
        private int[] _localIds;

        [Params(1, 4, 16)]
        public int ReaderThreads { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _filename = Path.Combine(Path.GetTempPath(), "litedb-comparative-read-" + Guid.NewGuid() + ".db");

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
            var localStart = DocumentCount / 2;
            _localIds = Enumerable.Range(localStart, ReadsPerInvoke).ToArray();

            Reopen();
            ValidateSeed();
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = ReadsPerInvoke)]
        public int RandomPointLookupHot()
        {
            return LookupBatch(_randomIds);
        }

        [Benchmark(OperationsPerInvoke = ReadsPerInvoke)]
        public int LocalPointLookupHot()
        {
            return LookupBatch(_localIds);
        }

        [Benchmark]
        public int NarrowIndexedRange()
        {
            return _collection.Find(x => x.Age >= 40 && x.Age <= 44).Count();
        }

        [Benchmark]
        public int FullScan()
        {
            return _collection.FindAll().Count();
        }

        [Benchmark(OperationsPerInvoke = ReadsPerInvoke)]
        public int ConcurrentRandomPointLookups()
        {
            var checksum = 0;
            var perWorker = ReadsPerInvoke / ReaderThreads;

            Parallel.For(0, ReaderThreads, worker =>
            {
                var localChecksum = 0;
                var start = worker * perWorker;
                var end = worker == ReaderThreads - 1 ? ReadsPerInvoke : start + perWorker;

                for (var i = start; i < end; i++)
                {
                    var document = _collection.FindById(_randomIds[i]);
                    if (document == null) throw new InvalidOperationException("Seeded benchmark document is missing.");
                    localChecksum += document.Age;
                }

                Interlocked.Add(ref checksum, localChecksum);
            });

            return checksum;
        }

        [IterationSetup(Target = nameof(RandomPointLookupAfterReopen))]
        public void ReopenBeforeIteration()
        {
            Reopen();
        }

        [Benchmark(OperationsPerInvoke = ReadsPerInvoke)]
        public int RandomPointLookupAfterReopen()
        {
            return LookupBatch(_randomIds);
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

        private void Reopen()
        {
            _database?.Dispose();
            _database = OpenDatabase();
            _collection = _database.GetCollection<ComparativeDocument>("docs");
        }

        private int LookupBatch(int[] ids)
        {
            var checksum = 0;
            for (var i = 0; i < ids.Length; i++)
            {
                var document = _collection.FindById(ids[i]);
                if (document == null) throw new InvalidOperationException("Seeded benchmark document is missing.");
                checksum += document.Age;
            }
            return checksum;
        }

        private void ValidateSeed()
        {
            if (_collection.Count() != DocumentCount)
                throw new InvalidOperationException("Comparative read benchmark seed count mismatch.");
            _ = LookupBatch(_randomIds);
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
