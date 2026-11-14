using System;
using System.Collections.Generic;
using System.Globalization;
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

/// <summary>The Sejm tools through a real MCP session; the Sejm API answers from recorded fixtures. They exist without any key.</summary>
public sealed class SejmToolsTests : IAsyncDisposable
{
    private const string Source = "Source: Sejm RP, API Sejmu (api.sejm.gov.pl)";

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

    private static HttpResponseMessage WithTotal(HttpResponseMessage response, int total)
    {
        response.Headers.TryAddWithoutValidation("X-Total-Count", total.ToString(CultureInfo.InvariantCulture));
        return response;
    }

    private static List<string> SejmPaths(StubHttpMessageHandler handler) =>
        handler.RequestUris.Select(u => u.PathAndQuery).Where(p => p.StartsWith("/sejm/", StringComparison.Ordinal)).ToList();

    /// <summary>Routes by path to the recorded fixtures, like the live API: 406 unless JSON is accepted, 404 for unknown items.</summary>
    private static StubHttpMessageHandler Router(Func<HttpRequestMessage, HttpResponseMessage?>? custom = null) => new(r =>
    {
        var accepts = string.Join(",", r.Headers.Accept.Select(a => a.MediaType));
        if (!accepts.Contains("application/json", StringComparison.Ordinal))
        {
            return StubHttpMessageHandler.Empty(HttpStatusCode.NotAcceptable);
        }

        if (custom?.Invoke(r) is { } response)
        {
            return response;
        }

        var path = r.RequestUri!.AbsolutePath;
        return path switch
        {
            "/sejm/term" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/terms.json"),
            "/sejm/term10/processes" or "/sejm/term10/processes/passed" => WithTotal(StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/processes-page.json"), 1794),
            "/sejm/term10/processes/318" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/process-318.json"),
            "/sejm/term10/processes/900" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/process-nested.json"),
            "/sejm/term10/prints/401" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/print-401.json"),
            "/sejm/term10/prints/1153-A" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/print-1153-A.json"),
            "/sejm/term10/bills" => r.RequestUri.Query.Contains("print=318", StringComparison.Ordinal)
                ? StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/bills-print-318.json")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"),
            var p when p.StartsWith("/sejm/term99/", StringComparison.Ordinal) => StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"),
            _ => StubHttpMessageHandler.Empty(HttpStatusCode.NotFound),
        };
    });

    // ---- listing and terms

    [Fact]
    public async Task Tools_AreListedWithoutAnyKey()
    {
        var handler = Router();
        await using var session = await McpTestSession.StartAsync(http => http.ConfigurePrimaryHttpMessageHandler(() => handler), null, TestContext.Current.CancellationToken);

        var tools = await session.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var names = tools.Select(t => t.Name).ToHashSet();
        Assert.Contains("search_sejm_processes", names);
        Assert.Contains("get_sejm_process", names);
        Assert.Empty(handler.RequestUris);   // listing the tools makes no request
    }

    [Fact]
    public async Task OmittedTerm_CostsOneTermRequest_AndUsesTheCurrentTerm()
    {
        var handler = Router();

        var data = Data(await CallAsync(handler, "search_sejm_processes", new() { ["limit"] = 3 }));

        Assert.Equal(10, data.GetProperty("term").GetInt32());
        var paths = SejmPaths(handler);
        Assert.Equal("/sejm/term", paths[0]);
        Assert.Equal(2, paths.Count);
        Assert.Equal(Source, data.GetProperty("source").GetString());
    }

    [Fact]
    public async Task UnknownTerm_SaysNoSuchTerm_NotNothingFound()
    {
        var handler = Router();

        var result = await CallAsync(handler, "search_sejm_processes", new() { ["term"] = 99, ["title"] = "ustawa" });

        Assert.True(result.IsError);
        Assert.Contains("No term 99", Text(result), StringComparison.Ordinal);
        Assert.Contains("/sejm/term", SejmPaths(handler));   // the second look at the terms list
    }

    // ---- search_sejm_processes

