using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Sejm.Tests;

public class SejmClientTests
{
    private static SejmClient Client(StubHttpMessageHandler handler, SejmClientOptions? options = null) =>
        new(new HttpClient(handler), options);

    private static HttpResponseMessage WithTotal(HttpResponseMessage response, int total)
    {
        response.Headers.TryAddWithoutValidation("X-Total-Count", total.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }

    /// <summary>Routes by path to the recorded fixtures, like the live API: 406 unless JSON is accepted, 404 or 204 or 200 [] for the odd not-found forms.</summary>
    internal static HttpResponseMessage Route(HttpRequestMessage r)
    {
        var accepts = string.Join(",", r.Headers.Accept.Select(a => a.MediaType));
        if (!accepts.Contains("application/json", StringComparison.Ordinal))
        {
            return StubHttpMessageHandler.Empty(HttpStatusCode.NotAcceptable);
        }

        var path = r.RequestUri!.AbsolutePath;
        return path switch
        {
            "/sejm/term" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/terms.json"),
            "/sejm/term10/processes" or "/sejm/term10/processes/passed" => WithTotal(StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/processes-page.json"), 1794),
            "/sejm/term10/processes/318" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/process-318.json"),
            "/sejm/term10/prints/401" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/print-401.json"),
            "/sejm/term10/prints/1153-A" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/print-1153-A.json"),
            "/sejm/term10/bills" => r.RequestUri.Query.Contains("print=318", StringComparison.Ordinal)
                ? StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/bills-print-318.json")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"),
            var p when p.StartsWith("/sejm/term99/", StringComparison.Ordinal) => StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"),
            _ => StubHttpMessageHandler.Empty(HttpStatusCode.NotFound),
        };
    }

    private static StubHttpMessageHandler Router() => new(Route);

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- pipeline

    [Fact]
    public async Task Status429_IsQuota_WithRetryAfter()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = StubHttpMessageHandler.Json((HttpStatusCode)429, "{}");
            response.Headers.TryAddWithoutValidation("Retry-After", "30");
            return response;
        });

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => Client(handler).GetTermsAsync(Ct));

        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
    }

    [Fact]
    public async Task ServerError_IsTransient()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(HttpStatusCode.BadGateway));

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).GetTermsAsync(Ct));
    }

    [Fact]
    public async Task ClientError_HasTheSnippet()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Html(HttpStatusCode.BadRequest, "bad request details"));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).GetTermsAsync(Ct));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("bad request details", ex.ResponseSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Waf403Html_IsApiException()
    {
        // the WAF answers 403 with an HTML page under a JSON content type: never a JsonException
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.Forbidden, "<html><body>The requested URL was rejected.</body></html>"));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).SearchProcessesAsync(new SejmProcessSearch { Term = 10 }, Ct));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Contains("rejected", ex.ResponseSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreadableJson_IsUnreadableResponse()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, "<html>not json</html>"));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).GetTermsAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task UnreadableDate_IsUnreadableResponse()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """[{"num":10,"from":"13.11.2023","current":true}]"""));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).GetTermsAsync(Ct));

        Assert.IsType<FormatException>(ex.InnerException);
    }

    [Fact]
    public async Task OverTheCap_RefusesToRead()
    {
        var big = "[" + new string(' ', 2_100_000) + "]";
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, big));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).GetTermsAsync(Ct));

        Assert.Contains("refusing to read it", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryRequest_AsksForJson_AndTheStubAnswers406Otherwise()
    {
        var handler = Router();

        _ = await Client(handler).GetTermsAsync(Ct);

        Assert.All(handler.Requests, r => Assert.Equal("application/json", r.Accept));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task NotFoundForms_404And204_AreNull(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(status));

        Assert.Null(await Client(handler).GetProcessAsync(10, "318", Ct));
    }

    [Fact]
    public async Task EmptyBody_OnAList_IsAnEmptyList()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, string.Empty));

        Assert.Empty(await Client(handler).GetTermsAsync(Ct));
    }

    [Fact]
    public void Constructor_RejectsHttpBaseAddress()
    {
        Assert.Throws<ArgumentException>(() => new SejmClient(new HttpClient(), new SejmClientOptions { BaseAddress = new Uri("http://api.sejm.gov.pl/sejm/") }));
    }

    [Fact]
    public async Task BaseAddressWithoutTrailingSlash_StillResolves()
    {
        var handler = Router();

        _ = await Client(handler, new SejmClientOptions { BaseAddress = new Uri("https://api.sejm.gov.pl/sejm") }).GetTermsAsync(Ct);

        Assert.Equal("https://api.sejm.gov.pl/sejm/term", handler.RequestUris.Single().AbsoluteUri);
    }

    // ---- terms

    [Fact]
    public async Task GetTerms_ReturnsAllTerms()
    {
        var terms = await Client(Router()).GetTermsAsync(Ct);

        Assert.Equal(Enumerable.Range(1, terms.Count), terms.Select(t => t.Number));
        Assert.Contains(terms, t => t.Number == 10);
    }

    [Fact]
    public async Task GetCurrentTerm_Is10()
    {
        var term = await Client(Router()).GetCurrentTermAsync(Ct);

        Assert.Equal(10, term.Number);
        Assert.True(term.Current);
    }

    [Fact]
    public async Task GetCurrentTerm_NoneCurrent_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """[{"num":9,"from":"2019-11-12","to":"2023-11-12","current":false}]"""));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).GetCurrentTermAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
    }

    // ---- processes

    [Fact]
    public async Task Search_SendsTheQuery_AndReadsXTotalCount()
    {
        var handler = Router();

        var page = await Client(handler).SearchProcessesAsync(new SejmProcessSearch
        {
            Term = 10,
            Title = "projekt uchwały",
            DocumentType = "projekt uchwały",
            ModifiedSince = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Unspecified),
        }, Ct);

        Assert.Equal(1794, page.Total);
        Assert.Equal(5, page.Items.Count);
        Assert.True(page.HasMore);
        var url = handler.RequestUris.Single().AbsoluteUri;
        Assert.StartsWith("https://api.sejm.gov.pl/sejm/term10/processes?title=projekt%20uchwa%C5%82y", url, StringComparison.Ordinal);
        Assert.Contains("documentType=projekt%20uchwa%C5%82y", url, StringComparison.Ordinal);
        Assert.Contains("modifiedSince=2026-11-01T00%3A00%3A00", url, StringComparison.Ordinal);
        Assert.EndsWith("&limit=20&offset=0", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_PassedOnly_UsesThePassedPath()
    {
        var handler = Router();

        _ = await Client(handler).SearchProcessesAsync(new SejmProcessSearch { Term = 10, PassedOnly = true, Limit = 5 }, Ct);

        Assert.Equal("/sejm/term10/processes/passed", handler.RequestUris.Single().AbsolutePath);
    }

    [Fact]
    public async Task Search_LastPage_HasNoMore()
    {
        var handler = new StubHttpMessageHandler(_ => WithTotal(StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/processes-page.json"), 1005));

        var page = await Client(handler).SearchProcessesAsync(new SejmProcessSearch { Term = 10, Offset = 1000, Limit = 20 }, Ct);

        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Search_UnknownTerm_IsAnEmptyPage()
    {
        var page = await Client(Router()).SearchProcessesAsync(new SejmProcessSearch { Term = 99 }, Ct);

        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
    }

    [Theory]
    [InlineData(0, 0, 10)]
    [InlineData(501, 0, 10)]
    [InlineData(20, -1, 10)]
    [InlineData(20, 0, 0)]
    public async Task Search_OutOfRange_Throws_WithoutARequest(int limit, int offset, int term)
    {
        var handler = Router();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Client(handler).SearchProcessesAsync(new SejmProcessSearch { Term = term, Limit = limit, Offset = offset }, Ct));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GetProcess_Maps318()
    {
        var process = await Client(Router()).GetProcessAsync(10, "318", Ct);

        Assert.NotNull(process);
        Assert.Equal("DU/2024/878", process.Summary.Eli);
    }

    [Fact]
    public async Task GetProcess_404_IsNull()
    {
        Assert.Null(await Client(Router()).GetProcessAsync(10, "401", Ct));
    }

    [Fact]
    public async Task GetProcess_EscapesTheNumber_AndTrimsIt()
    {
        var handler = Router();

        _ = await Client(handler).GetProcessAsync(10, " 128-BA ", Ct);

        Assert.Equal("/sejm/term10/processes/128-BA", handler.RequestUris.Single().AbsolutePath);
    }

    [Theory]
    [InlineData("1344/../x")]
    [InlineData("")]
    public async Task GetProcess_BadNumber_Throws_WithoutARequest(string number)
    {
        var handler = Router();

        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).GetProcessAsync(10, number, Ct));

        Assert.Empty(handler.RequestUris);
    }

    // ---- prints and bills

    [Fact]
    public async Task GetPrint_401_PointsAtProcess318()
    {
        var print = await Client(Router()).GetPrintAsync(10, "401", Ct);

        Assert.NotNull(print);
        Assert.Equal("318", print.ProcessNumber);
    }

    [Fact]
    public async Task GetPrint_Suffix_IsAddressed()
    {
        var handler = Router();

        var print = await Client(handler).GetPrintAsync(10, "1153-A", Ct);

        Assert.NotNull(print);
        Assert.Null(print.ProcessNumber);
        Assert.Equal("/sejm/term10/prints/1153-A", handler.RequestUris.Single().AbsolutePath);
    }

    [Fact]
    public async Task GetBill_FirstItemWhoseHasThePrint()
    {
        var handler = Router();

        var bill = await Client(handler).GetBillForPrintAsync(10, "318", Ct);

        Assert.NotNull(bill);
        Assert.Equal("318", bill.Print);
        Assert.Equal("/sejm/term10/bills", handler.RequestUris.Single().AbsolutePath);
        Assert.Equal("?print=318&limit=5", handler.RequestUris.Single().Query);
    }

    [Fact]
    public async Task GetBill_NoMatch_IsNull()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """[{"number":"X","print":"999","title":"t","submissionType":"BILL","applicantType":"GOVERNMENT","dateOfReceipt":"2024-01-01"}]"""));

        Assert.Null(await Client(handler).GetBillForPrintAsync(10, "318", Ct));
    }

    [Fact]
    public async Task GetBill_NoItems_IsNull()
    {
        Assert.Null(await Client(Router()).GetBillForPrintAsync(10, "5", Ct));
    }
}
