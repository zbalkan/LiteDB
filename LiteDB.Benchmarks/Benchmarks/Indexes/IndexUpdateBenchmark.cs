using System;
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace LiteDB.Benchmarks.Benchmarks.Indexes
{
    [BenchmarkCategory(Constants.Categories.INDEXES)]
    public class IndexUpdateBenchmark : IndexBenchmarkBase
    {
        private const int UpdateCount = 256;

        private LiteDatabase _database;
        private ILiteCollection<BsonDocument> _collection;
        private int[] _documentIds;
        private BsonDocument[] _documents;

        [Params(10_000, 100_000)]
        public int DatasetSize;

        [Params(IndexKeyKind.Int32, IndexKeyKind.ShortString, IndexKeyKind.LongString)]
        public IndexKeyKind KeyKind;

        [GlobalSetup]
        public void GlobalSetup()
        {
            DeleteDatabaseFiles();
            CreateIndexedDatabase(TemplatePath, DatasetSize, KeyKind);
            _documentIds = CreateRandomDocumentIds();
        }

        [IterationSetup]
        public void IterationSetup()
        {
            RestoreTemplate();

            _database = OpenDatabase(DatabasePath);
            _collection = GetCollection(_database);
            _documents = new BsonDocument[UpdateCount];

            for (var i = 0; i < UpdateCount; i++)
            {
                _documents[i] = CreateDocument(
                    _documentIds[i],
                    DatasetSize + i,
                    KeyKind);
            }
        }

        [Benchmark(OperationsPerInvoke = UpdateCount)]
        public int IndexedKeyUpdate()
        {
            var updated = _collection.Update(_documents);
            _database.Checkpoint();
            return updated;
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            _database?.Dispose();
            _database = null;
            _collection = null;
            _documents = null;

            DeleteWorkingDatabase();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            DeleteDatabaseFiles();
        }

        private int[] CreateRandomDocumentIds()
        {
            var ids = Enumerable.Range(0, DatasetSize).ToArray();
            var random = new Random(Seed);

            for (var i = 0; i < UpdateCount; i++)
            {
                var j = random.Next(i, ids.Length);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }

            return ids.Take(UpdateCount).ToArray();
        }
    }
}
