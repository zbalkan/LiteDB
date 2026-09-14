using System;
using System.Collections.Generic;
using System.IO;

namespace LiteDB.Benchmarks.Benchmarks.Indexes
{
    public enum IndexKeyKind
    {
        Int32,
        ShortString,
        LongString
    }

    public enum IndexInsertionPattern
    {
        Sequential,
        Random
    }

    public abstract class IndexBenchmarkBase
    {
        protected const int Seed = 3131;
        protected const string CollectionName = "index_benchmark";
        protected const string IndexField = "Key";

        protected string DatabasePath => Path.Combine(
            Path.GetTempPath(),
            $"LiteDB-Benchmarks-{GetType().Name}-{Environment.ProcessId}.db");

        protected string TemplatePath => DatabasePath + ".template";

        protected LiteDatabase OpenDatabase(string filename)
        {
            return new LiteDatabase(new ConnectionString(filename)
            {
                Connection = ConnectionType.Direct
            });
        }

        protected static ILiteCollection<BsonDocument> GetCollection(LiteDatabase database)
        {
            return database.GetCollection<BsonDocument>(CollectionName);
        }

        protected static BsonDocument CreateDocument(int id, int keyOrdinal, IndexKeyKind keyKind)
        {
            return new BsonDocument
            {
                ["_id"] = id,
                [IndexField] = CreateKey(keyOrdinal, keyKind),
                ["Payload"] = $"payload-{id:D8}"
            };
        }

        protected static BsonValue CreateKey(int ordinal, IndexKeyKind keyKind)
        {
            return keyKind switch
            {
                IndexKeyKind.Int32 => new BsonValue(ordinal),
                IndexKeyKind.ShortString => new BsonValue($"k-{ordinal:D10}"),
                IndexKeyKind.LongString => new BsonValue($"key-{ordinal:D10}-0123456789abcdef0123456789abcdef0123456789abcdef"),
                _ => throw new ArgumentOutOfRangeException(nameof(keyKind), keyKind, null)
            };
        }

        protected static IEnumerable<BsonDocument> CreateDocuments(
            int count,
            IndexKeyKind keyKind,
            Func<int, int> keySelector = null)
        {
            for (var i = 0; i < count; i++)
            {
                yield return CreateDocument(i, keySelector?.Invoke(i) ?? i, keyKind);
            }
        }

        protected void CreateIndexedDatabase(
            string path,
            int count,
            IndexKeyKind keyKind,
            Func<int, int> keySelector = null)
        {
            using var database = OpenDatabase(path);
            var collection = GetCollection(database);

            collection.EnsureIndex(IndexField, BsonExpression.Create(IndexField));
            collection.InsertBulk(CreateDocuments(count, keyKind, keySelector));
            database.Checkpoint();
        }

        protected void CreateUnindexedDatabase(string path, int count, IndexKeyKind keyKind)
        {
            using var database = OpenDatabase(path);
            var collection = GetCollection(database);

            collection.InsertBulk(CreateDocuments(count, keyKind));
            database.Checkpoint();
        }

        protected void RestoreTemplate()
        {
            DeleteFile(DatabasePath);
            File.Copy(TemplatePath, DatabasePath, true);
        }

        protected void DeleteDatabaseFiles()
        {
            DeleteFile(DatabasePath);
            DeleteFile(TemplatePath);
        }

        protected void DeleteWorkingDatabase()
        {
            DeleteFile(DatabasePath);
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
