using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Mcp.Tests;

/// <summary>The legal-act tools through a real MCP session; the Sejm ELI API answers from recorded fixtures. They exist without any key.</summary>
public sealed class EliToolsTests : IAsyncDisposable
{
    private const string Source = "Source: Sejm RP, ELI API (api.sejm.gov.pl)";

    private McpTestSession? _session;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    private async Task<CallToolResult> CallAsync(StubHttpMessageHandler handler, string tool, Dictionary<string, object?> args)
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _session = await McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero)),
            TestContext.Current.CancellationToken);
        return await _session.Client.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static JsonElement Data(CallToolResult result)
    {
        Assert.True(result.IsError != true, Text(result));
        return result.StructuredContent!.Value;
    }

    private static HttpResponseMessage Html(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html") };

    private static StubHttpMessageHandler Router(Func<string, HttpResponseMessage?>? custom = null) => new(r =>
    {
        var path = r.RequestUri!.AbsolutePath;
        var accepts = string.Join(",", r.Headers.Accept.Select(a => a.MediaType));
        // like the live API: text.html answers 406 unless HTML is accepted, and the JSON endpoints answer 406 unless JSON is
        if (path.Contains("/text.html/", StringComparison.Ordinal) ? !accepts.Contains("text/html", StringComparison.Ordinal) : !accepts.Contains("application/json", StringComparison.Ordinal))
        {
            return StubHttpMessageHandler.Empty(HttpStatusCode.NotAcceptable);
        }

        if (custom?.Invoke(path) is { } response)
        {
            return response;
        }

        return path switch
        {
            "/eli/acts/DU/1964/93" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-1964-93.json"),
            "/eli/acts/DU/2026/795" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-2026-795.json"),
            "/eli/acts/DU/2025/1071" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-2025-1071.json"),
            "/eli/acts/DU/2024/1061" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-2024-1061.json"),
            "/eli/acts/search" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/search-kodeks-cywilny.json"),
            "/eli/acts/DU/2024/1061/struct" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/struct-DU-2024-1061.json"),
            "/eli/acts/DU/2024/1061/text.html/book=TRZECIA/titl=VI/arti=415" => Html(Fixture.Read("eli/fragment-arti-415.html")),
            _ => StubHttpMessageHandler.Empty(HttpStatusCode.NotFound),
        };
    });

    [Fact]
    public async Task Tools_AreListedWithoutAnyKey_AndNamedInTheInstructions()
    {
        var handler = Router();
        await using var session = await McpTestSession.StartAsync(http => http.ConfigurePrimaryHttpMessageHandler(() => handler), null, TestContext.Current.CancellationToken);

        var tools = await session.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var names = tools.Select(t => t.Name).ToHashSet();
        Assert.Contains("search_legal_acts", names);
        Assert.Contains("get_legal_act", names);
        Assert.Contains("get_legal_article", names);
        Assert.Contains("isCurrent", session.Client.ServerInstructions ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(handler.RequestUris);   // listing the tools makes no request
    }

    [Fact]
    public async Task SearchLegalActs_ReturnsActsWithIdsStatusAndTheTotal()
    {
        var handler = Router();

        var data = Data(await CallAsync(handler, "search_legal_acts", new() { ["title"] = "kodeks cywilny", ["type"] = "Ustawa", ["year"] = 1964 }));

        Assert.Equal(48, data.GetProperty("totalMatches").GetInt32());
        var acts = data.GetProperty("acts").EnumerateArray().ToList();
        Assert.Equal(5, acts.Count);
        Assert.Equal("DU/2026/184", acts[0].GetProperty("eli").GetString());
        Assert.False(string.IsNullOrEmpty(acts[0].GetProperty("title").GetString()));
        Assert.Equal(Source, data.GetProperty("source").GetString());
        var url = Assert.Single(handler.RequestUris).Query;
        Assert.Contains("year=1964", url, StringComparison.Ordinal);
        Assert.Contains("type=Ustawa", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchLegalActs_NeedsACriterion()
    {
        var handler = Router();

        var result = await CallAsync(handler, "search_legal_acts", new() { ["inForceOnly"] = true });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GetLegalArticle_ForABaseAct_QuotesTheNewestHtmlText_AndWarnsThatItIsNotCurrent()
    {
        var handler = Router();

        var data = Data(await CallAsync(handler, "get_legal_article", new() { ["eli"] = "DU/1964/93", ["article"] = "415" }));

        Assert.Contains("szkod", data.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("415", data.GetProperty("article").GetString());
        Assert.Equal("DU/2024/1061", data.GetProperty("textSource").GetProperty("eli").GetString());
        Assert.Equal("2024-06-21", data.GetProperty("textSource").GetProperty("announcementDate").GetString());
        Assert.False(data.GetProperty("isCurrent").GetBoolean());
        var newer = data.GetProperty("newerConsolidatedTexts").EnumerateArray().Select(e => e.GetProperty("eli").GetString()).ToList();
        Assert.Equal(["DU/2026/795", "DU/2025/1071"], newer);
        Assert.Equal(5, data.GetProperty("amendmentsSince").GetProperty("count").GetInt32());
        Assert.Equal(1, data.GetProperty("futureAmendments").GetProperty("count").GetInt32());
        var warning = data.GetProperty("warning").GetString();
        Assert.Contains("Dz.U. 2026 poz. 795", warning, StringComparison.Ordinal);
        Assert.Contains("older", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Source, data.GetProperty("source").GetString());
        Assert.True(handler.RequestUris.Count <= 8, "requests: " + handler.RequestUris.Count);
    }

    [Fact]
    public async Task GetLegalArticle_ForAConsolidatedTextWithHtml_QuotesThatTextAndComparesItWithItsBaseAct()
    {
        var handler = Router();

        var data = Data(await CallAsync(handler, "get_legal_article", new() { ["eli"] = "DU/2024/1061", ["article"] = "art. 415" }));

        Assert.Equal("DU/2024/1061", data.GetProperty("textSource").GetProperty("eli").GetString());
        Assert.False(data.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(2, data.GetProperty("newerConsolidatedTexts").GetArrayLength());
        Assert.Equal(5, data.GetProperty("amendmentsSince").GetProperty("count").GetInt32());
        Assert.Equal(4, handler.RequestUris.Count);   // the act, its base act, the structure and the text
    }

    [Fact]
    public async Task GetLegalArticle_PdfOnlyConsolidatedText_IsAToolErrorWithThePdfLink()
    {
        var result = await CallAsync(Router(), "get_legal_article", new() { ["eli"] = "DU/2026/795", ["article"] = "415" });

        Assert.True(result.IsError);
        Assert.Contains("https://api.sejm.gov.pl/eli/acts/DU/2026/795/text.pdf", Text(result), StringComparison.Ordinal);
        Assert.Contains("DU/1964/93", Text(result), StringComparison.Ordinal);   // the base act to ask for the newest text with HTML
    }

    [Fact]
    public async Task GetLegalArticle_UnknownArticle_IsAToolError_WithoutAFragmentRequest()
    {
        var handler = Router();

        var result = await CallAsync(handler, "get_legal_article", new() { ["eli"] = "DU/1964/93", ["article"] = "99999" });

        Assert.True(result.IsError);
        Assert.Contains("99999", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain(handler.RequestUris, u => u.AbsolutePath.Contains("/text.html/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not an id", "415")]
    [InlineData("DU/1964/93", "4l5")]
    [InlineData("DU/1964/93", "")]
    public async Task GetLegalArticle_BadInput_IsAToolError_WithoutRequests(string eli, string article)
    {
        var handler = Router();

        var result = await CallAsync(handler, "get_legal_article", new() { ["eli"] = eli, ["article"] = article });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GetLegalArticle_ActNotFound_IsAToolError()
    {
        var result = await CallAsync(Router(), "get_legal_article", new() { ["eli"] = "DU/2020/999999", ["article"] = "1" });

        Assert.True(result.IsError);
        Assert.Contains("DU/2020/999999", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetLegalArticle_ABaseActWithoutAnyHtmlText_IsAToolErrorWithTheNewestPdf()
    {
        var handler = Router(p => p == "/eli/acts/DU/1964/93"
            ? StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"publisher\":\"DU\",\"year\":1964,\"pos\":93,\"displayAddress\":\"Dz.U. 1964 poz. 93\",\"title\":\"t\",\"textHTML\":false,\"textPDF\":true,\"references\":{\"Inf. o tekście jednolitym\":[{\"id\":\"DU/2026/795\"}]}}")
            : null);

        var result = await CallAsync(handler, "get_legal_article", new() { ["eli"] = "DU/1964/93", ["article"] = "415" });

        Assert.True(result.IsError);
        Assert.Contains("https://api.sejm.gov.pl/eli/acts/DU/2026/795/text.pdf", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetLegalAct_ForABaseAct_ReportsCurrencyAndFutureAmendments()
    {
        var handler = Router();

        var data = Data(await CallAsync(handler, "get_legal_act", new() { ["eli"] = "DU/1964/93" }));

        Assert.Equal("DU/1964/93", data.GetProperty("eli").GetString());
        Assert.Equal("Dz.U. 1964 nr 16 poz. 93", data.GetProperty("displayAddress").GetString());
        Assert.True(data.GetProperty("inForce").GetBoolean());
        var currency = data.GetProperty("currency");
        Assert.Equal("DU/2026/795", currency.GetProperty("newestConsolidated").GetProperty("eli").GetString());
        Assert.False(currency.GetProperty("newestConsolidated").GetProperty("hasHtml").GetBoolean());
        Assert.Equal("DU/2024/1061", currency.GetProperty("newestWithHtml").GetProperty("eli").GetString());
        Assert.False(currency.GetProperty("isHtmlTextCurrent").GetBoolean());
        var future = currency.GetProperty("futureAmendments").GetProperty("items").EnumerateArray().Single();
        Assert.Equal("DU/2026/507", future.GetProperty("eli").GetString());
        Assert.Equal("2028-11-01", future.GetProperty("effectiveDate").GetString());
        Assert.Equal("https://api.sejm.gov.pl/eli/acts/DU/1964/93/text.pdf", data.GetProperty("pdfLink").GetString());
        Assert.Equal(Source, data.GetProperty("source").GetString());
        Assert.True(handler.RequestUris.Count <= 6, "requests: " + handler.RequestUris.Count);
    }

    [Fact]
    public async Task GetLegalAct_ForAConsolidatedText_NamesItsBaseAct()
    {
        var data = Data(await CallAsync(Router(), "get_legal_act", new() { ["eli"] = "DU/2026/795" }));

        Assert.Equal("DU/1964/93", Assert.Single(data.GetProperty("baseActs").EnumerateArray()).GetString());
        Assert.False(data.GetProperty("hasHtml").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("currency").ValueKind);
    }

    [Fact]
    public async Task GetLegalAct_BadOrMissingEli_IsAToolError()
    {
        var handler = Router();

        var bad = await CallAsync(handler, "get_legal_act", new() { ["eli"] = "nope" });
        var missing = await CallAsync(handler, "get_legal_act", new() { ["eli"] = "DU/2020/999999" });

        Assert.True(bad.IsError);
        Assert.True(missing.IsError);
    }

    [Fact]
    public async Task Quota_IsExplainedForTheLegalTools()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json((HttpStatusCode)429, "{}"));

        var result = await CallAsync(handler, "get_legal_act", new() { ["eli"] = "DU/1964/93" });

        Assert.True(result.IsError);
        Assert.Contains("Sejm", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryAnswerNamesItsSource()
    {
        var handler = Router();

        foreach (var (tool, args) in new (string, Dictionary<string, object?>)[]
        {
            ("search_legal_acts", new() { ["title"] = "kodeks" }),
            ("get_legal_act", new() { ["eli"] = "DU/1964/93" }),
            ("get_legal_article", new() { ["eli"] = "DU/1964/93", ["article"] = "415" }),
        })
        {
            Assert.Equal(Source, Data(await CallAsync(handler, tool, args)).GetProperty("source").GetString());
        }
    }
}
