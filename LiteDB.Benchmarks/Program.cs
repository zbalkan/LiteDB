using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace LiteDB.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = DefaultConfig.Instance
                //.With(new BenchmarkDotNet.Filters.AnyCategoriesFilter(new[] { Benchmarks.Constants.Categories.GENERAL }))
                //.AddFilter(new BenchmarkDotNet.Filters.AnyCategoriesFilter([Benchmarks.Constants.Categories.GENERAL]))
                .AddJob(Job.Default
                    .WithJit(Jit.RyuJit)
                    .WithGcForce(true))
                /*.With(Job.Default.With(MonoRuntime.Default)
                    .With(Jit.Llvm)
                    .With(new[] {new MonoArgument("--optimize=inline")})
                    .WithGcForce(true))*/
                .AddDiagnoser(MemoryDiagnoser.Default)
                .AddExporter(BenchmarkReportExporter.Default, HtmlExporter.Default, MarkdownExporter.GitHub)
                .KeepBenchmarkFiles();

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        }
    }
}
