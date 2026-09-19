using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

using LiteDB.Generated;

using static LiteDB.AotSmokeTests.SmokeAssert;

#nullable enable
namespace LiteDB.AotSmokeTests
{
    /// <summary>
    /// Exercises the LINQ-to-BsonExpression translator and the fluent query surface over a
    /// source-generated collection. Every expression tree is translated without runtime code
    /// generation, so a trimmed or Native AOT build must produce the same results as a regular one.
    /// </summary>
    internal static class LinqQueryScenarios
    {
        private static readonly DateTime FirstCreatedAt = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime SecondCreatedAt = new DateTime(2024, 2, 20, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime ThirdCreatedAt = new DateTime(2023, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime FourthCreatedAt = new DateTime(2024, 5, 10, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime FifthCreatedAt = new DateTime(2024, 6, 20, 12, 0, 0, DateTimeKind.Utc);

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming",
            "IL2026",
            Justification = "The source generator emits direct access to every member used by these expression trees, so their accessors remain rooted.")]
        public static void Run()
        {
            using var file = new TemporaryDatabaseFile();
            var mapper = new FailOnGenericConversionMapper();
            LiteDbGeneratedMappings.Register(mapper);

            using var database = new LiteDatabase(file.Path, mapper);

            // Read dates back as UTC so the date-member assertions below do not depend on the host time zone.
            database.UtcDate = true;

            var records = database.GetGeneratedCollection<AotLinqRecord>("aot_linq_records");
            records.Insert(CreateSeedRecords());

            RunOperatorTranslation(records);
            RunStringTranslation(records);
            RunEnumAndDateTranslation(records);
            RunCaptureAndMembershipTranslation(records);
            RunOrderingPagingAndProjection(records);
            RunAggregatesAndSingleResults(records);
            RunFindOverloads(records);
            RunGroupingTranslation(records);
            RunBulkMutationTranslation(database, mapper);
        }

        private static AotLinqRecord[] CreateSeedRecords() =>
        [
            new AotLinqRecord
            {
                Id = 1,
                Name = "alpha widget",
                Category = "hardware",
                Quantity = 10,
                Price = 19.5d,
                Active = true,
                State = AotNativeScalarState.Captured,
                CreatedAt = FirstCreatedAt,
                Rank = 1,
                Tags = ["red", "blue"]
            },
            new AotLinqRecord
            {
                Id = 2,
                Name = "beta widget",
                Category = "hardware",
                Quantity = 4,
                Price = 7.25d,
                Active = false,
                State = AotNativeScalarState.Unknown,
                CreatedAt = SecondCreatedAt,
                Rank = null,
                Tags = ["blue"]
            },
            new AotLinqRecord
            {
                Id = 3,
                Name = "gamma gadget",
                Category = "software",
                Quantity = 7,
                Price = 45d,
                Active = true,
                State = AotNativeScalarState.Captured,
                CreatedAt = ThirdCreatedAt,
                Rank = 3,
                Tags = ["green"]
            },
            new AotLinqRecord
            {
                Id = 4,
                Name = "delta gadget",
                Category = "software",
                Quantity = 0,
                Price = 0d,
                Active = false,
                State = AotNativeScalarState.Unknown,
                CreatedAt = FourthCreatedAt,
                Rank = null,
                Tags = []
            },
            new AotLinqRecord
            {
                Id = 5,
                Name = "epsilon tool",
                Category = "services",
                Quantity = 25,
                Price = 99.99d,
                Active = true,
                State = AotNativeScalarState.Captured,
                CreatedAt = FifthCreatedAt,
                Rank = 5,
                Tags = ["red", "green", "blue"]
            }
        ];

        private static int[] Ids(this IEnumerable<AotLinqRecord> source) =>
            source.Select(record => record.Id).OrderBy(id => id).ToArray();

        private static int[] QueryIds(ILiteCollection<AotLinqRecord> records, Expression<Func<AotLinqRecord, bool>> predicate) =>
            records.Query().Where(predicate).ToArray().Ids();

        private static void RunOperatorTranslation(ILiteCollection<AotLinqRecord> records)
        {
            Console.WriteLine("  [4.1] Translate comparison, logical, and arithmetic operators.");
            RequireCheck("greater-than with AND", QueryIds(records, r => r.Quantity > 5 && r.Active).SequenceEqual([1, 3, 5]));
            RequireCheck("negated Boolean member", QueryIds(records, r => !r.Active).SequenceEqual([2, 4]));
            RequireCheck("inclusive range", QueryIds(records, r => r.Quantity >= 4 && r.Quantity <= 10).SequenceEqual([1, 2, 3]));
            RequireCheck("OR of equality tests", QueryIds(records, r => r.Category == "services" || r.Quantity == 0).SequenceEqual([4, 5]));
            RequireCheck("arithmetic in predicate", QueryIds(records, r => r.Price * 2 > 50).SequenceEqual([3, 5]));
            RequireCheck("inequality", QueryIds(records, r => r.Category != "hardware").SequenceEqual([3, 4, 5]));
            RequireCheck("nullable HasValue", QueryIds(records, r => r.Rank.HasValue).SequenceEqual([1, 3, 5]));
            RequireCheck("nullable Value comparison", QueryIds(records, r => r.Rank.Value >= 3).SequenceEqual([3, 5]));
            Console.WriteLine("        Passed: relational, logical, arithmetic, and nullable operator translation.");
        }

        private static void RunStringTranslation(ILiteCollection<AotLinqRecord> records)
        {
            Console.WriteLine("  [4.2] Translate string instance and static methods.");
            RequireCheck("StartsWith", QueryIds(records, r => r.Name.StartsWith("alpha")).SequenceEqual([1]));
            RequireCheck("EndsWith", QueryIds(records, r => r.Name.EndsWith("gadget")).SequenceEqual([3, 4]));
            RequireCheck("Contains", QueryIds(records, r => r.Name.Contains("widget")).SequenceEqual([1, 2]));
            RequireCheck("ToUpper", QueryIds(records, r => r.Name.ToUpper() == "ALPHA WIDGET").SequenceEqual([1]));
            RequireCheck("ToLower", QueryIds(records, r => r.Category.ToLower() == "software").SequenceEqual([3, 4]));
            RequireCheck("Length member", QueryIds(records, r => r.Name.Length == 11).SequenceEqual([2]));
            RequireCheck("Substring", QueryIds(records, r => r.Name.Substring(0, 5) == "gamma").SequenceEqual([3]));
            RequireCheck("IndexOf", QueryIds(records, r => r.Name.IndexOf("tool") == 8).SequenceEqual([5]));
            RequireCheck("Trim", QueryIds(records, r => r.Name.Trim() == "beta widget").SequenceEqual([2]));
            RequireCheck("Replace", QueryIds(records, r => r.Category.Replace("hard", "soft") == "software").SequenceEqual([1, 2, 3, 4]));
            RequireCheck("string.IsNullOrEmpty", QueryIds(records, r => string.IsNullOrEmpty(r.Name)).Length == 0);
            Console.WriteLine("        Passed: LIKE, case, length, substring, index, trim, replace, and emptiness translation.");
        }

        private static void RunEnumAndDateTranslation(ILiteCollection<AotLinqRecord> records)
        {
            Console.WriteLine("  [4.3] Translate enum comparisons and DateTime members.");
            RequireCheck("enum equality", QueryIds(records, r => r.State == AotNativeScalarState.Captured).SequenceEqual([1, 3, 5]));
            RequireCheck("enum inequality", QueryIds(records, r => r.State != AotNativeScalarState.Captured).SequenceEqual([2, 4]));
            RequireCheck("DateTime Year", QueryIds(records, r => r.CreatedAt.Year == 2023).SequenceEqual([3]));
            RequireCheck("DateTime Month", QueryIds(records, r => r.CreatedAt.Month == 6).SequenceEqual([5]));
            RequireCheck("DateTime Day", QueryIds(records, r => r.CreatedAt.Day == 20).SequenceEqual([2, 5]));
            var cutoff = new DateTime(2024, 3, 1, 12, 0, 0, DateTimeKind.Utc);
            var addDaysCutoff = new DateTime(2024, 6, 21, 6, 0, 0, DateTimeKind.Utc);
            RequireCheck("captured DateTime comparison", QueryIds(records, r => r.CreatedAt > cutoff).SequenceEqual([4, 5]));
            RequireCheck("DateTime constructor translation",
                QueryIds(records, r => r.CreatedAt < new DateTime(2024, 1, 1)).SequenceEqual([3]));
            RequireCheck("DateTime AddDays",
                QueryIds(records, r => r.CreatedAt.AddDays(1) > addDaysCutoff).SequenceEqual([5]));
            Console.WriteLine("        Passed: enum string comparison and date part, constant, and arithmetic translation.");
        }

        private static void RunCaptureAndMembershipTranslation(ILiteCollection<AotLinqRecord> records)
        {
            Console.WriteLine("  [4.4] Translate captured locals and collection membership.");
            var wantedCategory = "software";
            var minimumQuantity = 7;
            var categories = new[] { "hardware", "services" };
            var quantities = new List<int> { 0, 4 };

            RequireCheck("captured string local", QueryIds(records, r => r.Category == wantedCategory).SequenceEqual([3, 4]));
            RequireCheck("captured numeric local", QueryIds(records, r => r.Quantity >= minimumQuantity).SequenceEqual([1, 3, 5]));
            RequireCheck("captured array Contains", QueryIds(records, r => categories.Contains(r.Category)).SequenceEqual([1, 2, 5]));
            RequireCheck("captured list Contains", QueryIds(records, r => quantities.Contains(r.Quantity)).SequenceEqual([2, 4]));
            RequireCheck("document list Contains", QueryIds(records, r => r.Tags.Contains("red")).SequenceEqual([1, 5]));
            RequireCheck("document list Count", QueryIds(records, r => r.Tags.Count == 3).SequenceEqual([5]));
            RequireCheck("document list Any with predicate", QueryIds(records, r => r.Tags.Any(tag => tag == "green")).SequenceEqual([3, 5]));
            RequireCheck("document list Any", QueryIds(records, r => r.Tags.Any()).SequenceEqual([1, 2, 3, 5]));
            Console.WriteLine("        Passed: closure capture, IN membership, and array ANY/COUNT translation.");
        }

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming",
            "IL2026",
            Justification = "The source generator emits direct access to every member used by these expression trees, so their accessors remain rooted.")]
        private static void RunOrderingPagingAndProjection(ILiteCollection<AotLinqRecord> records)
        {
            Console.WriteLine("  [4.5] Order, page, and project generated query results.");
            var ascendingByPrice = records.Query().OrderBy(r => r.Price).Select(r => r.Id).ToArray();
            var descendingByPrice = records.Query().OrderByDescending(r => r.Price).Select(r => r.Id).ToArray();
            var compositeOrder = records.Query()
                .OrderBy(r => r.Category)
                .ThenByDescending(r => r.Quantity)
                .Select(r => r.Id)
                .ToArray();
            var page = records.Query().OrderBy(r => r.Id).Skip(1).Limit(2).ToArray().Ids();
            var offsetPage = records.Query().OrderBy(r => r.Id).Offset(3).ToArray().Ids();
            var names = records.Query().Where(r => r.Active).OrderBy(r => r.Id).Select(r => r.Name).ToList();
            var doubled = records.Query()
                .Where(r => r.Id == 1)
                .Select(r => new AotLinqRecord { Id = r.Id, Quantity = r.Quantity * 2, Name = r.Name })
                .ToArray();
            var documents = records.Query().Where(r => r.Id == 3).ToDocuments().ToArray();
            var enumerated = records.Query().Where(r => r.Quantity == 0).ToEnumerable().Count();

            RequireCheck("OrderBy ascending", ascendingByPrice.SequenceEqual([4, 2, 1, 3, 5]));
            RequireCheck("OrderByDescending", descendingByPrice.SequenceEqual([5, 3, 1, 2, 4]));
            RequireCheck("OrderBy with ThenByDescending", compositeOrder.SequenceEqual([1, 2, 5, 3, 4]));
            RequireCheck("Skip and Limit", page.SequenceEqual([2, 3]));
            RequireCheck("Offset", offsetPage.SequenceEqual([4, 5]));
            RequireCheck("scalar projection", names.SequenceEqual(["alpha widget", "gamma gadget", "epsilon tool"]));
            RequireCheck("entity projection", doubled.Length == 1 && doubled[0].Quantity == 20 && doubled[0].Name == "alpha widget");
            RequireCheck("raw document projection",
                documents.Length == 1 && documents[0][nameof(AotLinqRecord.Category)].AsString == "software");
            RequireCheck("ToEnumerable", enumerated == 1);
            Console.WriteLine("        Passed: ordering, composite ordering, paging, scalar, entity, and document materialization.");
        }

