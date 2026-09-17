using System;
using System.IO;
using System.Linq;
using LiteDB.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDB.AotTests
{
    [TestClass]
    public sealed class IncrementalGeneratorTests
    {
        [TestMethod]
        public void BsonSourceGenerator_ModelAnalysis_UsesStructuralEquality()
        {
            const string baselineSource = """
                using LiteDB;

                namespace IncrementalConsumer;

                [BsonSourceGenerated]
                public sealed class IncrementalRecord
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = string.Empty;
                }
                """;

            const string triviaChangedSource = """
                using LiteDB;

                namespace IncrementalConsumer;

                [BsonSourceGenerated]
                public sealed class IncrementalRecord
                {
                    // This edit does not change the generated mapping.
                    public int Id { get; set; }
                    public string Name { get; set; } = string.Empty;
                }
                """;

            const string modelChangedSource = """
                using LiteDB;

                namespace IncrementalConsumer;

                [BsonSourceGenerated]
                public sealed class IncrementalRecord
                {
                    public int Id { get; set; }
                    public string DisplayName { get; set; } = string.Empty;
                }
                """;

            var baselineTree = CSharpSyntaxTree.ParseText(baselineSource, path: "IncrementalConsumer.cs");
            var baselineCompilation = CreateCompilation(baselineTree);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators: new[] { new BsonSourceGenerator().AsSourceGenerator() },
                driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

            driver = driver.RunGenerators(baselineCompilation);

            var triviaTree = CSharpSyntaxTree.ParseText(triviaChangedSource, path: "IncrementalConsumer.cs");
            var triviaCompilation = baselineCompilation.ReplaceSyntaxTree(baselineTree, triviaTree);
            driver = driver.RunGenerators(triviaCompilation);

            var triviaReasons = GetModelAnalysisReasons(driver);
            Assert.AreEqual(1, triviaReasons.Length);
            Assert.IsTrue(
                triviaReasons[0] is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected unchanged model analysis after a trivia-only edit, but got {triviaReasons[0]}.");

            var modelChangedTree = CSharpSyntaxTree.ParseText(modelChangedSource, path: "IncrementalConsumer.cs");
            var modelChangedCompilation = triviaCompilation.ReplaceSyntaxTree(triviaTree, modelChangedTree);
            driver = driver.RunGenerators(modelChangedCompilation);

            var changedReasons = GetModelAnalysisReasons(driver);
            Assert.AreEqual(1, changedReasons.Length);
            Assert.AreEqual(IncrementalStepRunReason.Modified, changedReasons[0]);
        }

        private static IncrementalStepRunReason[] GetModelAnalysisReasons(GeneratorDriver driver)
        {
            var result = driver.GetRunResult().Results.Single();
            Assert.IsTrue(result.TrackedSteps.TryGetValue("ModelAnalysis", out var steps));
            return steps
                .SelectMany(step => step.Outputs)
                .Select(output => output.Reason)
                .ToArray();
        }

        private static CSharpCompilation CreateCompilation(SyntaxTree sourceTree)
        {
            return CSharpCompilation.Create(
                assemblyName: "IncrementalConsumer",
                syntaxTrees: new[] { sourceTree },
                references: GetMetadataReferences(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static MetadataReference[] GetMetadataReferences()
        {
            var trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
            var runtimeReferences = trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var liteDbReference = MetadataReference.CreateFromFile(typeof(BsonSourceGeneratedAttribute).Assembly.Location);

            return runtimeReferences.Append(liteDbReference).ToArray();
        }
    }
}
