using System;
using BenchmarkDotNet.Attributes;

namespace LiteDB.Benchmarks.Benchmarks.Indexes
{
    [BenchmarkCategory(Constants.Categories.INDEXES)]
    public class IndexRangeScanBenchmark : IndexBenchmarkBase
    {
        private const int ProbeCount = 256;

        private LiteDatabase _database;
        private ILiteCollection<BsonDocument> _collection;
        private BsonExpression[] _rangeQueries;
        private int _cursor;

        [Params(10_000, 100_000)]
        public int DatasetSize;

        [Params(10, 100, 1000)]
        public int RangeWidth;

        [Params(IndexKeyKind.Int32, IndexKeyKind.ShortString, IndexKeyKind.LongString)]
        public IndexKeyKind KeyKind;

        [GlobalSetup]
        public void GlobalSetup()
        {
            DeleteDatabaseFiles();
            CreateIndexedDatabase(DatabasePath, DatasetSize, KeyKind);

            _database = OpenDatabase(DatabasePath);
            _collection = GetCollection(_database);
            _rangeQueries = new BsonExpression[ProbeCount];

            var random = new Random(Seed + RangeWidth);
            var maxStart = DatasetSize - RangeWidth;

            for (var i = 0; i < ProbeCount; i++)
            {
                var start = random.Next(maxStart + 1);
                var end = start + RangeWidth - 1;

                _rangeQueries[i] = Query.Between(
                    IndexField,
                    CreateKey(start, KeyKind),
                    CreateKey(end, KeyKind));
            }
        }

        [Benchmark]
        public int IndexedRange()
        {
            var query = _rangeQueries[_cursor++ & (ProbeCount - 1)];
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
