using System;
using BenchmarkDotNet.Running;

namespace PolishOpenData.Benchmarks;

internal static class Program
{
    // Each class needs a real flat file (about 250 MB) and is skipped when its variable is unset, so `dotnet run` never fails or downloads anything.
    private static void Main()
    {
        var ran = false;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OfflineLookupBenchmarks.DataDirectoryVariable)))
        {
            _ = BenchmarkRunner.Run<OfflineLookupBenchmarks>();
            ran = true;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OfflineBuildBenchmarks.JsonVariable)))
        {
            _ = BenchmarkRunner.Run<OfflineBuildBenchmarks>();
            ran = true;
        }

        if (!ran)
        {
            Console.WriteLine($"Nothing to run: set {OfflineLookupBenchmarks.DataDirectoryVariable} (a data directory holding a prepared index) and/or {OfflineBuildBenchmarks.JsonVariable} (the extracted flat-file JSON), then run with -c Release.");
        }
    }
}
