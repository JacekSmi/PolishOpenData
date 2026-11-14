using System.Linq;
using System.Threading.Tasks;
using PolishOpenData.Eli;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// The ELI client against the live Sejm API (gated and budgeted by <see cref="Live"/>; no key). About 8 requests: one search, one act,
/// the current-text check (the act again plus up to three consolidated texts) and one article (structure and text).
/// </summary>
public sealed class EliLiveTests
{
    private static readonly EliActId KodeksCywilny = EliActId.Parse("DU/1964/93");

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Finds_the_civil_code_and_reads_article_415_with_its_provenance()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new EliClient(http);
        var ct = TestContext.Current.CancellationToken;

        try
        {
            var found = await client.SearchActsAsync(new EliSearch { Title = "kodeks cywilny", Type = "Ustawa", Year = 1964 }, ct);
            Assert.Contains(found.Items, a => a.Id == KodeksCywilny);

            var act = await client.GetActAsync(KodeksCywilny, ct);
            Assert.NotNull(act);
            Assert.NotEmpty(act.ConsolidatedTexts);

            var current = await client.GetCurrentTextAsync(KodeksCywilny, ct);
            Assert.NotNull(current.NewestConsolidated);
            if (current.NewestWithHtml is null)
            {
                Assert.Skip("The API serves no HTML for any of the five newest consolidated texts of the civil code any more.");
            }

            var article = await client.GetArticleAsync(current.NewestWithHtml.Id, "415", ct);
            Assert.NotNull(article);
            Assert.Contains("szkod", article.Text, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('<', article.Text);
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
        }
    }
}
