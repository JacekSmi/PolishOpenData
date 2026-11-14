using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PolishOpenData.Sejm;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// The Sejm client against the live API (gated and budgeted by <see cref="Live"/>; no key, no published limit). 8 requests in two tests:
/// processes (terms, search, process 318, print 401, a raw sort check) and votings (the index, a search, voting 67/1).
/// </summary>
public sealed class SejmLiveTests
{
    [Fact(Timeout = Live.TestTimeout)]
    public async Task Reads_a_process_with_its_act_and_follows_a_report_print()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new SejmClient(http);
        var ct = TestContext.Current.CancellationToken;

        try
        {
            var terms = await client.GetTermsAsync(ct);
            Assert.Single(terms, t => t.Current);

            var found = await client.SearchProcessesAsync(new SejmProcessSearch { Term = 10, PassedOnly = true, Limit = 3 }, ct);
            Assert.NotEmpty(found.Items);
            Assert.True(found.Total > 3);

            var process = await client.GetProcessAsync(10, "318", ct);
            Assert.NotNull(process);
            Assert.Equal("DU/2024/878", process.Summary.Eli);

            var report = await client.GetPrintAsync(10, "401", ct);
            Assert.Equal("318", report?.ProcessNumber);

            // drift pin: the API ignores sort options today; if it starts honouring them, newestFirst in the MCP tool can drop its count call
            using var raw = await http.GetAsync("https://api.sejm.gov.pl/sejm/term10/processes?sort_by=-number&limit=2", ct);
            using var json = JsonDocument.Parse(await raw.Content.ReadAsStringAsync(ct));
            Assert.Equal("1", json.RootElement.EnumerateArray().First().GetProperty("number").GetString());
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
        }
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Reads_a_voting_as_club_tallies_that_match_the_official_totals()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new SejmClient(http);
        var ct = TestContext.Current.CancellationToken;

        try
        {
            var days = await client.GetVotingDaysAsync(10, ct);
            Assert.NotEmpty(days);

            var page = await client.SearchVotingsAsync(new SejmVotingSearch { Term = 10, Sitting = 67, Limit = 2 }, ct);
            Assert.Equal(2, page.Items.Count);
            Assert.True(page.HasMore);

            var details = await client.GetVotingAsync(10, 67, 1, cancellationToken: ct);
            Assert.NotNull(details);
            Assert.True(details.SumsMatchTotals);
            Assert.Null(details.MpVotes);
            Assert.True(details.Clubs.Count >= 5);
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
        }
    }
}
