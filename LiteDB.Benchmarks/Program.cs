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
                .AddJob(Job.Default.WithRuntime(CoreRuntime.Core10_0)
                    .WithJit(Jit.RyuJit)
                    .WithGcForce(true))
                .AddDiagnoser(MemoryDiagnoser.Default)
                .AddExporter(BenchmarkReportExporter.Default, HtmlExporter.Default, MarkdownExporter.GitHub)
                .KeepBenchmarkFiles();

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        }
    }
}
