using System;
using System.Net.Http;
using System.Threading.Tasks;
using PolishOpenData.BialaLista;
using PolishOpenData.Krs;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// Calls the real registries. Runs only when POLISHOPENDATA_SMOKE=1 (the nightly workflow). Never enable it locally:
/// Biała Lista blocks the caller's IP until midnight after 100 searches a day.
/// </summary>
public sealed class LiveRegistryTests
{
    private static void SkipUnlessEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("POLISHOPENDATA_SMOKE"), "1", StringComparison.Ordinal))
        {
            Assert.Skip("Live smoke tests run only with POLISHOPENDATA_SMOKE=1 (nightly CI); they spend Biała Lista quota.");
        }
    }

    [Fact]
    public async Task Krs_current_extract_of_orlen_matches_the_model()
    {
        SkipUnlessEnabled();
        using var http = new HttpClient();
        var client = new KrsClient(http);

        KrsResult<KrsCurrentExtract> result;
        try
        {
            result = await client.GetCurrentExtractAsync(KrsNumber.Parse("28860"), KrsRegister.Entrepreneurs, TestContext.Current.CancellationToken);
        }
        catch (QuotaExceededException ex)
        {
            Assert.Skip("KRS rate limit reached for the runner's shared IP: " + ex.Message);
            return;
        }

        Assert.Equal(KrsLookupStatus.Found, result.Status);
        Assert.Empty(UnknownFields.Find(result.Extract));   // a new upstream field fails here and opens an issue
        var summary = result.Extract!.ToSummary();
        Assert.Contains("ORLEN", summary.Name, StringComparison.Ordinal);
        Assert.Equal(Nip.Parse("7740001454"), summary.Nip);
        Assert.NotNull(summary.Representation);
    }

    [Fact]
    public async Task BialaLista_search_and_check_of_orlen_match_the_model()
    {
        SkipUnlessEnabled();
        using var http = new HttpClient();
        var client = new BialaListaClient(http);

        try
        {
            var search = await client.FindByNipAsync(Nip.Parse("7740001454"), cancellationToken: TestContext.Current.CancellationToken);
            var subject = search.Value;
            Assert.NotNull(subject);
            Assert.Equal(VatStatus.Active, subject!.VatStatus);
            Assert.Empty(subject.UnknownFields);
            Assert.NotEmpty(subject.AccountNumbers);
            Assert.False(string.IsNullOrEmpty(search.RequestId));
            Assert.Empty(search.UnknownFields);

            var check = await client.CheckBankAccountAsync(subject.Nip!.Value, subject.AccountNumbers[0], cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(check.Value);
            Assert.False(string.IsNullOrEmpty(check.RequestId));
            Assert.Empty(check.UnknownFields);
        }
        catch (QuotaExceededException ex)
        {
            Assert.Skip("Biała Lista limit reached for the runner's shared IP: " + ex.Message);
        }
    }
}
