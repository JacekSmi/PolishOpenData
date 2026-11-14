using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Eli.Tests;

public class EliClientTests
{
    private static readonly EliActId Kc2024 = EliActId.Parse("DU/2024/1061");
    private static readonly EliActId KcBase = EliActId.Parse("DU/1964/93");

    private static EliClient Client(StubHttpMessageHandler handler, EliClientOptions? options = null) =>
        new(new HttpClient(handler), options);

    private static HttpResponseMessage Text(HttpStatusCode status, string body, string mediaType = "text/html") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    /// <summary>Routes by path to the recorded fixtures, like the live API for the Kodeks cywilny.</summary>
    private static HttpResponseMessage Route(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        var accepts = string.Join(",", r.Headers.Accept.Select(a => a.MediaType));
        // like the live API: text.html answers 406 unless HTML is accepted, and the JSON endpoints answer 406 unless JSON is
        if (path.Contains("/text.html/", StringComparison.Ordinal) ? !accepts.Contains("text/html", StringComparison.Ordinal) : !accepts.Contains("application/json", StringComparison.Ordinal))
        {
            return StubHttpMessageHandler.Empty(HttpStatusCode.NotAcceptable);
        }

        return path switch
        {
            "/eli/acts/DU/1964/93" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-1964-93.json"),
            "/eli/acts/DU/2024/1061" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-2024-1061.json"),
            "/eli/acts/DU/2026/795" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-2026-795.json"),
            "/eli/acts/search" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/search-kodeks-cywilny.json"),
            "/eli/acts/DU/2024/1061/struct" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/struct-DU-2024-1061.json"),
            "/eli/acts/DU/2024/1061/text.html/book=TRZECIA/titl=VI/arti=415" => Text(HttpStatusCode.OK, Fixture.Read("eli/fragment-arti-415.html")),
            "/eli/acts/DU/2024/1061/text.html/book=TRZECIA/titl=VI/arti=446_1" => Text(HttpStatusCode.OK, "<div><h3>Art.&nbsp;446<sup>1</sup>.</h3><div>Treść.</div></div>"),
            "/eli/acts/DU/2024/1061/text.html/book=PIERWSZA/part=OG%C3%93LNA/titl=I/arti=1" => Text(HttpStatusCode.OK, Fixture.Read("eli/fragment-arti-1.html")),
            "/eli/acts/DU/2022/5/struct" => StubHttpMessageHandler.Json(HttpStatusCode.OK, "[{\"id\":\"part_1\",\"type\":\"part\",\"children\":[{\"id\":\"para_1\",\"type\":\"para\",\"name\":\"1\",\"title\":\"\u00a7 1.\"},{\"id\":\"para_2\",\"type\":\"para\",\"name\":\"2\",\"title\":\"\u00a7 2.\"}]}]"),
            "/eli/acts/DU/2022/5/text.html/para=2" => Text(HttpStatusCode.OK, "<div><h3>\u00a7&nbsp;2.</h3><div>Tre\u015b\u0107 paragrafu drugiego.</div></div>"),
            _ => StubHttpMessageHandler.Empty(HttpStatusCode.NotFound),
        };
    }

    private static StubHttpMessageHandler Router() => new(Route);

    // ---- search