        private static void RunAggregatesAndSingleResults(ILiteCollection<AotLinqRecord> records)
        {
            Console.WriteLine("  [4.6] Evaluate aggregates and single-result query operators.");
            RequireCheck("Count with predicate", records.Count(r => r.Active) == 3);
            RequireCheck("LongCount", records.LongCount() == 5L);
            RequireCheck("LongCount with predicate", records.LongCount(r => r.Quantity > 5) == 3L);
            RequireCheck("Exists with match", records.Exists(r => r.Price > 90d));
            RequireCheck("Exists without match", records.Exists(r => r.Price > 1000d) == false);
            RequireCheck("Min with key selector", records.Min(r => r.Quantity) == 0);
            RequireCheck("Max with key selector", records.Max(r => r.Price) == 99.99d);
            RequireCheck("Min on identifier", records.Min().AsInt32 == 1);
            RequireCheck("Max on identifier", records.Max().AsInt32 == 5);
            RequireCheck("First", records.Query().OrderBy(r => r.Id).First().Id == 1);
            RequireCheck("FirstOrDefault without match", records.Query().Where(r => r.Quantity > 1000).FirstOrDefault() is null);
            RequireCheck("Single", records.Query().Where(r => r.Category == "services").Single().Id == 5);
            RequireCheck("SingleOrDefault without match", records.Query().Where(r => r.Category == "missing").SingleOrDefault() is null);
            RequireCheck("query Count", records.Query().Where(r => r.Active).Count() == 3);
            RequireCheck("query Exists", records.Query().Where(r => r.Tags.Contains("blue")).Exists());
            Console.WriteLine("        Passed: counting, existence, extremes, and first/single operators.");
        }

