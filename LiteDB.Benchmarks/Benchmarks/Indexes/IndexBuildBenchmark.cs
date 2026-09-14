using BenchmarkDotNet.Attributes;

namespace LiteDB.Benchmarks.Benchmarks.Indexes
{
    [BenchmarkCategory(Constants.Categories.INDEXES)]
    public class IndexBuildBenchmark : IndexBenchmarkBase
    {
        private LiteDatabase _database;
        private ILiteCollection<BsonDocument> _collection;
        private BsonExpression _indexExpression;

        [Params(10_000, 100_000)]
        public int DatasetSize;

        [Params(IndexKeyKind.Int32, IndexKeyKind.ShortString, IndexKeyKind.LongString)]
        public IndexKeyKind KeyKind;

        [GlobalSetup]
        public void GlobalSetup()
        {
            DeleteDatabaseFiles();
            CreateUnindexedDatabase(TemplatePath, DatasetSize, KeyKind);
            _indexExpression = BsonExpression.Create(IndexField);
        }

        [IterationSetup]
        public void IterationSetup()
        {
            RestoreTemplate();
            _database = OpenDatabase(DatabasePath);
            _collection = GetCollection(_database);
        }

        [Benchmark]
        public bool EnsureIndex()
        {
            var created = _collection.EnsureIndex(IndexField, _indexExpression);
            _database.Checkpoint();
            return created;
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            _database?.Dispose();
            _database = null;
            _collection = null;

            DeleteWorkingDatabase();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            DeleteDatabaseFiles();
        }
    }
}