    [Fact]
    public async Task Search_SendsTheQuery_AndMapsThePage()
    {
        var handler = Router();

        var page = await Client(handler).SearchActsAsync(new EliSearch { Title = "kodeks cywilny", Type = "Ustawa", Publisher = "DU" }, TestContext.Current.CancellationToken);

        Assert.Equal(48, page.TotalRecords);
        Assert.Equal(5, page.Items.Count);
        var url = handler.RequestUris.Single().AbsoluteUri;
        Assert.StartsWith("https://api.sejm.gov.pl/eli/acts/search?title=kodeks%20cywilny", url, StringComparison.Ordinal);
        Assert.Contains("type=Ustawa", url, StringComparison.Ordinal);
        Assert.Contains("publisher=DU", url, StringComparison.Ordinal);
        Assert.Contains("limit=20", url, StringComparison.Ordinal);
        Assert.DoesNotContain("inForce", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_InForceOnly_AddsTheFlag_AndYearAndOffset()
    {
        var handler = Router();

        _ = await Client(handler).SearchActsAsync(new EliSearch { Year = 2025, InForceOnly = true, Limit = 50, Offset = 100 }, TestContext.Current.CancellationToken);

        var q = handler.RequestUris.Single().Query;
        Assert.Contains("inForce=1", q, StringComparison.Ordinal);
        Assert.Contains("year=2025", q, StringComparison.Ordinal);
        Assert.Contains("limit=50", q, StringComparison.Ordinal);
        Assert.Contains("offset=100", q, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_NeedsATitleATypeOrAYear()
    {
        var handler = Router();

        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).SearchActsAsync(new EliSearch(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).SearchActsAsync(new EliSearch { Title = "  ", Publisher = "DU" }, TestContext.Current.CancellationToken));
        Assert.Empty(handler.RequestUris);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(501, 0)]
    [InlineData(20, -1)]
    public async Task Search_LimitAndOffsetAreChecked(int limit, int offset)
    {
        var handler = Router();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Client(handler).SearchActsAsync(new EliSearch { Title = "kodeks", Limit = limit, Offset = offset }, TestContext.Current.CancellationToken));
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task Search_EscapesTheTitle()
    {
        var handler = Router();

        _ = await Client(handler).SearchActsAsync(new EliSearch { Title = "a&b=c d" }, TestContext.Current.CancellationToken);

        Assert.Contains("title=a%26b%3Dc%20d", handler.RequestUris.Single().AbsoluteUri, StringComparison.Ordinal);
    }

    // ---- act

    [Fact]
    public async Task GetAct_Maps()
    {
        var handler = Router();

        var act = await Client(handler).GetActAsync(KcBase, TestContext.Current.CancellationToken);

        Assert.Equal("Dz.U. 1964 nr 16 poz. 93", act!.Summary.DisplayAddress);
        Assert.Equal(11, act.ConsolidatedTexts.Count);
        Assert.Equal("https://api.sejm.gov.pl/eli/acts/DU/1964/93", handler.RequestUris.Single().AbsoluteUri);
    }

    [Fact]
    public async Task GetAct_NotFound_IsNull()
    {
        Assert.Null(await Client(Router()).GetActAsync(EliActId.Parse("DU/2024/999999"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAct_EmptyId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Client(Router()).GetActAsync(default, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAct_UnreadableDate_IsAnUnreadableResponse()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            "{\"publisher\":\"DU\",\"year\":2024,\"pos\":1,\"displayAddress\":\"x\",\"title\":\"t\",\"announcementDate\":\"2024/01/02\"}"));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).GetActAsync(EliActId.Parse("DU/2024/1"), TestContext.Current.CancellationToken));

        Assert.IsType<FormatException>(ex.InnerException);
    }

    [Fact]
    public async Task GetAct_UnreadableJson_IsAnUnreadableResponse()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, "{"));

        await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).GetActAsync(KcBase, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Search_UnreadableItem_IsAnUnreadableResponse()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            "{\"totalCount\":1,\"items\":[{\"publisher\":\"DU\",\"year\":2024,\"displayAddress\":\"x\",\"title\":\"t\"}]}"));

        await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).SearchActsAsync(new EliSearch { Title = "x" }, TestContext.Current.CancellationToken));
    }

    // ---- structure

    [Fact]
    public async Task GetStructure_Maps()
    {
        var units = await Client(Router()).GetStructureAsync(Kc2024, TestContext.Current.CancellationToken);

        Assert.NotNull(units);
        Assert.Contains(Flatten(units!), u => u.Id == "book_TRZECIA-titl_VI-arti_415");
    }

    private static IEnumerable<EliUnit> Flatten(IEnumerable<EliUnit> units) => units.SelectMany(u => new[] { u }.Concat(Flatten(u.Children)));

    [Fact]
    public async Task GetStructure_ActWithoutHtml_404_IsNull()
    {
        Assert.Null(await Client(Router()).GetStructureAsync(EliActId.Parse("DU/2026/795"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStructure_EmptyBody_IsNull()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, string.Empty));

        Assert.Null(await Client(handler).GetStructureAsync(Kc2024, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStructure_IsCachedPerAct()
    {
        var handler = Router();
        var client = Client(handler);

        _ = await client.GetStructureAsync(Kc2024, TestContext.Current.CancellationToken);
        _ = await client.GetStructureAsync(Kc2024, TestContext.Current.CancellationToken);

        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task GetStructure_NotFound_IsNotCached_SoANewlyPublishedStructureIsSeen()
    {
        var handler = Router();
        var client = Client(handler);
        var id = EliActId.Parse("DU/2026/795");

        _ = await client.GetStructureAsync(id, TestContext.Current.CancellationToken);
        _ = await client.GetStructureAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task GetStructure_CacheKeepsOnlyTheLastSixteenActs()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/struct-DU-2024-1061.json"));
        var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        for (var n = 1; n <= 17; n++)
        {
            _ = await client.GetStructureAsync(EliActId.Parse("DU/2024/" + n), ct);
        }

        _ = await client.GetStructureAsync(EliActId.Parse("DU/2024/1"), ct);   // evicted: fetched again
        _ = await client.GetStructureAsync(EliActId.Parse("DU/2024/17"), ct);  // still cached

        Assert.Equal(18, handler.RequestUris.Count);
    }

    // ---- article

    [Fact]
    public async Task Requests_AskForTheMediaTypeEachEndpointServes()
    {
        var handler = Router();

        _ = await Client(handler).GetArticleAsync(Kc2024, "415", TestContext.Current.CancellationToken);

        Assert.Equal("application/json", handler.Requests[0].Accept);   // the structure
        Assert.Equal("text/html", handler.Requests[1].Accept);          // the fragment
    }

    [Fact]
    public async Task GetArticle_ResolvesThroughTheStructure_AndReturnsCleanText()
    {
        var handler = Router();

        var article = await Client(handler).GetArticleAsync(Kc2024, "415", TestContext.Current.CancellationToken);

        Assert.Equal("415", article!.Article);
        Assert.Equal("book_TRZECIA-titl_VI-arti_415", article.UnitId);
        Assert.Equal(Kc2024, article.Act);
        Assert.Equal("Art. 415.\nKto z winy swej wyrządził drugiemu szkodę, obowiązany jest do jej naprawienia.", article.Text);
        Assert.False(article.Truncated);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.EndsWith("/text.html/book=TRZECIA/titl=VI/arti=415", handler.RequestUris[1].AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("446¹")]
    [InlineData("446^1")]
    [InlineData("art. 446_1")]
    public async Task GetArticle_Superscripts_ResolveToTheSameUnit(string input)
    {
        var handler = Router();

        var article = await Client(handler).GetArticleAsync(Kc2024, input, TestContext.Current.CancellationToken);

        Assert.Equal("446_1", article!.Article);
        Assert.Equal("Art. 446^1.\nTreść.", article.Text);
        Assert.EndsWith("/arti=446_1", handler.RequestUris[1].AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetArticle_UnknownArticle_IsNull_WithoutAFragmentRequest()
    {
        var handler = Router();

        Assert.Null(await Client(handler).GetArticleAsync(Kc2024, "999", TestContext.Current.CancellationToken));

        Assert.Single(handler.RequestUris);   // the structure only
    }

    [Theory]
    [InlineData("4l5")]
    [InlineData("")]
    [InlineData("415/416")]
    public async Task GetArticle_MalformedArticle_IsNull_WithoutAnyRequest(string article)
    {
        var handler = Router();

        Assert.Null(await Client(handler).GetArticleAsync(Kc2024, article, TestContext.Current.CancellationToken));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GetArticle_ActWithoutHtml_IsNull_AfterOneRequest()
    {
        var handler = Router();

        Assert.Null(await Client(handler).GetArticleAsync(EliActId.Parse("DU/2026/795"), "415", TestContext.Current.CancellationToken));

        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task GetArticle_EmptyFragment_IsNull()
    {
        var handler = new StubHttpMessageHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/struct", StringComparison.Ordinal)
            ? StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/struct-DU-2024-1061.json")
            : Text(HttpStatusCode.OK, string.Empty));

        Assert.Null(await Client(handler).GetArticleAsync(Kc2024, "415", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetArticle_FragmentThatIsAllMarkup_IsNull()
    {
        var handler = new StubHttpMessageHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/struct", StringComparison.Ordinal)
            ? StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/struct-DU-2024-1061.json")
            : Text(HttpStatusCode.OK, "<div><script>x</script></div>"));

        Assert.Null(await Client(handler).GetArticleAsync(Kc2024, "415", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetArticle_UnitIdWithUnexpectedCharacters_IsNull_NotInjectedIntoThePath()
    {
        var handler = new StubHttpMessageHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/struct", StringComparison.Ordinal)
            ? StubHttpMessageHandler.Json(HttpStatusCode.OK, "[{\"id\":\"book_1/../../x-arti_415\",\"type\":\"arti\",\"name\":\"415\"}]")
            : Text(HttpStatusCode.OK, "<p>no</p>"));

        Assert.Null(await Client(handler).GetArticleAsync(Kc2024, "415", TestContext.Current.CancellationToken));

        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task GetArticle_HugeFragment_IsAnUnreadableResponse()
    {
        var handler = new StubHttpMessageHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/struct", StringComparison.Ordinal)
            ? StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/struct-DU-2024-1061.json")
            : Text(HttpStatusCode.OK, new string('x', 2_000_000)));

        await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).GetArticleAsync(Kc2024, "415", TestContext.Current.CancellationToken));
    }

    // ---- errors and options

    [Fact]
    public async Task Status429_IsQuota_WithRetryAfter()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = StubHttpMessageHandler.Json((HttpStatusCode)429, "{}");
            response.Headers.TryAddWithoutValidation("Retry-After", "30");
            return response;
        });

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => Client(handler).GetActAsync(KcBase, TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
    }

    [Fact]
    public async Task ServerError_IsTransient()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(HttpStatusCode.BadGateway));

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).GetActAsync(KcBase, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OtherClientError_IsAnApiExceptionWithTheSnippet()
    {
        var handler = new StubHttpMessageHandler(_ => Text(HttpStatusCode.BadRequest, "bad request details", "text/plain"));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).SearchActsAsync(new EliSearch { Title = "x" }, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("bad request details", ex.ResponseSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsHttpBaseAddress()
    {
        Assert.Throws<ArgumentException>(() => new EliClient(new HttpClient(), new EliClientOptions { BaseAddress = new Uri("http://api.sejm.gov.pl/eli/") }));
    }

    [Fact]
    public async Task Requests_GoToTheConfiguredBaseAddress_EvenWithoutATrailingSlash()
    {
        var handler = Router();
        var client = Client(handler, new EliClientOptions { BaseAddress = new Uri("https://example.test/eli") });

        _ = await client.GetActAsync(KcBase, TestContext.Current.CancellationToken);

        Assert.Equal("https://example.test/eli/acts/DU/1964/93", handler.RequestUris.Single().AbsoluteUri);
    }

    [Fact]
    public async Task GetArticle_NonAsciiUnitIds_AreReadAndEscapedInThePath()
    {
        var handler = Router();

        var article = await Client(handler).GetArticleAsync(Kc2024, "1", TestContext.Current.CancellationToken);

        Assert.Equal("book_PIERWSZA-part_OG\u00d3LNA-titl_I-arti_1", article!.UnitId);
        Assert.Contains("Kodeks niniejszy reguluje", article.Text, StringComparison.Ordinal);
        var path = handler.RequestUris[1].AbsolutePath;
        Assert.Contains("part=OG%C3%93LNA", path, StringComparison.Ordinal);
        Assert.EndsWith("/arti=1", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetArticle_AParagraphOfARegulation_IsReadByItsParagraphSign()
    {
        var article = await Client(Router()).GetArticleAsync(EliActId.Parse("DU/2022/5"), "\u00a7 2", TestContext.Current.CancellationToken);

        Assert.Equal("para", article!.UnitType);
        Assert.Equal("2", article.Article);
        Assert.Equal("\u00a7 2.\nTre\u015b\u0107 paragrafu drugiego.", article.Text);
    }

    [Fact]
    public async Task GetArticle_AnArticleNumberDoesNotMatchAParagraph()
    {
        var handler = Router();

        Assert.Null(await Client(handler).GetArticleAsync(EliActId.Parse("DU/2022/5"), "2", TestContext.Current.CancellationToken));

        Assert.Single(handler.RequestUris);   // the structure only: it has paragraphs, no articles
    }

    [Fact]
    public async Task GetArticle_AFragmentThatTimesTheTextExtractionOut_IsNeverARawTimeoutException()
    {
        var hostile = "<!--" + string.Concat(Enumerable.Repeat("<!--", 240_000));
        var handler = new StubHttpMessageHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/struct", StringComparison.Ordinal)
            ? StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/struct-DU-2024-1061.json")
            : Text(HttpStatusCode.OK, hostile));

        var ex = await Record.ExceptionAsync(() => Client(handler).GetArticleAsync(Kc2024, "415", TestContext.Current.CancellationToken));

        Assert.True(ex is null or PolishOpenDataApiException, ex?.GetType().Name);
    }
}
