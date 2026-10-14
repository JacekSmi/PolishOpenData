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
/// download from plikplaski.mf.gov.pl (no quota) and one Biała Lista search request for two NIPs (2 of the 100 daily
/// searches), see <see cref="Live"/>. The preparation is cut off after 15 minutes and then
/// skips the test (a slow MF server is not a regression); the test as a whole times out after 20. Set <see cref="KeepDirectoryVariable"/> to keep the prepared index (for the benchmarks)
/// instead of deleting it.
/// </summary>
public sealed class BialaListaOfflineLiveTests
{
    /// <summary>When set, the data directory to use and keep (created if missing); otherwise a temporary one that is deleted at the end.</summary>
    public const string KeepDirectoryVariable = "POLISHOPENDATA_SMOKE_OFFLINE_DIR";

    // The xunit timeout covers the whole test; the download gets a shorter budget of its own so that a slow MF server
    // ends as a skip (not a regression) while a finished download that gives wrong answers still fails.
    private const int OfflineTimeout = 20 * 60 * 1000;
    private static readonly TimeSpan DownloadBudget = TimeSpan.FromMinutes(15);

    // Two large VAT payers with many accounts in several banks. Their accounts come from the REST API on the file's date,
    // so nothing is hand-recorded. MF's published test pairs (dane_testowe_2026_09_30.txt) are fictional: the REST API
    // answers "not assigned" for all of them (live run of 2026-10-13), so they cannot show that the hashing is right.
    private static readonly Nip[] Taxpayers = [Nip.Parse(Live.OrlenNip), Nip.Parse("5260251049")];

    [Fact(Timeout = OfflineTimeout)]
    public async Task BialaListaOffline_prepares_todays_file_and_finds_the_accounts_the_rest_api_lists()
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
            var progress = new LastProgress();
            VatWhitelistFileInfo info;
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                budget.CancelAfter(DownloadBudget);
                try
                {
                    info = await offline.PrepareAsync(progress: progress, cancellationToken: budget.Token);
                }
                catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    Assert.Skip($"MF flat-file download too slow ({progress.Received / 1_000_000:N0} MB of {(progress.Total is { } total ? (total / 1_000_000).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "?")} MB in {DownloadBudget.TotalMinutes:N0} min); not a regression.");
                    return;
                }
                catch (VatWhitelistDataUnavailableException ex) when (ex.Reason == VatWhitelistDataUnavailableReason.NotPublishedYet)
                {
                    Assert.Skip("MF has not published today's flat file yet; not a regression: " + ex.Message);
                    return;
                }
            }

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
            _ = await offline.CheckAsync(Taxpayers[0], Nrb.Parse("70102035709724546729803218"), info.FileDate, ct);
            Assert.Equal(downloads, requests.Count);

            // The oracle is the REST API of the same date: every account it lists for an active taxpayer is in the file.
            using var restHttp = Live.CreateHttpClient();
            var rest = new BialaListaClient(restHttp);
            IReadOnlyList<VatBatchEntry> entries;
            try
            {
                entries = (await rest.FindByNipsAsync(Taxpayers, info.FileDate, ct)).Value;
            }
            catch (QuotaExceededException ex)
            {
                Live.SkipOnQuota(ex);
                return;
            }

            var pairs = entries.SelectMany(e => e.Subjects).Where(s => s.VatStatus == VatStatus.Active)
                .SelectMany(s => s.AccountNumbers.Select(a => (Nip: s.Nip!.Value, Account: a))).ToArray();
            Assert.True(pairs.Length >= Taxpayers.Length, "The REST API listed " + pairs.Length + " accounts of active taxpayers.");
            var answers = await offline.CheckManyAsync(pairs, info.FileDate, ct);
            var missing = pairs.Zip(answers, (p, a) => (p, a)).Where(x => x.a.Status != VatWhitelistOfflineStatus.Active).Select(x => $"{x.p.Nip} {x.p.Account}: {x.a.Status}").ToList();
            output?.WriteLine($"{pairs.Length} accounts from the REST API, {answers.Count(a => a.MatchedVirtualAccountMask)} through a mask, {missing.Count} not active offline.");
            Assert.True(missing.Count == 0, "The REST API lists these accounts, the offline index does not: " + string.Join(" | ", missing));

            // The same account under another taxpayer's NIP is not in the file.
            var other = Taxpayers.First(n => n != pairs[0].Nip);
            Assert.Equal(VatWhitelistOfflineStatus.NotFound, (await offline.CheckAsync(other, pairs[0].Account, info.FileDate, ct)).Status);
        }
        finally
        {
            if (string.IsNullOrEmpty(keep) && Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    // Synchronous, unlike Progress<T>: the last report is already there when the preparation is cancelled.
    private sealed class LastProgress : IProgress<VatWhitelistPreparationProgress>
    {
        private long _received;
        private long _total = -1;

        public long Received => Interlocked.Read(ref _received);

        public long? Total => Interlocked.Read(ref _total) is var t && t >= 0 ? t : null;

        public void Report(VatWhitelistPreparationProgress value)
        {
            Interlocked.Exchange(ref _received, value.BytesReceived);
            Interlocked.Exchange(ref _total, value.TotalBytes ?? -1);
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