        private static void RunFindOverloads(ILiteCollection<AotLinqRecord> records)
        {
            Console.WriteLine("  [4.7] Execute Find overloads and parameterized BSON expressions.");
            RequireCheck("FindAll", records.FindAll().Ids().SequenceEqual([1, 2, 3, 4, 5]));
            RequireCheck("Find with predicate", records.Find(r => r.Active).Ids().SequenceEqual([1, 3, 5]));
            RequireCheck("Find with skip and limit", records.Find(r => r.Active, skip: 1, limit: 1).Count() == 1);
            RequireCheck("FindOne with predicate", records.FindOne(r => r.Category == "services")?.Id == 5);
            RequireCheck("FindOne with BSON expression", records.FindOne(BsonExpression.Create("$.Quantity = 7"))?.Id == 3);
            RequireCheck("positional parameters",
                records.Query().Where("$.Quantity > @0", 5).ToArray().Ids().SequenceEqual([1, 3, 5]));
            RequireCheck("named parameters",
                records.Query()
                    .Where("$.Category = @category", new BsonDocument { ["category"] = "hardware" })
                    .ToArray()
                    .Ids()
                    .SequenceEqual([1, 2]));
            RequireCheck("Count with parameterized expression", records.Count("$.Active = true") == 3);
            Console.WriteLine("        Passed: Find, FindOne, and positional and named BSON expression parameters.");
        }

