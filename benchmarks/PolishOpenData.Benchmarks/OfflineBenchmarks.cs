using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using PolishOpenData.BialaLista.Offline;

namespace PolishOpenData.Benchmarks;

/// <summary>
/// Lookups against an index that is already prepared, through the public API. Set <see cref="DataDirectoryVariable"/> to a
/// data directory with a <c>yyyyMMdd.wlidx</c> file whose date is still inside the allowed window (today-30 to today+1);
/// the live test keeps its directory when <c>POLISHOPENDATA_SMOKE_OFFLINE_DIR</c> is set. Nothing is downloaded: the
/// HTTP handler fails every request.
/// </summary>
[MemoryDiagnoser]
public class OfflineLookupBenchmarks : IDisposable
{
    public const string DataDirectoryVariable = "POLISHOPENDATA_BENCH_DATA";

    private VatWhitelistOffline _client = null!;
    private DateOnly _date;
    private (Nip, Nrb) _one;
    private (Nip, Nrb)[] _many = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var dir = Environment.GetEnvironmentVariable(DataDirectoryVariable) ?? throw new InvalidOperationException(DataDirectoryVariable + " is not set.");
        var name = Directory.EnumerateFiles(dir, "*.wlidx").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal).LastOrDefault()
            ?? throw new InvalidOperationException("No .wlidx file in " + dir);
        _date = DateOnly.ParseExact(name!, "yyyyMMdd", null);
        _client = new VatWhitelistOffline(new HttpClient(new FailingHandler()), new VatWhitelistOfflineOptions { DataDirectory = dir });
        _one = (Nip.Parse("1810004094"), Nrb.Parse("39102055610000370202959088")); // one of MF's published test pairs
        // Deterministic, mostly not-found pairs: the slowest path (the plain hash plus every mask of the bank, all rounds).
        var random = new Random(1);
        _many = Enumerable.Range(0, 1000).Select(_ => (RandomNip(random), RandomNrb(random))).ToArray();
        _ = await _client.PrepareAsync(_date); // opens the index outside the measurement
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public Task<VatWhitelistOfflineCheck> CheckOne() => _client.CheckAsync(_one.Item1, _one.Item2, _date);

    [Benchmark]
    public Task<IReadOnlyList<VatWhitelistOfflineCheck>> CheckMany_1000() => _client.CheckManyAsync(_many, _date);

    private static Nip RandomNip(Random random)
    {
        while (true)
        {
            if (Nip.TryParse(random.NextInt64(1_000_000_000, 9_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture), out var nip))
            {
                return nip;
            }
        }
    }

    private static Nrb RandomNrb(Random random)
    {
        while (true)
        {
            var digits = string.Concat(Enumerable.Range(0, 26).Select(_ => (char)('0' + random.Next(10))));
            if (Nrb.TryParse(digits, out var nrb))
            {
                return nrb;
            }
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The benchmark must not download: prepare the index first.");
    }
}

/// <summary>
/// Parses the extracted flat-file JSON and writes the index (the CPU and disk part of preparing a date, without the
/// download). Set <see cref="JsonVariable"/> to the path of the extracted <c>.json</c>.
/// It excludes the 7z decoding and SHA-512 verification of a real preparation (about 30 s on the live file).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)] // each iteration reads a file of about 515 MB
public class OfflineBuildBenchmarks
{
    public const string JsonVariable = "POLISHOPENDATA_BENCH_JSON";

    private string _json = null!;
    private string _index = null!;

    [GlobalSetup]
    public void Setup()
    {
        _json = Environment.GetEnvironmentVariable(JsonVariable) ?? throw new InvalidOperationException(JsonVariable + " is not set.");
        _index = Path.Combine(Path.GetTempPath(), "polishopendata-bench-" + Guid.NewGuid().ToString("N") + ".wlidx");
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_index);

    [Benchmark]
    public long BuildIndex_from_real_json()
    {
        using (var json = File.OpenRead(_json))
        {
            var parsed = FlatFileParser.Parse(json, new DateOnly(2000, 1, 1), CancellationToken.None);
            WhitelistIndex.Write(_index, new DateOnly(2000, 1, 1), parsed);
        }

        return new FileInfo(_index).Length;
    }
}
