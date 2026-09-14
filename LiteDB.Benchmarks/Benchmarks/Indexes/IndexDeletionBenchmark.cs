using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace LiteDB.Benchmarks.Benchmarks.Indexes
{
    [BenchmarkCategory(Constants.Categories.INDEXES)]
    public class IndexDeletionBenchmark : IndexBenchmarkBase
    {
        private const int DeleteCount = 256;

        private LiteDatabase _database;
        private ILiteCollection<BsonDocument> _collection;
        private int[] _documentIds;

        [Params(10_000, 100_000)]
        public int DatasetSize;

        [Params(IndexKeyKind.Int32, IndexKeyKind.ShortString, IndexKeyKind.LongString)]
        public IndexKeyKind KeyKind;

        [Params(1, 3)]
        public int SecondaryIndexCount;

        [GlobalSetup]
        public void GlobalSetup()
        {
            DeleteDatabaseFiles();

            using (var database = OpenDatabase(TemplatePath))
            {
                var collection = GetCollection(database);

                collection.EnsureIndex(IndexField, BsonExpression.Create(IndexField));

                if (SecondaryIndexCount >= 2)
                {
                    collection.EnsureIndex("Key2", BsonExpression.Create("Key2"));
                }

                if (SecondaryIndexCount >= 3)
                {
                    collection.EnsureIndex("Key3", BsonExpression.Create("Key3"));
                }

                collection.InsertBulk(CreateDeletionDocuments());
                database.Checkpoint();
            }

            _documentIds = CreateRandomDocumentIds();
        }

        [IterationSetup]
        public void IterationSetup()
        {
            RestoreTemplate();
            _database = OpenDatabase(DatabasePath);
            _collection = GetCollection(_database);
        }

        [Benchmark(OperationsPerInvoke = DeleteCount)]
        public int IndexedDelete()
        {
            var deleted = 0;

            foreach (var id in _documentIds)
            {
                if (_collection.Delete(id))
                {
                    deleted++;
                }
            }

            _database.Checkpoint();
            return deleted;
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

        private IEnumerable<BsonDocument> CreateDeletionDocuments()
        {
            for (var i = 0; i < DatasetSize; i++)
            {
                var document = CreateDocument(i, i, KeyKind);

                if (SecondaryIndexCount >= 2)
                {
                    document["Key2"] = CreateKey(DatasetSize - i, KeyKind);
                }

                if (SecondaryIndexCount >= 3)
                {
                    document["Key3"] = CreateKey(i * 2, KeyKind);
                }

                yield return document;
            }
        }

        private int[] CreateRandomDocumentIds()
        {
            var ids = Enumerable.Range(0, DatasetSize).ToArray();
            var random = new Random(Seed + SecondaryIndexCount);

            for (var i = 0; i < DeleteCount; i++)
            {
                var j = random.Next(i, ids.Length);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }

            return ids.Take(DeleteCount).ToArray();
        }
    }
}