        private static void RunGroupingTranslation(ILiteCollection<AotLinqRecord> records)
        {
            Console.WriteLine("  [4.8] Group generated records and aggregate each group.");
            var groups = records.Query().GroupBy(r => r.Category).ToArray();
            var keys = records.Query().GroupBy(r => r.Category).Select(group => group.Key).ToArray().OrderBy(key => key).ToArray();
            var counts = records.Query().GroupBy(r => r.Category).Select(group => group.Count()).ToArray().OrderBy(count => count).ToArray();
            var totals = records.Query()
                .GroupBy(r => r.Category)
                .Select(group => group.Sum(r => r.Quantity))
                .ToArray()
                .OrderBy(total => total)
                .ToArray();
            var hardware = groups.Single(group => group.Key == "hardware");

            RequireCheck("group count", groups.Length == 3);
            RequireCheck("group keys", keys.SequenceEqual(["hardware", "services", "software"]));
            RequireCheck("group item materialization", hardware.Select(record => record.Id).OrderBy(id => id).SequenceEqual([1, 2]));
            RequireCheck("grouped Count projection", counts.SequenceEqual([1, 2, 2]));
            RequireCheck("grouped Sum projection", totals.SequenceEqual([7, 14, 25]));
            Console.WriteLine("        Passed: grouping keys, grouped item hydration, and grouped aggregate projections.");
        }

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming",
            "IL2026",
            Justification = "The source generator emits direct access to every member used by these expression trees, so their accessors remain rooted.")]
        private static void RunBulkMutationTranslation(LiteDatabase database, BsonMapper mapper)
        {
            Console.WriteLine("  [4.9] Translate bulk update and delete expressions.");
            var mutations = database.GetGeneratedCollection<AotLinqRecord>("aot_linq_mutations");
            mutations.Insert(CreateSeedRecords());

            var updated = mutations.UpdateMany(
                r => new AotLinqRecord { Quantity = r.Quantity + 100 },
                r => r.Active);
            var promoted = mutations.Query().Where(r => r.Quantity >= 100).ToArray().Ids();
            var deleted = mutations.DeleteMany(r => r.Active == false);
            var remaining = mutations.FindAll().Ids();

            RequireCheck("UpdateMany with predicate", updated == 3);
            RequireCheck("UpdateMany transform applied", promoted.SequenceEqual([1, 3, 5]));
            RequireCheck("DeleteMany with predicate", deleted == 2);
            RequireCheck("DeleteMany left the remaining documents", remaining.SequenceEqual([1, 3, 5]));
            RequireCheck("DeleteAll", mutations.DeleteAll() == 3 && mutations.Count() == 0);

            var expression = mapper.GetExpression<AotLinqRecord, bool>(record => record.Quantity > 5 && record.Name.StartsWith("alpha"));
            var evaluated = expression.Execute(new BsonDocument
            {
                ["_id"] = 1,
                [nameof(AotLinqRecord.Name)] = "alpha widget",
                [nameof(AotLinqRecord.Quantity)] = 10
            }).ToArray();
            RequireCheck("standalone expression evaluation", evaluated.Length == 1 && evaluated[0].AsBoolean);
            Console.WriteLine("        Passed: bulk update, bulk delete, delete-all, and standalone expression evaluation.");
        }
    }

    [BsonSourceGenerated]
    public sealed class AotLinqRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public double Price { get; set; }
        public bool Active { get; set; }
        public AotNativeScalarState State { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? Rank { get; set; }
        public List<string> Tags { get; set; } = [];
    }
}