    [Fact]
    public async Task Search_StatusPublishedAsAct_AndPassedWithoutEli()
    {
        var handler = Router(r => r.RequestUri!.AbsolutePath == "/sejm/term10/processes"
            ? WithTotal(StubHttpMessageHandler.Json(HttpStatusCode.OK, """
                [
                 {"number":"318","title":"Rządowy projekt ustawy","documentType":"projekt ustawy","passed":true,"ELI":"DU/2024/878","displayAddress":"Dz.U. 2024 poz. 878","changeDate":"2024-06-27T19:35:17","processStartDate":"2024-04-18","closureDate":"2024-05-23","term":10},
                 {"number":"5","title":"Wniosek","documentType":"wniosek","passed":true,"changeDate":"2023-11-14T11:44:49","processStartDate":"2023-11-13","closureDate":"2023-11-13","term":10},
                 {"number":"6","title":"Projekt odrzucony","documentType":"projekt ustawy","passed":false,"changeDate":"2023-12-14T11:44:49","processStartDate":"2023-11-13","closureDate":"2023-12-13","term":10},
                 {"number":"7","title":"Projekt w toku","documentType":"projekt ustawy","passed":false,"changeDate":"2026-12-14T11:44:49","processStartDate":"2026-11-13","term":10}
                ]
                """), 4)
            : null);

        var data = Data(await CallAsync(handler, "search_sejm_processes", new() { ["term"] = 10, ["title"] = "projekt" }));

        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(4, data.GetProperty("totalMatches").GetInt32());
        Assert.Equal("passed; published as Dz.U. 2024 poz. 878", items[0].GetProperty("status").GetString());
        Assert.Equal("DU/2024/878", items[0].GetProperty("eli").GetString());
        Assert.Equal("passed; not published as an act (motion, appointment or similar)", items[1].GetProperty("status").GetString());
        Assert.Equal("not passed; closed on 2023-12-13", items[2].GetProperty("status").GetString());
        Assert.Equal("in progress", items[3].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("eli").ValueKind);
    }

    [Fact]
    public async Task Search_PrintNumber_GoesThroughPrint_AndIgnoresTheOtherFilters()
    {
        var handler = Router();

        var data = Data(await CallAsync(handler, "search_sejm_processes", new() { ["term"] = 10, ["printNumber"] = "401", ["title"] = "ignored", ["passedOnly"] = true }));

        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("318", item.GetProperty("number").GetString());
        Assert.Equal("DU/2024/878", item.GetProperty("eli").GetString());
        Assert.Equal(new[] { "/sejm/term10/prints/401", "/sejm/term10/processes/318" }, SejmPaths(handler));
    }

