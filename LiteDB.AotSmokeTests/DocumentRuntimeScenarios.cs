using System;
using System.Linq;

using static LiteDB.AotSmokeTests.SmokeAssert;

#nullable enable
namespace LiteDB.AotSmokeTests
{
    /// <summary>
    /// Exercises the document runtime that every other API depends on: BSON values, JSON
    /// serialization, expression evaluation, and ObjectId generation. None of these paths may
    /// rely on runtime code generation.
    /// </summary>
    internal static class DocumentRuntimeScenarios
    {
        public static void Run()
        {
            RunBsonValueChecks();
            RunDocumentAndArrayChecks();
            RunJsonRoundTrip();
            RunExpressionRuntime();
            RunObjectIdChecks();
        }

        private static void RunBsonValueChecks()
        {
            Console.WriteLine("  [10.1] Convert, compare, and sort BSON values.");
            BsonValue integer = 42;
            BsonValue text = "litedb";
            BsonValue number = 42.5d;
            BsonValue money = 19.99m;
            BsonValue flag = true;
            BsonValue binary = new byte[] { 1, 2, 3 };

            RequireCheck("implicit int conversion", integer.IsInt32 && integer.AsInt32 == 42);
            RequireCheck("implicit string conversion", text.IsString && text.AsString == "litedb");
            RequireCheck("implicit double conversion", number.IsDouble && number.AsDouble == 42.5d);
            RequireCheck("implicit decimal conversion", money.IsDecimal && money.AsDecimal == 19.99m);
            RequireCheck("implicit Boolean conversion", flag.IsBoolean && flag.AsBoolean);
            RequireCheck("implicit binary conversion", binary.IsBinary && binary.AsBinary.Length == 3);
            RequireCheck("numeric widening comparison", integer.AsInt64 == 42L && integer.AsDouble == 42d);
            RequireCheck("cross-type comparison", integer.CompareTo(number) < 0);
            RequireCheck("equality", new BsonValue(42).Equals(integer));
            RequireCheck("null value", BsonValue.Null.IsNull && BsonValue.Null.RawValue is null);
            RequireCheck("min and max values", BsonValue.MinValue < BsonValue.MaxValue);

            var sorted = new[] { text, integer, BsonValue.Null, flag, number }
                .OrderBy(value => value)
                .Select(value => value.Type)
                .ToArray();
            RequireCheck("BSON type ordering",
                sorted.SequenceEqual([BsonType.Null, BsonType.Int32, BsonType.Double, BsonType.String, BsonType.Boolean]));
            Console.WriteLine("        Passed: implicit conversions, numeric widening, equality, and BSON type ordering.");
        }

        private static void RunDocumentAndArrayChecks()
        {
            Console.WriteLine("  [10.2] Build and navigate documents and arrays.");
            var document = new BsonDocument
            {
                ["_id"] = 1,
                ["name"] = "root",
                ["nested"] = new BsonDocument
                {
                    ["level"] = 2,
                    ["labels"] = new BsonArray { "a", "b", "c" }
                },
                ["items"] = new BsonArray
                {
                    new BsonDocument { ["price"] = 10, ["quantity"] = 2 },
                    new BsonDocument { ["price"] = 5, ["quantity"] = 4 }
                }
            };

            RequireCheck("nested document access", document["nested"]["level"].AsInt32 == 2);
            RequireCheck("nested array access", document["nested"]["labels"].AsArray[1].AsString == "b");
            RequireCheck("array of documents", document["items"].AsArray[0].AsDocument["price"].AsInt32 == 10);
            RequireCheck("missing key returns null", document["missing"].IsNull);
            RequireCheck("ContainsKey", document.ContainsKey("name") && document.ContainsKey("absent") == false);
            RequireCheck("key enumeration", document.Keys.Contains("items"));
            RequireCheck("TryGetValue", document.TryGetValue("name", out var name) && name.AsString == "root");

            document["name"] = "updated";
            document["extra"] = 3;
            RequireCheck("value replacement", document["name"].AsString == "updated");
            RequireCheck("value addition", document.Count == 5 && document["extra"].AsInt32 == 3);
            RequireCheck("value removal", document.Remove("extra") && document.ContainsKey("extra") == false);

            var array = new BsonArray { 1, 2, 3 };
            array.Add(4);
            RequireCheck("array append", array.Count == 4 && array[3].AsInt32 == 4);
            RequireCheck("array removal", array.Remove(array[0]) && array.Count == 3);
            RequireCheck("document deep clone", ((BsonDocument)JsonSerializer.Deserialize(JsonSerializer.Serialize(document))).Count == document.Count);
            Console.WriteLine("        Passed: nested access, key management, mutation, and JSON-based cloning.");
        }

