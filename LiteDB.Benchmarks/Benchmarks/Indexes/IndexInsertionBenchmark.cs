using System;
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace LiteDB.Benchmarks.Benchmarks.Indexes
{
    [BenchmarkCategory(Constants.Categories.INDEXES)]
    public class IndexInsertionBenchmark : IndexBenchmarkBase
    {
        private const int InsertCount = 256;

        private LiteDatabase _database;
        private ILiteCollection<BsonDocument> _collection;
        private int[] _keyOrdinals;
        private BsonDocument[] _documents;

        [Params(10_000, 100_000)]
        public int DatasetSize;

        [Params(IndexKeyKind.Int32, IndexKeyKind.ShortString, IndexKeyKind.LongString)]
        public IndexKeyKind KeyKind;

        [Params(IndexInsertionPattern.Sequential, IndexInsertionPattern.Random)]
        public IndexInsertionPattern Pattern;

        [GlobalSetup]
        public void GlobalSetup()
        {
            DeleteDatabaseFiles();
            CreateIndexedDatabase(TemplatePath, DatasetSize, KeyKind, i => i * 2);

            _keyOrdinals = Pattern == IndexInsertionPattern.Sequential
                ? CreateSequentialOrdinals()
                : CreateRandomGapOrdinals();
        }

        [IterationSetup]
        public void IterationSetup()
        {
            RestoreTemplate();

            _database = OpenDatabase(DatabasePath);
            _collection = GetCollection(_database);
            _documents = new BsonDocument[InsertCount];

            for (var i = 0; i < InsertCount; i++)
            {
                _documents[i] = CreateDocument(DatasetSize + i, _keyOrdinals[i], KeyKind);
            }
        }

        [Benchmark(OperationsPerInvoke = InsertCount)]
        public int IndexedInsert()
        {
            var count = _collection.Insert(_documents);
            _database.Checkpoint();
            return count;
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

        private int[] CreateSequentialOrdinals()
        {
            return Enumerable.Range(0, InsertCount)
                .Select(i => (DatasetSize + i) * 2)
                .ToArray();
        }

        private int[] CreateRandomGapOrdinals()
        {
            var slots = Enumerable.Range(0, DatasetSize).ToArray();
            var random = new Random(Seed);

            for (var i = 0; i < InsertCount; i++)
            {
                var j = random.Next(i, slots.Length);
                (slots[i], slots[j]) = (slots[j], slots[i]);
            }

            return slots
                .Take(InsertCount)
                .Select(slot => slot * 2 + 1)
                .ToArray();
        }
    }
}