    [Fact]
    public async Task Search_PrintWithoutAProcess_SaysSo()
    {
        var handler = Router();

        var data = Data(await CallAsync(handler, "search_sejm_processes", new() { ["term"] = 10, ["printNumber"] = "1153-A" }));

        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Contains("belongs to no process", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task Search_ChangedSinceDays_OutOfRange_IsToolError(int days)
    {
        var handler = Router();

        var result = await CallAsync(handler, "search_sejm_processes", new() { ["term"] = 10, ["changedSinceDays"] = days });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task Search_ChangedSinceDays_SendsWarsawMidnight()
    {
        var handler = Router();

        _ = Data(await CallAsync(handler, "search_sejm_processes", new() { ["term"] = 10, ["changedSinceDays"] = 3 }));

        Assert.Contains("modifiedSince=2026-11-11T00%3A00%3A00", SejmPaths(handler).Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Search_LimitOutOfRange_IsToolError(int limit)
    {
        var handler = Router();

        var result = await CallAsync(handler, "search_sejm_processes", new() { ["term"] = 10, ["limit"] = limit });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task NewestFirst_ReversesFromEnd_WithOneCountRequest()
    {
        var handler = Router();

        var data = Data(await CallAsync(handler, "search_sejm_processes", new() { ["term"] = 10, ["newestFirst"] = true, ["limit"] = 3, ["offset"] = 1 }));

        var paths = SejmPaths(handler);
        Assert.Equal(2, paths.Count);
        Assert.EndsWith("limit=1&offset=0", paths[0], StringComparison.Ordinal);
        // total 1794, offset 1, take 3: the server page starts at 1794 - 1 - 3 = 1790
        Assert.EndsWith("limit=3&offset=1790", paths[1], StringComparison.Ordinal);
        var numbers = data.GetProperty("items").EnumerateArray().Select(i => int.Parse(i.GetProperty("number").GetString()!, CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(3, numbers.Count);
        Assert.Equal(numbers.OrderByDescending(n => n).ToList(), numbers);
    }

    [Fact]
    public async Task NewestFirst_OffsetPastEnd_IsEmptyWithOneCountRequest()
    {
        var handler = Router();

        var data = Data(await CallAsync(handler, "search_sejm_processes", new() { ["term"] = 10, ["newestFirst"] = true, ["offset"] = 2000 }));

        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Equal(1794, data.GetProperty("totalMatches").GetInt32());
        Assert.Single(SejmPaths(handler));
    }

    [Fact]
    public async Task Search_ZeroHitsForAMultiWordTitle_HintsAShorterFragment()
    {
        var handler = Router(r => r.RequestUri!.AbsolutePath == "/sejm/term10/processes" ? WithTotal(StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"), 0) : null);

        var data = Data(await CallAsync(handler, "search_sejm_processes", new() { ["term"] = 10, ["title"] = "kodeks pracy zmiana" }));

        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Contains("shorter", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    // ---- get_sejm_process

    [Fact]
    public async Task GetProcess_ReportPrint401_ResolvesTo318_WithTheActHint()
    {
        var handler = Router();

        var data = Data(await CallAsync(handler, "get_sejm_process", new() { ["term"] = 10, ["number"] = "401" }));

        Assert.Equal("318", data.GetProperty("number").GetString());
        var act = data.GetProperty("act");
        Assert.Equal("DU/2024/878", act.GetProperty("eli").GetString());
        Assert.Contains("get_legal_act", act.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("passed; published as Dz.U. 2024 poz. 878", data.GetProperty("status").GetString());
        Assert.Equal(new[] { "/sejm/term10/processes/401", "/sejm/term10/prints/401", "/sejm/term10/processes/318", "/sejm/term10/bills?print=318&limit=5" }, SejmPaths(handler));
    }

    [Fact]
    public async Task GetProcess_SubmittedByFromTheBill_AndTheTimeline()
    {
        var handler = Router();

        var data = Data(await CallAsync(handler, "get_sejm_process", new() { ["term"] = 10, ["number"] = "318" }));

        Assert.Equal("GOVERNMENT", data.GetProperty("submittedBy").GetString());
        var timeline = data.GetProperty("timeline").EnumerateArray().ToList();
        Assert.StartsWith("Projekt wp", timeline[0].GetProperty("stage").GetString(), StringComparison.Ordinal);
        var third = timeline.Single(t => t.GetProperty("stage").GetString()!.StartsWith("III czytanie", StringComparison.Ordinal));
        Assert.Equal("uchwalono", third.GetProperty("decision").GetString());
        Assert.Contains(timeline, t => t.TryGetProperty("voting", out var v) && v.ValueKind == JsonValueKind.Object && v.GetProperty("yes").GetInt32() == 408);
        Assert.Contains("318_u3.pdf", data.GetProperty("textAfterThirdReading").GetString(), StringComparison.Ordinal);
        Assert.Contains("318", data.GetProperty("prints").EnumerateArray().Select(p => p.GetString()));
        Assert.Contains("401", data.GetProperty("prints").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task GetProcess_NeverReturnsTheRapporteur()
    {
        var result = await CallAsync(Router(), "get_sejm_process", new() { ["term"] = 10, ["number"] = "318" });

        Assert.DoesNotContain("KOWALSKI", Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rapporteur", Text(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeline_FlattensNestedStagesInOrder_CapsAt30()
    {
        var data = Data(await CallAsync(Router(), "get_sejm_process", new() { ["term"] = 10, ["number"] = "900" }));

        var timeline = data.GetProperty("timeline").EnumerateArray().ToList();
        Assert.Equal(30, timeline.Count);
        Assert.True(data.GetProperty("timelineTruncated").GetBoolean());
        // tree order: Start, then each CommitteeWork followed by its Referral and its CommitteeReport
        Assert.StartsWith("Projekt wp", timeline[0].GetProperty("stage").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("Praca w komisjach 1", timeline[1].GetProperty("stage").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("Skierowanie 1", timeline[2].GetProperty("stage").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("Sprawozdanie komisji 1", timeline[3].GetProperty("stage").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("Praca w komisjach 2", timeline[4].GetProperty("stage").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timeline_SejmCommitteeCodeIsShownAsIs_AndNeverPointsAtTheBodyTool()
    {
        var result = await CallAsync(Router(), "get_sejm_process", new() { ["term"] = 10, ["number"] = "900" });

        var data = Data(result);
        var referral = data.GetProperty("timeline").EnumerateArray().First(t => t.GetProperty("stage").GetString()!.StartsWith("Skierowanie 1", StringComparison.Ordinal));
        Assert.Equal("Sejm", referral.GetProperty("committee").GetString());
        Assert.DoesNotContain("get_sejm_body", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProcess_NotPublishedAsAnAct_HasNoActAndSaysSo()
    {
        var handler = Router(r => r.RequestUri!.AbsolutePath == "/sejm/term10/processes/5"
            ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"number":"5","title":"Wniosek","documentType":"wniosek","passed":true,"changeDate":"2023-11-14T11:44:49","term":10,"stages":[{"stageType":"End","stageName":"Uchwalono"}]}""")
            : null);

        var data = Data(await CallAsync(handler, "get_sejm_process", new() { ["term"] = 10, ["number"] = "5" }));

        Assert.Equal(JsonValueKind.Null, data.GetProperty("act").ValueKind);
        Assert.Equal("passed; not published as an act (motion, appointment or similar)", data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetProcess_UnknownNumber_IsToolError()
    {
        var result = await CallAsync(Router(), "get_sejm_process", new() { ["term"] = 10, ["number"] = "99999" });

        Assert.True(result.IsError);
        Assert.Contains("99999", Text(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1344/../x")]
    public async Task GetProcess_MissingOrBadNumber_IsToolError_WithoutARequest(string? number)
    {
        var handler = Router();

        var result = await CallAsync(handler, "get_sejm_process", new() { ["term"] = 10, ["number"] = number });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task IncludeAllVotings_DedupesDropsPrint3180_AndStaysWithinSixSearches()
    {
        const string votingJson = """
            {{"term":10,"sitting":{0},"sittingDay":2,"votingNumber":{1},"date":"2024-05-23T14:00:54","title":"{2}","topic":"{3}","description":"{4}","kind":"ELECTRONIC","majorityType":"SIMPLE_MAJORITY","majorityVotes":22,"yes":408,"no":21,"abstain":0,"notParticipating":30,"present":0,"totalVoted":429}}
            """;
        var final = string.Format(CultureInfo.InvariantCulture, votingJson, 12, 28, "Pkt. 6 Sprawozdanie Komisji o projekcie ustawy (druki nr 318 i 401)", "g\\u0142osowanie nad ca\\u0142o\\u015bci\\u0105 projektu.", "ca\\u0142o\\u015b\\u0107 projektu ustawy");
        var other = string.Format(CultureInfo.InvariantCulture, votingJson, 12, 29, "Pkt. 7 Inny projekt (druk nr 3180)", "g\\u0142osowanie", "wniosek");
        var amendment = string.Format(CultureInfo.InvariantCulture, votingJson, 12, 27, "Pkt. 6 Sprawozdanie Komisji o projekcie ustawy (druki nr 318 i 401)", "poprawka 1", "poprawka");
        var handler = Router(r =>
        {
            if (r.RequestUri!.AbsolutePath != "/sejm/term10/votings/search")
            {
                return null;
            }

            // every search returns the same three hits: the amendment and the final vote name 318, the other vote names 3180
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "[" + amendment + "," + final + "," + other + "]");
        });

        var data = Data(await CallAsync(handler, "get_sejm_process", new() { ["term"] = 10, ["number"] = "318", ["includeAllVotings"] = true }));

        var searchPaths = SejmPaths(handler).Where(p => p.Contains("/votings/search", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, searchPaths.Count);   // one search per print (318 and 401), not per sitting
        Assert.All(searchPaths, p => Assert.DoesNotContain("proceeding=", p, StringComparison.Ordinal));
        Assert.False(data.GetProperty("allVotingsTruncated").GetBoolean());
        var lines = data.GetProperty("allVotings").EnumerateArray().ToList();
        Assert.Equal(new[] { 27, 28 }, lines.Select(l => l.GetProperty("number").GetInt32()).OrderBy(n => n).ToArray());   // deduped, 3180 dropped
        var finalLine = lines.Single(l => l.GetProperty("number").GetInt32() == 28);
        Assert.True(finalLine.GetProperty("isFinalPassage").GetBoolean());
        Assert.False(lines.Single(l => l.GetProperty("number").GetInt32() == 27).GetProperty("isFinalPassage").GetBoolean());
    }

    [Fact]
    public async Task IncludeAllVotings_MorePrintsThanTheCap_SearchesSixAndSaysTheListIsPartial()
    {
        var handler = Router(r => r.RequestUri!.AbsolutePath == "/sejm/term10/votings/search" ? StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]") : null);

        var result = await CallAsync(handler, "get_sejm_process", new() { ["term"] = 10, ["number"] = "900", ["includeAllVotings"] = true });

        var data = Data(result);
        Assert.Equal(6, SejmPaths(handler).Count(p => p.Contains("/votings/search", StringComparison.Ordinal)));   // process 900 has 12 prints
        Assert.True(data.GetProperty("allVotingsTruncated").GetBoolean());
        Assert.Contains("partial", data.GetProperty("note").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncludeAllVotings_AFullSearchPage_SaysTheListIsPartial()
    {
        // the API has no total: a page that came back full (the client asks for limit+1) means there may be more
        var many = "[" + string.Join(",", Enumerable.Range(1, 101).Select(i => $$"""{"term":10,"sitting":1,"sittingDay":1,"votingNumber":{{i}},"date":"2024-05-23T14:00:54","title":"Pkt. {{i}} (druk nr 318)","kind":"ELECTRONIC","majorityType":"SIMPLE_MAJORITY","majorityVotes":22,"yes":1,"no":0,"abstain":0,"notParticipating":0,"present":0,"totalVoted":1}""")) + "]";
        var handler = Router(r => r.RequestUri!.AbsolutePath == "/sejm/term10/votings/search" ? StubHttpMessageHandler.Json(HttpStatusCode.OK, many) : null);

        var data = Data(await CallAsync(handler, "get_sejm_process", new() { ["term"] = 10, ["number"] = "318", ["includeAllVotings"] = true }));

        Assert.True(data.GetProperty("allVotingsTruncated").GetBoolean());
    }

    // ---- a process that did not pass but has a linked act (a rejected no-confidence motion is linked to a Monitor Polski entry)

    private static HttpResponseMessage? NotPassedWithEli(HttpRequestMessage r) => r.RequestUri!.AbsolutePath switch
    {
        "/sejm/term10/processes" => WithTotal(StubHttpMessageHandler.Json(HttpStatusCode.OK, """[{"number":"921","title":"Wniosek o wotum nieufno\u015bci","documentType":"wniosek","passed":false,"ELI":"MP/2025/55","displayAddress":"M.P. 2025 poz. 55","changeDate":"2025-02-01T10:00:00","processStartDate":"2025-01-10","closureDate":"2025-01-30","term":10}]"""), 1),
        "/sejm/term10/processes/921" => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"number":"921","title":"Wniosek o wotum nieufno\u015bci","documentType":"wniosek","passed":false,"ELI":"MP/2025/55","displayAddress":"M.P. 2025 poz. 55","changeDate":"2025-02-01T10:00:00","processStartDate":"2025-01-10","closureDate":"2025-01-30","term":10,"stages":[{"stageType":"End","stageName":"Odrzucono"}]}"""),
        _ => null,
    };

    [Fact]
    public async Task Search_NotPassedButLinkedToAnAct_IsNeverReportedAsPublished()
    {
        var data = Data(await CallAsync(Router(NotPassedWithEli), "search_sejm_processes", new() { ["term"] = 10 }));

        var status = Assert.Single(data.GetProperty("items").EnumerateArray()).GetProperty("status").GetString();
        Assert.StartsWith("not passed; closed on 2025-01-30", status, StringComparison.Ordinal);
        Assert.Contains("linked act M.P. 2025 poz. 55", status, StringComparison.Ordinal);
        Assert.DoesNotContain("published as", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProcess_NotPassedButLinkedToAnAct_SaysTheActIsOnlyLinked()
    {
        var data = Data(await CallAsync(Router(NotPassedWithEli), "get_sejm_process", new() { ["term"] = 10, ["number"] = "921" }));

        Assert.StartsWith("not passed; closed on 2025-01-30", data.GetProperty("status").GetString(), StringComparison.Ordinal);
        var act = data.GetProperty("act");
        Assert.Equal("MP/2025/55", act.GetProperty("eli").GetString());
        Assert.Contains("did not pass", act.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("całość projektu ustawy", null, true)]
    [InlineData("całość projaktu ustawy", null, true)]          // a typo seen in the live data
    [InlineData("całość projektu uchwały", null, true)]
    [InlineData(null, "głosowanie nad całością projektu.", true)]
    [InlineData("wniosek o odrzucenie w całości projektu.", null, false)]   // a motion to reject the bill, not its passage
    [InlineData("poprawka 1", "głosowanie nad poprawką", false)]
    [InlineData(null, null, false)]
    public void IsFinalPassage_PinsTheKnownWordings(string? description, string? topic, bool expected)
    {
        Assert.Equal(expected, SejmTools.IsFinalPassage(description, topic));
    }

    // ---- search_sejm_votings

    private static HttpResponseMessage? VotingRoute(HttpRequestMessage r) => r.RequestUri!.AbsolutePath switch
    {
        "/sejm/term10/votings" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/votings-index.json"),
        "/sejm/term10/votings/search" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/votings-search-67.json"),
        "/sejm/term10/votings/67/1" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/voting-67-1.json"),
        "/sejm/term10/votings/67/2" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/voting-67-2-quorum.json"),
        "/sejm/term10/votings/1/13" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/synthetic-voting-on-list.json"),
        _ => null,
    };

    [Fact]
    public async Task SearchVotings_NoFilter_UsesTheLatestSittingFromTheIndex()
    {
        var handler = Router(VotingRoute);
        var latest = JsonDocument.Parse(Fixture.Read("sejm/votings-index.json")).RootElement.EnumerateArray().Max(d => d.GetProperty("proceeding").GetInt32());

        var data = Data(await CallAsync(handler, "search_sejm_votings", new() { ["term"] = 10 }));

        var paths = SejmPaths(handler);
        Assert.Equal(2, paths.Count);
        Assert.Equal("/sejm/term10/votings", paths[0]);
        Assert.StartsWith("/sejm/term10/votings/search?proceeding=" + latest.ToString(CultureInfo.InvariantCulture), paths[1], StringComparison.Ordinal);
        Assert.Contains("latest sitting", data.GetProperty("query").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchVotings_HasMore_AndTheHintExplainsTheApi()
    {
        var handler = Router(VotingRoute);

        var data = Data(await CallAsync(handler, "search_sejm_votings", new() { ["term"] = 10, ["sitting"] = 67, ["limit"] = 2 }));

        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
        Assert.True(data.GetProperty("hasMore").GetBoolean());
        var first = data.GetProperty("items")[0];
        Assert.Equal(67, first.GetProperty("sitting").GetInt32());
        Assert.Equal(1, first.GetProperty("number").GetInt32());
        Assert.Contains("get_sejm_voting", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Contains("oldest first", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("/sejm/term10/votings/search?proceeding=67&limit=3", SejmPaths(handler).Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchVotings_BadDate_IsToolError_WithoutARequest()
    {
        var handler = Router(VotingRoute);

        var result = await CallAsync(handler, "search_sejm_votings", new() { ["term"] = 10, ["dateFrom"] = "14.11.2026" });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task SearchVotings_LimitOutOfRange_IsToolError(int limit)
    {
        var handler = Router(VotingRoute);

        var result = await CallAsync(handler, "search_sejm_votings", new() { ["term"] = 10, ["sitting"] = 67, ["limit"] = limit });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    // ---- get_sejm_voting

    [Fact]
    public async Task GetVoting_Default_NoNamesNoNote_AndOutcomeIsInferred()
    {
        var handler = Router(VotingRoute);

        var result = await CallAsync(handler, "get_sejm_voting", new() { ["term"] = 10, ["sitting"] = 67, ["number"] = 1 });

        var data = Data(result);
        Assert.DoesNotContain("KOWALSKI", Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("mpVotes").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("personalDataNote").ValueKind);
        Assert.Equal("passed (inferred: yes >= required)", data.GetProperty("outcome").GetString());
        Assert.Equal(10, data.GetProperty("required").GetInt32());
        Assert.True(data.GetProperty("sumsMatchTotals").GetBoolean());
        var clubs = data.GetProperty("clubs").EnumerateArray().ToList();
        Assert.Equal("KO", clubs[0].GetProperty("club").GetString());
        Assert.Equal(156, clubs[0].GetProperty("seats").GetInt32());
        Assert.Single(SejmPaths(handler));
    }

    [Fact]
    public async Task GetVoting_FailedWhenYesIsBelowRequired_AndMismatchedSumsAreFlagged()
    {
        var handler = Router(r => r.RequestUri!.AbsolutePath == "/sejm/term10/votings/5/1"
            ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"term":10,"sitting":5,"sittingDay":1,"votingNumber":1,"date":"2024-01-02T10:00:00","title":"Pkt. 1 (druk nr 77)","kind":"ELECTRONIC","majorityType":"SIMPLE_MAJORITY","majorityVotes":200,"yes":150,"no":210,"abstain":0,"notParticipating":100,"present":0,"totalVoted":360,"votes":[{"MP":1,"club":"A","firstName":"X","lastName":"Y","vote":"YES"}]}""")
            : null);

        var data = Data(await CallAsync(handler, "get_sejm_voting", new() { ["term"] = 10, ["sitting"] = 5, ["number"] = 1 }));

        Assert.Equal("failed (inferred: yes < required)", data.GetProperty("outcome").GetString());
        Assert.False(data.GetProperty("sumsMatchTotals").GetBoolean());   // one vote against totals of 150 yes: the API changed shape
    }

    [Fact]
    public async Task GetVoting_IncludeMpVotes_NamesWithoutIds_WithTheNote()
    {
        var result = await CallAsync(Router(VotingRoute), "get_sejm_voting", new() { ["term"] = 10, ["sitting"] = 67, ["number"] = 1, ["includeMpVotes"] = true });

        var data = Data(result);
        var votes = data.GetProperty("mpVotes").EnumerateArray().ToList();
        Assert.Equal(460, votes.Count);
        Assert.Equal("JAN KOWALSKI", votes[0].GetProperty("name").GetString());
        Assert.False(votes[0].TryGetProperty("mpId", out _));
        Assert.False(votes[0].TryGetProperty("id", out _));
        Assert.Equal(PersonalNote, data.GetProperty("personalDataNote").GetString());
    }

    private const string PersonalNote = "Individual MPs' votes are personal data: use them only for this voting question; do not compile voting records of a person.";

    [Fact]
    public async Task GetVoting_Quorum_OutcomeNull()
    {
        var data = Data(await CallAsync(Router(VotingRoute), "get_sejm_voting", new() { ["term"] = 10, ["sitting"] = 67, ["number"] = 2 }));

        Assert.Equal(JsonValueKind.Null, data.GetProperty("outcome").ValueKind);
        Assert.Equal(407, data.GetProperty("present").GetInt32());
        Assert.True(data.GetProperty("sumsMatchTotals").GetBoolean());
    }

    [Fact]
    public async Task GetVoting_OnList_PerOptionOutcome_AndNoDescriptionOrNames()
    {
        var result = await CallAsync(Router(VotingRoute), "get_sejm_voting", new() { ["term"] = 10, ["sitting"] = 1, ["number"] = 13 });

        var data = Data(result);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("outcome").ValueKind);
        var options = data.GetProperty("options").EnumerateArray().ToList();
        Assert.Equal(8, options.Count);
        Assert.Equal("KANDYDAT A", options[0].GetProperty("option").GetString());
        Assert.Equal("reached the required votes (inferred)", options[1].GetProperty("outcome").GetString());   // 246 >= 229
        Assert.Equal("did not reach the required votes (inferred)", options[0].GetProperty("outcome").GetString());   // 193 < 229
        Assert.DoesNotContain("KOWALSKI", Text(result), StringComparison.OrdinalIgnoreCase);   // the option description names a person
        Assert.True(data.GetProperty("sumsMatchTotals").GetBoolean());
    }

    [Fact]
    public async Task GetVoting_PrintsFromTheTitle_AndTheHint()
    {
        var data = Data(await CallAsync(Router(VotingRoute), "get_sejm_voting", new() { ["term"] = 10, ["sitting"] = 67, ["number"] = 1 }));

        Assert.Equal(new[] { "3221" }, data.GetProperty("prints").EnumerateArray().Select(p => p.GetString()).ToArray());
        Assert.Contains("get_sejm_process", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(67, null)]
    [InlineData(0, 1)]
    public async Task GetVoting_MissingOrBadNumbers_IsToolError_WithoutARequest(int? sitting, int? number)
    {
        var handler = Router(VotingRoute);

        var result = await CallAsync(handler, "get_sejm_voting", new() { ["term"] = 10, ["sitting"] = sitting, ["number"] = number });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GetVoting_Unknown_IsToolError()
    {
        var result = await CallAsync(Router(VotingRoute), "get_sejm_voting", new() { ["term"] = 10, ["sitting"] = 67, ["number"] = 9999 });

        Assert.True(result.IsError);
        Assert.Contains("67/9999", Text(result), StringComparison.Ordinal);
    }

    // ---- get_sejm_body

    private static HttpResponseMessage? BodyRoute(HttpRequestMessage r) => r.RequestUri!.AbsolutePath switch
    {
        "/sejm/term10/clubs" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/clubs.json"),
        "/sejm/term10/clubs/Centrum" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/club-centrum.json"),
        "/sejm/term10/committees" => StubHttpMessageHandler.Json(HttpStatusCode.OK, "[" + Fixture.Read("sejm/committee-asw.json") + "]"),
        "/sejm/term10/committees/ASW" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/committee-asw.json"),
        "/sejm/term10/committees/XX" => StubHttpMessageHandler.Empty(HttpStatusCode.NoContent),
        _ => null,
    };

    [Fact]
    public async Task GetBody_Clubs_DefaultNoNames()
    {
        var result = await CallAsync(Router(BodyRoute), "get_sejm_body", new() { ["term"] = 10, ["kind"] = "club" });

        var data = Data(result);
        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(12, items.Count);
        Assert.Contains(items, i => i.GetProperty("code").GetString() == "Centrum" && i.GetProperty("members").GetInt32() == 15);
        Assert.DoesNotContain("Kowalski", Text(result), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("detail").ValueKind);
    }

    [Fact]
    public async Task GetBody_Club_WithMembers_HasTheNote()
    {
        var data = Data(await CallAsync(Router(BodyRoute), "get_sejm_body", new() { ["term"] = 10, ["kind"] = "club", ["code"] = "Centrum", ["includeMembers"] = true }));

        var detail = data.GetProperty("detail");
        Assert.Equal(3, detail.GetProperty("members").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(detail.GetProperty("membersNote").GetString()));
        Assert.Equal(15, detail.GetProperty("membersCount").GetInt32());
    }

    [Fact]
    public async Task GetBody_Committee_CompositionByClub_NoNamesByDefault()
    {
        var result = await CallAsync(Router(BodyRoute), "get_sejm_body", new() { ["term"] = 10, ["kind"] = "committee", ["code"] = "ASW" });

        var detail = Data(result).GetProperty("detail");
        Assert.Equal("ASW", detail.GetProperty("code").GetString());
        Assert.Equal(40, detail.GetProperty("composition").EnumerateArray().Sum(c => c.GetProperty("seats").GetInt32()));
        Assert.Equal(8, detail.GetProperty("subCommittees").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("members").ValueKind);
        Assert.DoesNotContain("Kowalski", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBody_Committee_MembersAreCappedAt80()
    {
        var members = string.Join(",", Enumerable.Range(1, 100).Select(i => $$"""{"id":{{i}},"firstName":"Jan","lastName":"Kowalski{{i}}","club":"A"}"""));
        var handler = Router(r => r.RequestUri!.AbsolutePath == "/sejm/term10/committees/BIG"
            ? StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"code":"BIG","name":"Duza komisja","type":"STANDING","members":[{{members}}]}""")
            : null);

        var detail = Data(await CallAsync(handler, "get_sejm_body", new() { ["term"] = 10, ["kind"] = "committee", ["code"] = "BIG", ["includeMembers"] = true })).GetProperty("detail");

        Assert.Equal(80, detail.GetProperty("members").GetArrayLength());
        Assert.True(detail.GetProperty("membersTruncated").GetBoolean());
        Assert.Equal(100, detail.GetProperty("membersCount").GetInt32());
    }

    [Fact]
    public async Task GetBody_UnknownClub_NamesTheResolvedTerm_AndDissolvedClubs()
    {
        var result = await CallAsync(Router(BodyRoute), "get_sejm_body", new() { ["term"] = 10, ["kind"] = "club", ["code"] = "Polska2050-TD" });

        Assert.True(result.IsError);
        Assert.Contains("No club 'Polska2050-TD' in term 10", Text(result), StringComparison.Ordinal);
        Assert.Contains("dissolved", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBody_UnknownCommittee_IsToolError()
    {
        var result = await CallAsync(Router(BodyRoute), "get_sejm_body", new() { ["term"] = 10, ["kind"] = "committee", ["code"] = "XX" });

        Assert.True(result.IsError);
        Assert.Contains("No committee 'XX'", Text(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("party")]
    public async Task GetBody_BadKind_IsToolError_WithoutARequest(string? kind)
    {
        var handler = Router(BodyRoute);

        var result = await CallAsync(handler, "get_sejm_body", new() { ["term"] = 10, ["kind"] = kind });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GetBody_BadCode_IsToolError_WithoutARequest()
    {
        var handler = Router(BodyRoute);

        var result = await CallAsync(handler, "get_sejm_body", new() { ["term"] = 10, ["kind"] = "committee", ["code"] = "asw" });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    // ---- all tools

    [Fact]
    public async Task AllAnswersCarryTheSource()
    {
        foreach (var (tool, args) in new (string, Dictionary<string, object?>)[]
        {
            ("search_sejm_processes", new() { ["term"] = 10 }),
            ("get_sejm_process", new() { ["term"] = 10, ["number"] = "318" }),
            ("search_sejm_votings", new() { ["term"] = 10, ["sitting"] = 67 }),
            ("get_sejm_voting", new() { ["term"] = 10, ["sitting"] = 67, ["number"] = 1 }),
            ("get_sejm_body", new() { ["term"] = 10, ["kind"] = "club" }),
        })
        {
            var data = Data(await CallAsync(Router(r => VotingRoute(r) ?? BodyRoute(r)), tool, args));
            Assert.Equal(Source, data.GetProperty("source").GetString());
        }
    }

    [Fact]
    public async Task Quota_IsAToolErrorThatNamesTheSejmApi()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = StubHttpMessageHandler.Json((HttpStatusCode)429, "{}");
            response.Headers.TryAddWithoutValidation("Retry-After", "30");
            return response;
        });

        var result = await CallAsync(handler, "search_sejm_processes", new() { ["term"] = 10 });

        Assert.True(result.IsError);
        Assert.Contains("Sejm API is rate-limiting", Text(result), StringComparison.Ordinal);
        Assert.Contains("30", Text(result), StringComparison.Ordinal);
    }
}
