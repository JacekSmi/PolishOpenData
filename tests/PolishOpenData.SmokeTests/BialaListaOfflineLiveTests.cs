using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.BialaLista;
using PolishOpenData.BialaLista.Offline;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// The offline whitelist against the real flat file (about 250 MB; one download per run). Cost per run: one flat-file
/// download from plikplaski.mf.gov.pl (no quota) and one Biała Lista check request per MF test pair (<see cref="Pairs"/>
/// has 14, of the 5,000 daily checks), see <see cref="Live"/>. The whole preparation can take many minutes when MF is
/// slow, hence the long timeout. Set <see cref="KeepDirectoryVariable"/> to keep the prepared index (for the benchmarks)
/// instead of deleting it.
/// </summary>
public sealed class BialaListaOfflineLiveTests
{
    /// <summary>When set, the data directory to use and keep (created if missing); otherwise a temporary one that is deleted at the end.</summary>
    public const string KeepDirectoryVariable = "POLISHOPENDATA_SMOKE_OFFLINE_DIR";

    private const int OfflineTimeout = 20 * 60 * 1000;

    // MF's published test pairs, copied on 2026-10-13 from https://www.gov.pl/attachment/e429f4a2-98b2-4634-a2b4-7f9c1ec3e757
    // (dane_testowe_2026_09_30.txt, 17 rows). Only the rows that pass Nip.TryParse and Nrb.TryParse are kept: the three
    // NRBs of 1810004094 ending ...7534289857, ...9973554 and ...7887664831 fail the NRB check digits. Their status is not
    // hand-recorded here: the test asks the REST API about each pair on the same date and compares.
    private static readonly (string Nip, string Nrb)[] Pairs =
    [
        ("9810000054", "70102035709724546729803218"),
        ("1120000322", "83249000054243980827278527"),
        ("1120000428", "89926400093578089389446762"),
        ("1810004094", "39102055610000370202959088"),
        ("1120000411", "33144010558221640315760086"),
        ("1810004094", "38102055610000320209756141"),
        ("1120000351", "98124062349989896801925550"),
        ("1810004094", "23102055610000340209684145"),
        ("1130013160", "57102010550000910200159426"),
        ("1130013160", "53102011560000740200072686"),
        ("1130013160", "31102011560000730200166736"),
        ("5838154651", "63102011560000710200483933"),
        ("5838154651", "24102055610000360209622046"),
        ("9889982762", "63102011560000710200483933"),
    ];

    [Fact(Timeout = OfflineTimeout)]
    public async Task BialaListaOffline_prepares_todays_file_and_agrees_with_the_rest_api_on_mfs_test_pairs()
    {
        Live.SkipUnlessEnabled();
        var ct = TestContext.Current.CancellationToken;
        var output = TestContext.Current.TestOutputHelper;
        var keep = Environment.GetEnvironmentVariable(KeepDirectoryVariable);
        var dataDirectory = string.IsNullOrEmpty(keep) ? Path.Combine(Path.GetTempPath(), "polishopendata-offline-smoke-" + Guid.NewGuid().ToString("N")) : keep;
        var requests = new CountingHandler();
        using var http = new HttpClient(requests) { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            using var offline = new VatWhitelistOffline(http, new VatWhitelistOfflineOptions { DataDirectory = dataDirectory });

            var clock = Stopwatch.StartNew();
            var info = await offline.PrepareAsync(cancellationToken: ct);
            clock.Stop();
            var downloads = requests.Count;
            var indexBytes = Directory.EnumerateFiles(dataDirectory, "*.wlidx").Sum(f => new FileInfo(f).Length);
            output?.WriteLine($"Prepared {info.FileDate:yyyy-MM-dd} in {clock.Elapsed}: {downloads} request(s), index {indexBytes:N0} bytes, " +
                $"generated {info.GeneratedOn:yyyy-MM-dd}, {info.Transformations} transformations, active {info.ActiveCount:N0}, exempt {info.ExemptCount:N0}, masks {info.MaskCount:N0}.");

            Assert.True(downloads > 0);
            Assert.True(info.Transformations > 0);
            // MF's specification says the header date is the file's date. Do not weaken this: report a difference.
            Assert.Equal(info.FileDate, info.GeneratedOn);
            // 1,000,000 is the order of magnitude of active taxpayers' accounts on the whitelist; lower it only to what a live run shows.
            Assert.True(info.ActiveCount > 1_000_000, "ActiveCount " + info.ActiveCount);
            Assert.True(info.ExemptCount > 0, "ExemptCount " + info.ExemptCount);
            Assert.True(info.MaskCount > 0, "MaskCount " + info.MaskCount);

            // A second call for the same date is served from the prepared index: no request.
            Assert.Equal(info.ActiveCount, (await offline.PrepareAsync(info.FileDate, cancellationToken: ct)).ActiveCount);
            _ = await offline.CheckAsync(Nip.Parse(Pairs[0].Nip), Nrb.Parse(Pairs[0].Nrb), info.FileDate, ct);
            Assert.Equal(downloads, requests.Count);

            // The oracle is the REST API of the same date: it answers "is this account assigned to this NIP", which is
            // "found in the file" (Active or Exempt) on the offline side.
            using var restHttp = Live.CreateHttpClient();
            var rest = new BialaListaClient(restHttp);
            var pairs = Pairs.Select(p => (Nip: Nip.Parse(p.Nip), Account: Nrb.Parse(p.Nrb))).ToArray();
            var answers = await offline.CheckManyAsync(pairs, info.FileDate, ct);
            var mismatches = new List<string>();
            try
            {
                for (var i = 0; i < pairs.Length; i++)
                {
                    var apiAssigned = (await rest.CheckBankAccountAsync(pairs[i].Nip, pairs[i].Account, info.FileDate, ct)).Value;
                    var line = $"{pairs[i].Nip} {pairs[i].Account}: offline {answers[i].Status} (mask {answers[i].MatchedVirtualAccountMask}), REST assigned {apiAssigned}";
                    output?.WriteLine(line);
                    if ((answers[i].Status != VatWhitelistOfflineStatus.NotFound) != apiAssigned)
                    {
                        mismatches.Add(line);
                    }
                }
            }
            catch (QuotaExceededException ex)
            {
                Live.SkipOnQuota(ex);
            }

            output?.WriteLine("Split: " + string.Join(", ", answers.GroupBy(a => a.Status).Select(g => $"{g.Key} {g.Count()}")));
            Assert.True(mismatches.Count == 0, "The offline answer differs from the REST API for: " + string.Join(" | ", mismatches));
        }
        finally
        {
            if (string.IsNullOrEmpty(keep) && Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    private sealed class CountingHandler : DelegatingHandler
    {
        private int _count;

        public CountingHandler() : base(new HttpClientHandler())
        {
        }

        public int Count => Volatile.Read(ref _count);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _count);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
