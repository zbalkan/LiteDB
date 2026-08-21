using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LiteDB;
using LiteDB.AOT;

if (args.Contains("--benchmark", StringComparer.Ordinal))
{
    return RunBenchmark(args.Contains("--reflection", StringComparer.Ordinal));
}

var mapper = new BsonMapper()
    .UseGeneratedMappers(new LiteDbGeneratedMapperProvider(), strict: true);

var path = Path.Combine(Path.GetTempPath(), "litedb-aot-smoke-" + Guid.NewGuid().ToString("N") + ".db");

try
{
    // 1. Untyped Document, SQL, and Expression Scenarios
    using (var database = new LiteDatabase(path, mapper))
    {
        RunDocumentAndExpressionScenarios(database);
    }

    // 2. Stream-backed Scenario
    RunStreamBackedScenario(mapper);

    // 3. Strongly-Typed Generated Entities Scenario
    RunGeneratedEntitiesScenario(path, mapper);

    Console.WriteLine("Native AOT smoke test passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Native AOT smoke test failed: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}
finally
{
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}

static void RunDocumentAndExpressionScenarios(LiteDatabase database)
{
    var collection = database.GetCollection("items");

    collection.Insert(new BsonDocument
    {
        ["_id"] = 1,
        ["name"] = "native-aot",
        ["score"] = 2,
        ["values"] = new BsonArray { 1, 2, 3 }
    });
    collection.Insert(new BsonDocument
    {
        ["_id"] = 2,
        ["name"] = "secondary",
        ["score"] = 4,
        ["values"] = new BsonArray { 4, 5, 6 }
    });

    var item = collection.FindById(1);
    Require(item is not null && item["name"].AsString == "native-aot", "The Native AOT database round trip failed.");

    Require(collection.EnsureIndex("score", BsonExpression.Create("$.score")), "The Native AOT index creation failed.");

    var between = collection.Query()
        .Where("$.score BETWEEN @0 AND @1", 1, 3)
        .ToArray();
    Require(between.Length == 1 && between[0]["_id"].AsInt32 == 1, "The Native AOT BETWEEN query failed.");

    var arrayMembership = collection.Query()
        .Where("2 IN [1, 2, 3]")
        .ToArray();
    Require(arrayMembership.Length == 2, "The Native AOT array expression query failed.");

    using (var projectionReader = database.Execute("SELECT name AS label, score + 1 AS next FROM items WHERE score BETWEEN 1 AND 3"))
    {
        var projections = projectionReader.ToArray();
        Require(projections.Length == 1 &&
                projections[0].AsDocument["label"].AsString == "native-aot" &&
                projections[0].AsDocument["next"].AsInt32 == 3,
            "The Native AOT SELECT document builder failed.");
    }

    using (database.Execute("UPDATE items SET name = UPPER($.name), score = $.score + 10 WHERE _id = 1"))
    {
    }

    var updated = collection.FindById(1);
    Require(updated is not null &&
            updated["name"].AsString == "NATIVE-AOT" &&
            updated["score"].AsInt32 == 12,
        "The Native AOT UPDATE document builder failed.");

    Require(collection.Upsert(new BsonDocument
    {
        ["_id"] = 3,
        ["name"] = "upserted",
        ["score"] = 9
    }), "The Native AOT upsert failed.");
    Require(collection.Delete(3), "The Native AOT delete failed.");
    Require(collection.FindById(3) is null, "The Native AOT delete verification failed.");
}

static void RunStreamBackedScenario(BsonMapper mapper)
{
    using var stream = new MemoryStream();
    using var database = new LiteDatabase(stream, mapper);

    var collection = database.GetCollection("streamitems");
    collection.Insert(new BsonDocument
    {
        ["_id"] = 1,
        ["name"] = "stream-backed"
    });

    var item = collection.FindById(1);
    Require(item is not null && item["name"].AsString == "stream-backed", "The Native AOT stream-backed database round trip failed.");
}

static void RunGeneratedEntitiesScenario(string databasePath, BsonMapper mapper)
{
    using (var database = new LiteDatabase(databasePath, mapper))
    {
        var collection = database.GetCollection<AotPerson>("people");

        collection.Insert(new AotPerson
        {
            Id = 7,
            Name = "Ada",
            Address = new AotAddress
            {
                City = "London"
            },
            Notes = new List<AotNote>
            {
                new AotNote { Text = "first" },
                new AotNote { Text = "second" }
            },
            NoteArray = new[]
            {
                new AotNote { Text = "first" },
                new AotNote { Text = "second" }
            },
            NoteSet = new HashSet<AotNote>
            {
                new AotNote { Text = "first" },
                new AotNote { Text = "second" }
            },
            NoteDict = new Dictionary<string, AotNote>
            {
                ["first"] = new AotNote { Text = "first" },
                ["second"] = new AotNote { Text = "second" }
            },
            ReadOnlyNotes = new List<AotNote>
            {
                new AotNote { Text = "first" },
                new AotNote { Text = "second" }
            }
        });

        var orders = database.GetCollection<AotImmutableOrder>("orders");
        orders.Insert(new AotImmutableOrder(42, "ORD-42"));
    }

    using (var database = new LiteDatabase(databasePath, mapper))
    {
        var restored = database.GetCollection<AotPerson>("people").FindAll().FirstOrDefault();
        Require(restored != null, "missing person.");
        Require(restored.Id == 7 && restored.Name == "Ada", "flat fields.");
        Require(restored.Address != null && restored.Address.City == "London", "nested address.");
        Require(restored.Notes != null && restored.Notes.Count == 2 && restored.Notes[1].Text == "second", "list materialization.");
        Require(restored.NoteArray != null && restored.NoteArray.Length == 2 && restored.NoteArray[1].Text == "second", "array materialization.");
        Require(restored.NoteSet != null && restored.NoteSet.Count == 2, "hashset materialization.");
        Require(restored.NoteDict != null && restored.NoteDict.Count == 2 && restored.NoteDict["second"].Text == "second", "dictionary materialization.");
        Require(restored.ReadOnlyNotes != null && restored.ReadOnlyNotes.Count == 2 && restored.ReadOnlyNotes[1].Text == "second", "read-only list materialization.");

        var order = database.GetCollection<AotImmutableOrder>("orders").FindById(42);
        Require(order != null && order.Id == 42 && order.OrderNumber == "ORD-42", "immutable constructor mapping.");
    }
}

static int RunBenchmark(bool includeReflection)
{
    const int iterations = 10_000;
    var sample = new AotPerson
    {
        Id = 7,
        Name = "Ada",
        Address = new AotAddress { City = "London" },
        Notes = new List<AotNote>
        {
            new AotNote { Text = "first" },
            new AotNote { Text = "second" }
        }
    };

    var generatedMapper = new BsonMapper()
        .UseGeneratedMappers(new LiteDbGeneratedMapperProvider());
    RunMappingBenchmark("generated", generatedMapper, sample, iterations);

    if (includeReflection)
    {
        RunMappingBenchmark("reflection", new BsonMapper(), sample, iterations);
    }
    else
    {
        Console.WriteLine("reflection: skipped (pass --reflection in a managed process)");
    }

    return 0;
}

static void RunMappingBenchmark(string name, BsonMapper mapper, AotPerson sample, int iterations)
{
    var warmup = mapper.ToDocument(sample);
    _ = mapper.Deserialize<AotPerson>(warmup);

    long checksum = 0;
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        var document = mapper.ToDocument(sample);
        var restored = mapper.Deserialize<AotPerson>(document);
        checksum += restored.Id + restored.Notes.Count + document.Count;
    }

    stopwatch.Stop();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    var nanosecondsPerOperation = stopwatch.Elapsed.TotalMilliseconds * 1_000_000 / iterations;

    Console.WriteLine($"{name}: {iterations:N0} round-trips, {stopwatch.Elapsed.TotalMilliseconds:N1} ms, {nanosecondsPerOperation:N0} ns/op, {allocated:N0} bytes, checksum {checksum}");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Native AOT smoke test failed: {message}");
    }
}

// -------------------------------------------------------------
// Source Generated Entities
// -------------------------------------------------------------

[LiteEntity]
public sealed class AotPerson
{
    [BsonId]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public AotAddress Address { get; set; } = new AotAddress();

    public List<AotNote> Notes { get; set; } = new List<AotNote>();

    public AotNote[] NoteArray { get; set; } = Array.Empty<AotNote>();

    public HashSet<AotNote> NoteSet { get; set; } = new HashSet<AotNote>();

    public Dictionary<string, AotNote> NoteDict { get; set; } = new Dictionary<string, AotNote>();

    public IReadOnlyList<AotNote> ReadOnlyNotes { get; set; } = new List<AotNote>();
}

[LiteEntity]
public sealed class AotAddress
{
    public string City { get; set; } = string.Empty;
}

[LiteEntity]
public sealed class AotNote
{
    public string Text { get; set; } = string.Empty;
}

[LiteEntity]
public sealed class AotImmutableOrder
{
    [BsonId]
    public int Id { get; }

    [BsonField("order_no")]
    public string OrderNumber { get; }

    public AotImmutableOrder(int id, string orderNumber)
    {
        Id = id;
        OrderNumber = orderNumber;
    }
}
