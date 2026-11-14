using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Sejm.Tests;

public class SejmVotingTests
{
    private static SejmClient Client(StubHttpMessageHandler handler) => new(new HttpClient(handler));

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Route(HttpRequestMessage r)
    {
        var accepts = string.Join(",", r.Headers.Accept.Select(a => a.MediaType));
        if (!accepts.Contains("application/json", StringComparison.Ordinal))
        {
            return StubHttpMessageHandler.Empty(HttpStatusCode.NotAcceptable);
        }

        var path = r.RequestUri!.AbsolutePath;
        var query = r.RequestUri.Query;
        return path switch
        {
            "/sejm/term10/votings" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/votings-index.json"),
            "/sejm/term10/votings/search" => query.Contains("proceeding=67", StringComparison.Ordinal)
                ? StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/votings-search-67.json")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"),
            "/sejm/term10/votings/67/1" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/voting-67-1.json"),
            "/sejm/term10/votings/67/2" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/voting-67-2-quorum.json"),
            "/sejm/term10/votings/1/13" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/synthetic-voting-on-list.json"),
            _ => StubHttpMessageHandler.Empty(HttpStatusCode.NotFound),
        };
    }

    [Fact]
    public async Task VotingDays_Map()
    {
        var days = await Client(new StubHttpMessageHandler(Route)).GetVotingDaysAsync(10, Ct);

        Assert.Equal(6, days.Count);
    }

    [Fact]
    public async Task Search_SendsProceedingAndLimitPlusOne_AndHasMoreIsExact()
    {
        var handler = new StubHttpMessageHandler(Route);

        var page = await Client(handler).SearchVotingsAsync(new SejmVotingSearch { Term = 10, Sitting = 67, Limit = 2 }, Ct);

        Assert.Equal("?proceeding=67&limit=3&offset=0", handler.RequestUris.Single().Query);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.HasMore);
        Assert.Null(page.Total);
    }

    [Fact]
    public async Task Search_ExactlyLimitItems_HasNoMore()
    {
        var handler = new StubHttpMessageHandler(Route);

        var page = await Client(handler).SearchVotingsAsync(new SejmVotingSearch { Term = 10, Sitting = 67, Limit = 3 }, Ct);

        Assert.Equal(3, page.Items.Count);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Search_SendsTitleAndDates()
    {
        var handler = new StubHttpMessageHandler(Route);

        _ = await Client(handler).SearchVotingsAsync(new SejmVotingSearch
        {
            Term = 10,
            Title = "druk 1344",
            DateFrom = new DateOnly(2026, 10, 1),
            DateTo = new DateOnly(2026, 10, 31),
        }, Ct);

        var url = handler.RequestUris.Single().AbsoluteUri;
        Assert.Contains("title=druk%201344", url, StringComparison.Ordinal);
        Assert.Contains("dateFrom=2026-10-01", url, StringComparison.Ordinal);
        Assert.Contains("dateTo=2026-10-31", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_NoCriteria_Throws_WithoutARequest()
    {
        var handler = new StubHttpMessageHandler(Route);

        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).SearchVotingsAsync(new SejmVotingSearch { Term = 10 }, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).SearchVotingsAsync(new SejmVotingSearch { Term = 10, Title = "  " }, Ct));

        Assert.Empty(handler.RequestUris);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(501, 0)]
    [InlineData(20, -1)]
    public async Task Search_OutOfRange_Throws(int limit, int offset)
    {
        var handler = new StubHttpMessageHandler(Route);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Client(handler).SearchVotingsAsync(new SejmVotingSearch { Term = 10, Sitting = 67, Limit = limit, Offset = offset }, Ct));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task Search_UnknownSitting_IsEmpty()
    {
        var page = await Client(new StubHttpMessageHandler(Route)).SearchVotingsAsync(new SejmVotingSearch { Term = 10, Sitting = 999 }, Ct);

        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task GetVoting_404_IsNull()
    {
        Assert.Null(await Client(new StubHttpMessageHandler(Route)).GetVotingAsync(10, 67, 9999, cancellationToken: Ct));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(67, 0)]
    public async Task GetVoting_BadNumbers_Throw_WithoutARequest(int sitting, int number)
    {
        var handler = new StubHttpMessageHandler(Route);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Client(handler).GetVotingAsync(10, sitting, number, cancellationToken: Ct));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GetVoting_Default_MpVotesNull_ClubsFilled()
    {
        var details = await Client(new StubHttpMessageHandler(Route)).GetVotingAsync(10, 67, 1, cancellationToken: Ct);

        Assert.NotNull(details);
        Assert.Null(details.MpVotes);
        Assert.True(details.SumsMatchTotals);
        Assert.Equal(12, details.Clubs.Count);
        Assert.Equal("KO", details.Clubs[0].Club);
        Assert.Equal(389, details.Voting.Yes);
        Assert.DoesNotContain("KOWALSKI", System.Text.Json.JsonSerializer.Serialize(details), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVoting_IncludeMpVotes_Has460()
    {
        var details = await Client(new StubHttpMessageHandler(Route)).GetVotingAsync(10, 67, 1, includeMpVotes: true, cancellationToken: Ct);

        Assert.NotNull(details?.MpVotes);
        Assert.Equal(460, details.MpVotes.Count);
    }

    [Fact]
    public async Task GetVoting_OnList_ChecksTotalVoted()
    {
        var details = await Client(new StubHttpMessageHandler(Route)).GetVotingAsync(10, 1, 13, cancellationToken: Ct);

        Assert.NotNull(details);
        Assert.True(details.SumsMatchTotals);
        Assert.Equal(8, details.Voting.Options.Count);
    }
}
