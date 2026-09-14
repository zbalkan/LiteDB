using BenchmarkDotNet.Attributes;

namespace LiteDB.Benchmarks.Benchmarks.Indexes
{
    [BenchmarkCategory(Constants.Categories.INDEXES)]
    public class IndexOrderedScanBenchmark : IndexBenchmarkBase
    {
        private LiteDatabase _database;
        private ILiteCollection<BsonDocument> _collection;

        [Params(10_000, 100_000)]
        public int DatasetSize;

        [Params(IndexKeyKind.Int32, IndexKeyKind.ShortString, IndexKeyKind.LongString)]
        public IndexKeyKind KeyKind;

        [GlobalSetup]
        public void GlobalSetup()
        {
            DeleteDatabaseFiles();
            CreateIndexedDatabase(DatabasePath, DatasetSize, KeyKind);

            _database = OpenDatabase(DatabasePath);
            _collection = GetCollection(_database);
        }

        [Benchmark]
        public int Ascending()
        {
            return Scan(Query.Ascending);
        }

        [Benchmark]
        public int Descending()
        {
            return Scan(Query.Descending);
        }

        private int Scan(int order)
        {
            var count = 0;

            foreach (var _ in _collection.Find(Query.All(IndexField, order)))
            {
                count++;
            }

            return count;
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _database?.Dispose();
            _database = null;
            _collection = null;

            DeleteDatabaseFiles();
        }
    }
}