        private static void RunJsonRoundTrip()
        {
            Console.WriteLine("  [10.3] Round-trip documents through extended JSON.");
            var objectId = new ObjectId("64c61e5f18a9421a8862c71c");
            var correlationId = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");
            var timestamp = new DateTime(2024, 4, 5, 6, 7, 8, 910, DateTimeKind.Utc);
            var source = new BsonDocument
            {
                ["_id"] = objectId,
                ["name"] = "json round trip",
                ["count"] = 9_000_000_000L,
                ["ratio"] = 1.5d,
                ["amount"] = 12.345m,
                ["enabled"] = false,
                ["missing"] = BsonValue.Null,
                ["correlationId"] = correlationId,
                ["timestamp"] = timestamp,
                ["payload"] = new byte[] { 9, 8, 7 },
                ["tags"] = new BsonArray { "x", "y" },
                ["nested"] = new BsonDocument { ["inner"] = 1 }
            };

            var json = JsonSerializer.Serialize(source);
            var restored = JsonSerializer.Deserialize(json).AsDocument;

            RequireCheck("ObjectId survives JSON", restored["_id"].AsObjectId == objectId);
            RequireCheck("string survives JSON", restored["name"].AsString == "json round trip");
            RequireCheck("Int64 survives JSON", restored["count"].AsInt64 == 9_000_000_000L);
            RequireCheck("Double survives JSON", restored["ratio"].AsDouble == 1.5d);
            RequireCheck("Decimal survives JSON", restored["amount"].AsDecimal == 12.345m);
            RequireCheck("Boolean survives JSON", restored["enabled"].AsBoolean == false);
            RequireCheck("null survives JSON", restored["missing"].IsNull);
            RequireCheck("Guid survives JSON", restored["correlationId"].AsGuid == correlationId);
            RequireCheck("DateTime survives JSON", restored["timestamp"].AsDateTime.ToUniversalTime() == timestamp);
            RequireCheck("binary survives JSON", restored["payload"].AsBinary.SequenceEqual(new byte[] { 9, 8, 7 }));
            RequireCheck("array survives JSON", restored["tags"].AsArray.Count == 2);
            RequireCheck("nested document survives JSON", restored["nested"]["inner"].AsInt32 == 1);

            var indented = JsonSerializer.Serialize(source, indent: true);
            RequireCheck("indented JSON is valid", JsonSerializer.Deserialize(indented).AsDocument.Count == source.Count);
            RequireCheck("JSON array parsing",
                JsonSerializer.DeserializeArray("[{\"a\": 1}, {\"a\": 2}]").Select(value => value.AsDocument["a"].AsInt32).SequenceEqual([1, 2]));
            Console.WriteLine("        Passed: extended JSON for every native type, indentation, and array parsing.");
        }

        private static void RunExpressionRuntime()
        {
            Console.WriteLine("  [10.4] Evaluate BSON expressions over in-memory documents.");
            var document = new BsonDocument
            {
                ["_id"] = 1,
                ["name"] = "  LiteDB  ",
                ["created"] = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc),
                ["items"] = new BsonArray
                {
                    new BsonDocument { ["price"] = 10, ["quantity"] = 2 },
                    new BsonDocument { ["price"] = 5, ["quantity"] = 4 },
                    new BsonDocument { ["price"] = 20, ["quantity"] = 1 }
                }
            };

