using System;
using System.Threading.Tasks;
using PolishOpenData.Ceidg;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// The CEIDG client against the production API (gated and budgeted by <see cref="Live"/>). Owner-run: it needs your own token in
/// <see cref="Live.CeidgTokenVariable"/> and is skipped without it; CI never sets it (the token is personal and must not go into
/// GitHub secrets). Two requests; the second runs only when <see cref="Live.CeidgNipVariable"/> holds a NIP that is in CEIDG
/// (use your own: the repository holds no real person's NIP).
/// </summary>
public sealed class CeidgLiveTests
{
    // The Ministry of Finance is not a sole trader, so CEIDG has no entry: proves the token works and an empty answer maps to an empty list.
    private const string NotASoleTraderNip = "5260250995";

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Production_accepts_the_token_and_maps_an_entry()
    {
        Live.SkipUnlessEnabled();
        var token = Environment.GetEnvironmentVariable(Live.CeidgTokenVariable);
        if (string.IsNullOrWhiteSpace(token))
        {
            Assert.Skip("The production CEIDG test runs only when " + Live.CeidgTokenVariable + " holds your own CEIDG token; CI never sets it.");
        }

        using var http = Live.CreateHttpClient();
        var client = new CeidgClient(http, new CeidgClientOptions { Token = token });
        var ct = TestContext.Current.CancellationToken;

        try
        {
            Assert.Empty(await client.FindByNipAsync(Nip.Parse(NotASoleTraderNip), ct));

            var ownNip = Environment.GetEnvironmentVariable(Live.CeidgNipVariable);
            if (!string.IsNullOrWhiteSpace(ownNip))
            {
                var found = await client.FindByNipAsync(Nip.Parse(ownNip), ct);
                Assert.NotEmpty(found);
                var entry = found[0];
                Assert.NotEqual(Guid.Empty, entry.Id);
                Assert.NotEqual(CeidgStatus.Unknown, entry.Status);
                Assert.NotNull(entry.StartDate);
                Assert.Equal(Nip.Parse(ownNip), entry.Nip);
            }
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
        }
    }
}
