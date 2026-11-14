using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Sejm.Tests;

public class SejmBodiesTests
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

        return r.RequestUri!.AbsolutePath switch
        {
            "/sejm/term10/clubs" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/clubs.json"),
            "/sejm/term10/clubs/Centrum" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/club-centrum.json"),
            "/sejm/term10/committees" => StubHttpMessageHandler.Json(HttpStatusCode.OK, "[" + Fixture.Read("sejm/committee-asw.json") + "]"),
            "/sejm/term10/committees/ASW" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/committee-asw.json"),
            "/sejm/term10/committees/XX" => StubHttpMessageHandler.Empty(HttpStatusCode.NoContent),   // like the live API: 204 for an unknown committee
            _ => StubHttpMessageHandler.Empty(HttpStatusCode.NotFound),                                // a dissolved club is 404
        };
    }

    [Fact]
    public async Task Clubs_DefaultHasNoMembers_HasCount()
    {
        var clubs = await Client(new StubHttpMessageHandler(Route)).GetClubsAsync(10, cancellationToken: Ct);

        Assert.Equal(12, clubs.Count);
        Assert.All(clubs, c => Assert.Empty(c.Members));
        Assert.All(clubs, c => Assert.True(c.MembersCount > 0));
    }

    [Fact]
    public async Task Clubs_IncludeMembers()
    {
        var clubs = await Client(new StubHttpMessageHandler(Route)).GetClubsAsync(10, includeMembers: true, cancellationToken: Ct);

        Assert.All(clubs, c => Assert.Equal(3, c.Members.Count));
    }

    [Fact]
    public async Task Club_ById_DefaultNoMembers_ThenWithMembers()
    {
        var client = Client(new StubHttpMessageHandler(Route));

        var plain = await client.GetClubAsync(10, "Centrum", cancellationToken: Ct);
        var full = await client.GetClubAsync(10, "Centrum", includeMembers: true, cancellationToken: Ct);

        Assert.NotNull(plain);
        Assert.Empty(plain.Members);
        Assert.Equal("Centrum", plain.Id);
        Assert.Equal(3, full!.Members.Count);
    }

    [Fact]
    public async Task Club_Dissolved404_IsNull()
    {
        Assert.Null(await Client(new StubHttpMessageHandler(Route)).GetClubAsync(10, "Polska2050-TD", cancellationToken: Ct));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("")]
    public async Task Club_BadId_Throws_WithoutARequest(string id)
    {
        var handler = new StubHttpMessageHandler(Route);

        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).GetClubAsync(10, id, cancellationToken: Ct));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task Committees_ListWithoutMembers()
    {
        var committees = await Client(new StubHttpMessageHandler(Route)).GetCommitteesAsync(10, cancellationToken: Ct);

        var asw = Assert.Single(committees);
        Assert.Equal("ASW", asw.Code);
        Assert.Empty(asw.Members);
    }

    [Fact]
    public async Task Committee_CompositionSummedFromMembers_EvenWithoutThem()
    {
        var committee = await Client(new StubHttpMessageHandler(Route)).GetCommitteeAsync(10, "ASW", cancellationToken: Ct);

        Assert.NotNull(committee);
        Assert.Empty(committee.Members);
        Assert.Equal(40, committee.Composition.Sum(c => c.Seats));
        Assert.Equal(committee.Composition.OrderByDescending(c => c.Seats).First().Club, committee.Composition[0].Club);
    }

    [Fact]
    public async Task Committee_IncludeMembers()
    {
        var committee = await Client(new StubHttpMessageHandler(Route)).GetCommitteeAsync(10, "ASW", includeMembers: true, cancellationToken: Ct);

        Assert.Equal(40, committee!.Members.Count);
    }

    [Fact]
    public async Task Committee_204_IsNull()
    {
        Assert.Null(await Client(new StubHttpMessageHandler(Route)).GetCommitteeAsync(10, "XX", cancellationToken: Ct));
    }

    [Fact]
    public async Task UnreadableCommittee_DoesNotLeakMemberNamesInTheSnippet()
    {
        var bad = Fixture.Read("sejm/committee-asw.json").Replace("\"appointmentDate\": \"2023-11-21\"", "\"appointmentDate\": \"21.11.2023\"");
        Assert.Contains("Kowalski", bad, StringComparison.Ordinal);
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, bad));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).GetCommitteeAsync(10, "ASW", cancellationToken: Ct));

        Assert.DoesNotContain("Kowalski", ex.ResponseSnippet ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Kowalski", ex.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("asw")]
    [InlineData("A/B")]
    public async Task Committee_BadCode_Throws_WithoutARequest(string code)
    {
        var handler = new StubHttpMessageHandler(Route);

        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).GetCommitteeAsync(10, code, cancellationToken: Ct));

        Assert.Empty(handler.RequestUris);
    }
}
