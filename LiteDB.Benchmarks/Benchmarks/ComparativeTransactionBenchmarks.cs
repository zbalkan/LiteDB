using System;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace LiteDB.Benchmarks.Benchmarks
{
    /// <summary>
    /// Stable transaction workloads shared by all comparative performance branches.
    /// Each measured invocation starts from a freshly seeded database so WAL growth
    /// and previous mutations cannot bias later iterations.
    /// </summary>
    [MemoryDiagnoser]
    public class ComparativeTransactionBenchmarks
    {
        private const int SeedCount = 5_000;
        private const string PayloadA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string PayloadB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private string _filename;
        private LiteDatabase _database;
        private ILiteCollection<ComparativeDocument> _collection;
        private bool _payloadToggle;

        [Params(1, 10, 100, 1_000)]
        public int TransactionSize { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            _filename = Path.Combine(Path.GetTempPath(), "litedb-comparative-write-" + Guid.NewGuid() + ".db");
        }

        [IterationSetup]
        public void SetupIteration()
        {
            DisposeDatabase();
            DeleteDatabaseFiles(_filename);

            _database = new LiteDatabase(new ConnectionString(_filename)
            {
                CacheSize = 64L * 1024L * 1024L,
                TransactionPageLimit = 1_000
            });
            _collection = _database.GetCollection<ComparativeDocument>("docs");
            _collection.InsertBulk(Enumerable.Range(1, SeedCount).Select(CreateDocument));
            _collection.EnsureIndex("age_idx", x => x.Age);
            _database.Checkpoint();
            _payloadToggle = false;
        }

        [Benchmark(Baseline = true)]
        [InvocationCount(1)]
        [UnrollFactor(1)]
        public void NonIndexedUpdateSingleCommit()
        {
            var payload = _payloadToggle ? PayloadA : PayloadB;
            _payloadToggle = !_payloadToggle;

            _database.BeginTrans();
            for (var id = 1; id <= TransactionSize; id++)
            {
                var document = RequireDocument(id);
                document.Payload = payload;
                if (!_collection.Update(document))
                    throw new InvalidOperationException("Comparative update unexpectedly failed.");
            }
            _database.Commit();
        }

        [Benchmark]
        [InvocationCount(1)]
        [UnrollFactor(1)]
        public void IndexedUpdateSingleCommit()
        {
            _database.BeginTrans();
            for (var id = 1; id <= TransactionSize; id++)
            {
                var document = RequireDocument(id);
                document.Age = (document.Age + 17) % 90;
                if (!_collection.Update(document))
                    throw new InvalidOperationException("Comparative indexed update unexpectedly failed.");
            }
            _database.Commit();
        }

        [Benchmark]
        [InvocationCount(1)]
        [UnrollFactor(1)]
        public void IndexedUpdateCommitEach()
        {
            for (var id = 1; id <= TransactionSize; id++)
            {
                _database.BeginTrans();
                var document = RequireDocument(id);
                document.Age = (document.Age + 17) % 90;
                if (!_collection.Update(document))
                    throw new InvalidOperationException("Comparative indexed update unexpectedly failed.");
                _database.Commit();
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            DisposeDatabase();
            DeleteDatabaseFiles(_filename);
        }

        private ComparativeDocument RequireDocument(int id)
        {
            return _collection.FindById(id)
                ?? throw new InvalidOperationException("Seeded comparative benchmark document is missing.");
        }

        private void DisposeDatabase()
        {
            _database?.Dispose();
            _database = null;
            _collection = null;
        }

        private static ComparativeDocument CreateDocument(int id)
        {
            return new ComparativeDocument
            {
                Id = id,
                Age = id % 90,
                Payload = PayloadA
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
