using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using LiteDB.Generated;

using static LiteDB.AotSmokeTests.SmokeAssert;

#nullable enable
namespace LiteDB.AotSmokeTests
{
    /// <summary>
    /// Exercises engine-level runtime features that a trimmed or Native AOT build must keep working:
    /// index management and plan selection, transactions, the SQL command surface, maintenance
    /// pragmas, and encrypted file databases.
    /// </summary>
    internal static class EngineFeatureScenarios
    {
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming",
            "IL2026",
            Justification = "The source generator emits direct access to every member used by these expression trees, so their accessors remain rooted.")]
        public static void RunIndexScenario()
        {
            using var file = new TemporaryDatabaseFile();
            var mapper = new FailOnGenericConversionMapper();
            LiteDbGeneratedMappings.Register(mapper);

            using var database = new LiteDatabase(file.Path, mapper);
            var records = database.GetGeneratedCollection<AotIndexedRecord>("aot_indexed_records");
            records.Insert(
            [
                new AotIndexedRecord { Id = 1, Category = "hardware", Serial = "SN-1", Quantity = 10 },
                new AotIndexedRecord { Id = 2, Category = "hardware", Serial = "SN-2", Quantity = 4 },
                new AotIndexedRecord { Id = 3, Category = "software", Serial = "SN-3", Quantity = 7 }
            ]);

            Console.WriteLine("  [5.1] Create generated indexes from LINQ key selectors.");
            RequireCheck("EnsureIndex from key selector", records.EnsureIndex(r => r.Category));
            RequireCheck("EnsureIndex is idempotent", records.EnsureIndex(r => r.Category) == false);
            RequireCheck("named EnsureIndex", records.EnsureIndex("idx_quantity", r => r.Quantity));
            RequireCheck("unique EnsureIndex", records.EnsureIndex(r => r.Serial, unique: true));
            Console.WriteLine("        Passed: derived index names, idempotent creation, named indexes, and unique indexes.");

            Console.WriteLine("  [5.2] Verify index selection through the query plan.");
            var indexedPlan = records.Query().Where(r => r.Category == "software").GetPlan();
            var quantityPlan = records.Query().Where(r => r.Quantity > 5).GetPlan();
            var identifierPlan = records.Query().Where(r => r.Id == 1).GetPlan();
            RequireCheck("plan uses derived index", indexedPlan["index"]["name"].AsString == "Category");
            RequireCheck("plan reports the index expression", indexedPlan["index"]["expr"].AsString == "$.Category");
            RequireCheck("plan uses named index", quantityPlan["index"]["name"].AsString == "idx_quantity");
            RequireCheck("plan uses the primary key", identifierPlan["index"]["name"].AsString == "_id");
            RequireCheck("plan reports the collection", indexedPlan["collection"].AsString == "aot_indexed_records");
            Console.WriteLine("        Passed: derived, named, and primary-key index selection in the execution plan.");

            Console.WriteLine("  [5.3] Enforce unique indexes and drop an index.");
            var duplicateKeyRejected = false;

            try
            {
                records.Insert(new AotIndexedRecord { Id = 4, Category = "software", Serial = "SN-1", Quantity = 1 });
            }
            catch (LiteException exception)
            {
                duplicateKeyRejected = exception.ErrorCode == LiteException.INDEX_DUPLICATE_KEY;
            }

            RequireCheck("unique index rejects a duplicate key", duplicateKeyRejected);
            RequireCheck("duplicate key did not change the collection", records.Count() == 3);
            RequireCheck("DropIndex", records.DropIndex("Category"));
            RequireCheck("DropIndex is idempotent", records.DropIndex("Category") == false);
            RequireCheck("plan falls back to a full scan",
                records.Query().Where(r => r.Category == "software").GetPlan()["index"]["name"].AsString == "_id");
            RequireCheck("results survive the dropped index",
                records.Query().Where(r => r.Category == "software").ToArray().Length == 1);
            Console.WriteLine("        Passed: unique-key rejection, index removal, and post-removal query execution.");

            Console.WriteLine("  [5.4] Index BSON document collections with explicit expressions.");
            var documents = database.GetCollection("aot_index_documents");
            documents.Insert(new BsonDocument { ["_id"] = 1, ["score"] = 5, ["tags"] = new BsonArray { "a", "b" } });
            documents.Insert(new BsonDocument { ["_id"] = 2, ["score"] = 9, ["tags"] = new BsonArray { "b", "c" } });
            RequireCheck("document index creation", documents.EnsureIndex("idx_score", BsonExpression.Create("$.score")));
            RequireCheck("multikey index creation", documents.EnsureIndex("idx_tags", BsonExpression.Create("$.tags[*]")));
            RequireCheck("document plan uses the index",
                documents.Query().Where("$.score = 9").GetPlan()["index"]["name"].AsString == "idx_score");
            RequireCheck("multikey lookup",
                documents.Query().Where("$.tags ANY = 'b'").ToArray().Length == 2);
            Console.WriteLine("        Passed: scalar and multikey document indexes and their query plans.");
        }

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming",
            "IL2026",
            Justification = "The source generator emits direct access to every member used by these expression trees, so their accessors remain rooted.")]
        public static void RunTransactionScenario()
        {
            using var file = new TemporaryDatabaseFile();
            var mapper = new FailOnGenericConversionMapper();
            LiteDbGeneratedMappings.Register(mapper);

            using var database = new LiteDatabase(file.Path, mapper);
            var documents = database.GetCollection("aot_transactions");
            documents.Insert(new BsonDocument { ["_id"] = 1, ["name"] = "committed" });

            Console.WriteLine("  [6.1] Roll back an explicit transaction.");
            RequireCheck("BeginTrans", database.BeginTrans());
            RequireCheck("nested BeginTrans is a no-op", database.BeginTrans() == false);
            documents.Insert(new BsonDocument { ["_id"] = 2, ["name"] = "rolled-back" });
            RequireCheck("uncommitted write is visible inside the transaction", documents.Count() == 2);
            RequireCheck("Rollback", database.Rollback());
            RequireCheck("rolled-back write is gone", documents.Count() == 1 && documents.FindById(2) is null);
            Console.WriteLine("        Passed: transaction start, nested start, and rollback isolation.");

            Console.WriteLine("  [6.2] Commit an explicit transaction across typed and document collections.");
            var records = database.GetGeneratedCollection<AotIndexedRecord>("aot_transaction_records");
            RequireCheck("BeginTrans before mixed writes", database.BeginTrans());
            documents.Insert(new BsonDocument { ["_id"] = 3, ["name"] = "persisted" });
            records.Insert(new AotIndexedRecord { Id = 10, Category = "hardware", Serial = "SN-10", Quantity = 2 });
            RequireCheck("Commit", database.Commit());
            RequireCheck("committed document write", documents.FindById(3)?["name"].AsString == "persisted");
            RequireCheck("committed typed write", records.FindById(10)?.Serial == "SN-10");
            RequireCheck("Rollback without a transaction", database.Rollback() == false);
            RequireCheck("Commit without a transaction", database.Commit() == false);
            Console.WriteLine("        Passed: commit durability across collection types and no-op commit and rollback.");

            Console.WriteLine("  [6.3] Drive a transaction through SQL statements.");
            using (database.Execute("BEGIN"))
            {
            }

            using (database.Execute("INSERT INTO aot_transactions VALUES {_id: 4, name: 'sql-rollback'}"))
            {
            }

            using (database.Execute("ROLLBACK"))
            {
            }

            RequireCheck("SQL rollback discarded the write", documents.FindById(4) is null);
            Console.WriteLine("        Passed: SQL BEGIN and ROLLBACK statements.");
        }

        public static void RunSqlScenario()
        {
            using var file = new TemporaryDatabaseFile();
            using var database = new LiteDatabase(file.Path);

            Console.WriteLine("  [7.1] Insert documents with an explicit auto-identifier type.");
            using (var reader = database.Execute(
                "INSERT INTO sales:int VALUES " +
                "{region: 'north', product: 'widget', amount: 10, sold: true}, " +
                "{region: 'north', product: 'gadget', amount: 25, sold: true}, " +
                "{region: 'south', product: 'widget', amount: 5, sold: false}, " +
                "{region: 'south', product: 'tool', amount: 40, sold: true}"))
            {
                RequireCheck("INSERT INTO returned the inserted count", reader.ToArray().Single().AsInt32 == 4);
            }

            var sales = database.GetCollection("sales");
            RequireCheck("auto identifiers are integers", sales.FindAll().All(document => document["_id"].IsInt32));
            Console.WriteLine("        Passed: multi-document INSERT with an integer auto-identifier.");

            Console.WriteLine("  [7.2] Project, filter, sort, and page with SELECT.");
            using (var reader = database.Execute("SELECT { product: $.product, doubled: $.amount * 2 } FROM sales WHERE $.amount >= @0 ORDER BY $.amount DESC LIMIT 2", 10))
            {
                var rows = reader.ToArray();
                RequireCheck("SELECT projection and ordering",
                    rows.Length == 2 &&
                    rows[0].AsDocument["product"].AsString == "tool" &&
                    rows[0].AsDocument["doubled"].AsInt32 == 80 &&
                    rows[1].AsDocument["product"].AsString == "gadget");
            }

            using (var reader = database.Execute("SELECT $.product FROM sales ORDER BY $.amount LIMIT 1 OFFSET 1"))
            {
                RequireCheck("SELECT with OFFSET", reader.ToArray().Single().AsDocument["product"].AsString == "widget");
            }

            Console.WriteLine("        Passed: document projection, parameters, ordering, LIMIT, and OFFSET.");

            Console.WriteLine("  [7.3] Aggregate with GROUP BY and HAVING.");
            using (var reader = database.Execute(
                "SELECT { region: @key, total: SUM(*.amount), items: COUNT(*) } FROM sales GROUP BY $.region ORDER BY @key"))
            {
                var rows = reader.ToArray().Select(value => value.AsDocument).ToArray();
                RequireCheck("GROUP BY produced one row per key", rows.Length == 2);
                RequireCheck("GROUP BY aggregates",
                    rows[0]["region"].AsString == "north" &&
                    rows[0]["total"].AsInt32 == 35 &&
                    rows[0]["items"].AsInt32 == 2 &&
                    rows[1]["region"].AsString == "south" &&
                    rows[1]["total"].AsInt32 == 45);
            }

            using (var reader = database.Execute(
                "SELECT { region: @key, total: SUM(*.amount) } FROM sales GROUP BY $.region HAVING SUM(*.amount) > 40"))
            {
                var rows = reader.ToArray();
                RequireCheck("HAVING filtered the groups",
                    rows.Length == 1 && rows[0].AsDocument["region"].AsString == "south");
            }

            Console.WriteLine("        Passed: grouped aggregation, aggregate ordering, and HAVING filters.");

            Console.WriteLine("  [7.4] Run UPDATE, DELETE, index, and collection statements.");
            using (var reader = database.Execute("UPDATE sales SET amount = $.amount + 1 WHERE $.region = 'south'"))
            {
                RequireCheck("UPDATE returned the affected count", reader.ToArray().Single().AsInt32 == 2);
            }

            using (var reader = database.Execute("CREATE INDEX idx_region ON sales($.region)"))
            {
                RequireCheck("CREATE INDEX", reader.ToArray().Single().AsBoolean);
            }

            using (var reader = database.Execute("SELECT $ INTO sold_sales:int FROM sales WHERE $.sold = true"))
            {
                RequireCheck("SELECT INTO copied the matching documents", reader.ToArray().Single().AsInt32 == 3);
            }

            using (var reader = database.Execute("DELETE sales WHERE $.sold = false"))
            {
                RequireCheck("DELETE returned the affected count", reader.ToArray().Single().AsInt32 == 1);
            }

            RequireCheck("statements changed the stored data",
                sales.Count() == 3 && database.GetCollection("sold_sales").Count() == 3);
            RequireCheck("UPDATE applied the expression",
                sales.FindOne("$.product = 'tool'")["amount"].AsInt32 == 41);
            Console.WriteLine("        Passed: UPDATE, CREATE INDEX, SELECT INTO, and DELETE statements.");

            Console.WriteLine("  [7.5] Explain a query and evaluate a SELECT without a source collection.");
            using (var reader = database.Execute("EXPLAIN SELECT $ FROM sales WHERE $.region = 'north'"))
            {
                var plan = reader.ToArray().Single().AsDocument;
                RequireCheck("EXPLAIN returned a plan", plan["index"]["name"].AsString == "idx_region");
            }

            using (var reader = database.Execute("SELECT { upper: UPPER('litedb'), length: LENGTH('litedb'), sum: SUM([1, 2, 3]) }"))
            {
                var row = reader.ToArray().Single().AsDocument;
                RequireCheck("collection-less SELECT",
                    row["upper"].AsString == "LITEDB" && row["length"].AsInt32 == 6 && row["sum"].AsInt32 == 6);
            }

            Console.WriteLine("        Passed: EXPLAIN plans and expression-only SELECT evaluation.");
        }

        public static void RunMaintenanceScenario()
        {
            using var file = new TemporaryDatabaseFile();
            using var database = new LiteDatabase(file.Path);
            var collection = database.GetCollection("aot_maintenance");
            collection.Insert(Enumerable.Range(1, 50).Select(index => new BsonDocument
            {
                ["_id"] = index,
                ["name"] = $"document-{index:D3}",
                ["payload"] = new byte[128]
            }));

            Console.WriteLine("  [8.1] Read and write engine pragmas.");
            database.UserVersion = 7;
            database.UtcDate = true;
            database.Timeout = TimeSpan.FromSeconds(90);
            RequireCheck("UserVersion round trip", database.UserVersion == 7);
            RequireCheck("UserVersion pragma", database.Pragma("USER_VERSION").AsInt32 == 7);
            RequireCheck("UtcDate pragma", database.UtcDate && database.Pragma("UTC_DATE").AsBoolean);
            RequireCheck("Timeout pragma", database.Timeout == TimeSpan.FromSeconds(90));
            database.Pragma("USER_VERSION", 9);
            RequireCheck("Pragma write", database.UserVersion == 9);
            Console.WriteLine("        Passed: user version, UTC date, and timeout pragmas.");

            Console.WriteLine("  [8.2] Enumerate, rename, and drop collections.");
            RequireCheck("collection listing", database.GetCollectionNames().Contains("aot_maintenance"));
            RequireCheck("CollectionExists is case insensitive", database.CollectionExists("AOT_MAINTENANCE"));
            RequireCheck("RenameCollection", database.RenameCollection("aot_maintenance", "aot_renamed"));
            RequireCheck("renamed collection keeps its data", database.GetCollection("aot_renamed").Count() == 50);
            RequireCheck("renaming a missing collection fails", database.RenameCollection("aot_maintenance", "aot_other") == false);
            RequireCheck("DropCollection", database.DropCollection("aot_renamed"));
            RequireCheck("dropped collection is gone", database.CollectionExists("aot_renamed") == false);
            RequireCheck("dropping a missing collection fails", database.DropCollection("aot_renamed") == false);
            Console.WriteLine("        Passed: listing, case-insensitive lookup, renaming, and dropping collections.");

            Console.WriteLine("  [8.3] Checkpoint and rebuild the data file.");
            var survivors = database.GetCollection("aot_survivors");
            survivors.Insert(new BsonDocument { ["_id"] = 1, ["name"] = "survivor" });
            database.Checkpoint();
            RequireCheck("checkpoint kept the data", survivors.FindById(1)?["name"].AsString == "survivor");
            database.Rebuild();
            RequireCheck("rebuild kept the data", survivors.FindById(1)?["name"].AsString == "survivor");
            RequireCheck("rebuild kept the user version", database.UserVersion == 9);
            Console.WriteLine("        Passed: checkpoint and rebuild with preserved documents and pragmas.");
        }

        public static void RunEncryptedDatabaseScenario()
        {
            using var file = new TemporaryDatabaseFile();
            const string password = "aot-smoke-password";

            Console.WriteLine("  [9.1] Write and read an AES-encrypted file database.");
            using (var database = new LiteDatabase($"Filename={file.Path};Password={password}"))
            {
                var collection = database.GetCollection("aot_encrypted");
                collection.Insert(new BsonDocument { ["_id"] = 1, ["secret"] = "classified" });
                RequireCheck("encrypted write", collection.Count() == 1);
            }

            using (var database = new LiteDatabase($"Filename={file.Path};Password={password}"))
            {
                RequireCheck("encrypted read",
                    database.GetCollection("aot_encrypted").FindById(1)?["secret"].AsString == "classified");
            }

            Console.WriteLine("        Passed: encrypted persistence and reopening with the correct password.");

            Console.WriteLine("  [9.2] Reject an incorrect password.");
            RequireThrows<LiteException>(
                () =>
                {
                    using var database = new LiteDatabase($"Filename={file.Path};Password=wrong-password");
                    database.GetCollection("aot_encrypted").Count();
                },
                "The Native AOT encrypted database accepted an incorrect password.");
            RequireThrows<LiteException>(
                () =>
                {
                    using var database = new LiteDatabase($"Filename={file.Path}");
                    database.GetCollection("aot_encrypted").Count();
                },
                "The Native AOT encrypted database opened without a password.");
            Console.WriteLine("        Passed: incorrect and missing password rejection.");
        }
    }

    [BsonSourceGenerated]
    public sealed class AotIndexedRecord
    {
        public int Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Serial { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    /// <summary>
    /// Creates a unique temporary database path and removes the data and log files afterwards.
    /// </summary>
    internal sealed class TemporaryDatabaseFile : IDisposable
    {
        public TemporaryDatabaseFile()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"litedb-aot-{Guid.NewGuid():N}.db");
        }

        public string Path { get; }

        public void Dispose()
        {
            Delete(Path);
            Delete(System.IO.Path.ChangeExtension(Path, null) + "-log.db");
        }

        private static void Delete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // A locked temporary file must never fail the smoke test transcript.
            }
        }
    }
}
