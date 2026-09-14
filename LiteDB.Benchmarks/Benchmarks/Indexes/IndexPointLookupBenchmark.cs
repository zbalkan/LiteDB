using System;
using BenchmarkDotNet.Attributes;

namespace LiteDB.Benchmarks.Benchmarks.Indexes
{
    [BenchmarkCategory(Constants.Categories.INDEXES)]
    public class IndexPointLookupBenchmark : IndexBenchmarkBase
    {
        private const int ProbeCount = 1024;

        private LiteDatabase _database;
        private ILiteCollection<BsonDocument> _collection;
        private BsonExpression[] _hitQueries;
        private BsonExpression[] _missQueries;
        private int _hitCursor;
        private int _missCursor;

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

            _hitQueries = new BsonExpression[ProbeCount];
            _missQueries = new BsonExpression[ProbeCount];

            var random = new Random(Seed);

            for (var i = 0; i < ProbeCount; i++)
            {
                var hitOrdinal = random.Next(DatasetSize);
                var missOrdinal = DatasetSize + i + 1;

                _hitQueries[i] = Query.EQ(IndexField, CreateKey(hitOrdinal, KeyKind));
                _missQueries[i] = Query.EQ(IndexField, CreateKey(missOrdinal, KeyKind));
            }
        }

        [Benchmark]
        public int IndexedHit()
        {
            var query = _hitQueries[_hitCursor++ & (ProbeCount - 1)];
            return _collection.Count(query);
        }

        [Benchmark]
        public int IndexedMiss()
        {
            var query = _missQueries[_missCursor++ & (ProbeCount - 1)];
            return _collection.Count(query);
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