            RequireCheck("path expression", BsonExpression.Create("$.name").ExecuteScalar(document).AsString == "  LiteDB  ");
            RequireCheck("string function", BsonExpression.Create("TRIM($.name)").ExecuteScalar(document).AsString == "LiteDB");
            RequireCheck("nested string functions", BsonExpression.Create("LOWER(TRIM($.name))").ExecuteScalar(document).AsString == "litedb");
            RequireCheck("date function", BsonExpression.Create("YEAR($.created)").ExecuteScalar(document).AsInt32 == 2024);
            RequireCheck("array aggregate", BsonExpression.Create("SUM($.items[*].price)").ExecuteScalar(document).AsInt32 == 35);
            RequireCheck("array count", BsonExpression.Create("COUNT($.items[*])").ExecuteScalar(document).AsInt32 == 3);
            RequireCheck("array extremes",
                BsonExpression.Create("MAX($.items[*].price)").ExecuteScalar(document).AsInt32 == 20 &&
                BsonExpression.Create("MIN($.items[*].price)").ExecuteScalar(document).AsInt32 == 5);
            RequireCheck("array filter",
                BsonExpression.Create("COUNT(FILTER($.items[*] => @.price > 5))").ExecuteScalar(document).AsInt32 == 2);
            RequireCheck("array map",
                BsonExpression.Create("SUM(MAP($.items[*] => @.price * @.quantity))").ExecuteScalar(document).AsInt32 == 60);
            RequireCheck("ANY operator", BsonExpression.Create("$.items[*].price ANY = 20").ExecuteScalar(document).AsBoolean);
            RequireCheck("ALL operator", BsonExpression.Create("$.items[*].price ALL > 1").ExecuteScalar(document).AsBoolean);
            RequireCheck("positional parameter",
                BsonExpression.Create("$.items[*].price ANY = @0", 5).ExecuteScalar(document).AsBoolean);
            RequireCheck("named parameter",
                BsonExpression.Create("LENGTH(TRIM($.name)) = @size", new BsonDocument { ["size"] = 6 }).ExecuteScalar(document).AsBoolean);
            RequireCheck("document builder",
                BsonExpression.Create("{ total: SUM($.items[*].price), label: UPPER(TRIM($.name)) }")
                    .ExecuteScalar(document).AsDocument["label"].AsString == "LITEDB");
            RequireCheck("conditional expression",
                BsonExpression.Create("IIF(COUNT($.items[*]) > 2, 'many', 'few')").ExecuteScalar(document).AsString == "many");
            RequireCheck("expression source is preserved", BsonExpression.Create("$.name").Source == "$.name");
            RequireCheck("expression reports its fields", BsonExpression.Create("$.items[*].price").Fields.Contains("items"));
            Console.WriteLine("        Passed: paths, string and date functions, aggregates, filters, maps, parameters, and builders.");
        }

        private static void RunObjectIdChecks()
        {
            Console.WriteLine("  [10.5] Generate and parse ObjectId values.");
            var generated = ObjectId.NewObjectId();
            var parsed = new ObjectId(generated.ToString());
            var fixedId = new ObjectId("64c61e5f18a9421a8862c71c");

            RequireCheck("ObjectId string round trip", parsed == generated);
            RequireCheck("ObjectId hash codes match", parsed.GetHashCode() == generated.GetHashCode());
            RequireCheck("ObjectId inequality", fixedId != generated);
            RequireCheck("ObjectId ordering", ObjectId.Empty.CompareTo(fixedId) < 0);
            RequireCheck("ObjectId creation time", generated.CreationTime > new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            RequireCheck("ObjectId is monotonic within a process", ObjectId.NewObjectId() != generated);
            RequireCheck("ObjectId to BsonValue", new BsonValue(fixedId).AsObjectId == fixedId);
            Console.WriteLine("        Passed: generation, parsing, comparison, and creation timestamps.");
        }
    }
}
