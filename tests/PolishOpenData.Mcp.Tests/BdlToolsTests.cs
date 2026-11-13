using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Mcp.Tests;

/// <summary>The BDL tools through a real MCP session; GUS answers from recorded fixtures. They exist without any key.</summary>
public sealed class BdlToolsTests : IAsyncDisposable
{
    private McpTestSession? _session;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    private async Task<McpTestSession> StartAsync(StubHttpMessageHandler handler, string clientId = "")
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _session = await McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler),
            null,
            TestContext.Current.CancellationToken,
            bdlClientId: clientId);
        return _session;
    }

    private async Task<CallToolResult> CallAsync(StubHttpMessageHandler handler, string tool, Dictionary<string, object?> args, string clientId = "")
    {
        var session = await StartAsync(handler, clientId);
        return await session.Client.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static JsonElement Data(CallToolResult result)
    {
        Assert.True(result.IsError != true, Text(result));
        return result.StructuredContent!.Value;
    }

    private static StubHttpMessageHandler Fixture(string fixture, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => StubHttpMessageHandler.FromFixture(status, fixture));

    private const string Attribution = "Source: Główny Urząd Statystyczny (GUS), Bank Danych Lokalnych, CC BY 4.0";

    [Fact]
    public async Task Tools_AreListedWithoutAnyKey()
    {
        var session = await StartAsync(Fixture("bdl/units-search-krakow.json"));
        var tools = await session.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var names = tools.Select(t => t.Name).ToHashSet();
        Assert.Contains("find_statistical_unit", names);
        Assert.Contains("search_statistics", names);
        Assert.Contains("get_statistics", names);
        Assert.Contains("find_statistical_unit", session.Client.ServerInstructions ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindStatisticalUnit_ReturnsLevelsKindsAndTeryt()
    {
        var handler = Fixture("bdl/units-search-krakow.json");

        var data = Data(await CallAsync(handler, "find_statistical_unit", new() { ["name"] = "Kraków" }));

        var units = data.GetProperty("units").EnumerateArray().ToList();
        Assert.Equal(3, units.Count);
        var gmina = units.Single(u => u.GetProperty("level").GetString() == "gmina");
        Assert.Equal("011212161011", gmina.GetProperty("id").GetString());
        Assert.Equal("1261011", gmina.GetProperty("teryt").GetString());
        Assert.Equal(Attribution, data.GetProperty("source").GetString());
        Assert.Single(handler.RequestUris);
    }

    [Fact]
    public async Task FindStatisticalUnit_ShortName_IsToolError()
    {
        var handler = Fixture("bdl/units-search-krakow.json");

        var result = await CallAsync(handler, "find_statistical_unit", new() { ["name"] = "K" });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    private static string VariablesJson() =>
        "{\"totalRecords\":57,\"results\":[" + string.Join(",", Enumerable.Range(1, 7).Select(i =>
            "{\"id\":" + (1000 + i) + ",\"subjectId\":\"P" + i + "\",\"n1\":\"ogółem\",\"n2\":\"mężczyźni\",\"level\":6,\"measureUnitId\":26,\"measureUnitName\":\"osoba\"}")) + "]}";

    private static HttpResponseMessage SubjectOrVariables(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        if (path.EndsWith("/variables/search", StringComparison.Ordinal))
        {
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, VariablesJson());
        }

        var id = path[(path.LastIndexOf('/') + 1)..];
        return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"id\":\"" + id + "\",\"parentId\":\"G1\",\"name\":\"Temat " + id + "\",\"dimensions\":[\"Płeć\"]}");
    }

    [Fact]
    public async Task SearchStatistics_ResolvesAtMostFiveSubjects()
    {
        var handler = new StubHttpMessageHandler(SubjectOrVariables);

        var data = Data(await CallAsync(handler, "search_statistics", new() { ["query"] = "ludność" }));

        Assert.Equal(6, handler.RequestUris.Count);   // 1 search + 5 subjects
        Assert.Equal(57, data.GetProperty("totalMatches").GetInt32());
        var variables = data.GetProperty("variables").EnumerateArray().ToList();
        Assert.Equal(7, variables.Count);
        Assert.StartsWith("Temat P1: ogółem, mężczyźni", variables[0].GetProperty("label").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("P6: ogółem, mężczyźni", variables[5].GetProperty("label").GetString(), StringComparison.Ordinal);
        Assert.Equal("osoba", variables[0].GetProperty("measureUnit").GetString());
        Assert.Equal(Attribution, data.GetProperty("source").GetString());
    }

    [Fact]
    public async Task GetStatistics_NoValueIsNullWithFlag_AndAttributionIsIncluded()
    {
        var handler = Fixture("bdl/data-by-unit-novalue.json");

        var data = Data(await CallAsync(handler, "get_statistics", new() { ["unitId"] = "011212161011", ["variableIds"] = new[] { 111, 222 } }));

        var first = data.GetProperty("series").EnumerateArray().Single(s => s.GetProperty("variableId").GetInt32() == 111).GetProperty("values").EnumerateArray().ToList();
        Assert.Equal(JsonValueKind.Null, first[0].GetProperty("value").ValueKind);
        Assert.Equal("x", first[0].GetProperty("flag").GetString());
        Assert.Equal(JsonValueKind.Null, first[1].GetProperty("value").ValueKind);
        Assert.Equal("n", first[1].GetProperty("flag").GetString());
        var real = data.GetProperty("series").EnumerateArray().Single(s => s.GetProperty("variableId").GetInt32() == 222).GetProperty("values")[0];
        Assert.Equal(0, real.GetProperty("value").GetDouble());
        Assert.Equal("a", real.GetProperty("flag").GetString());
        Assert.Equal(Attribution, data.GetProperty("source").GetString());
    }

    [Fact]
    public async Task GetStatistics_UnknownAttribute_IsNotPresentedAsACleanNumber()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            "{\"unitId\":\"1\",\"unitName\":\"x\",\"results\":[{\"id\":5,\"measureUnitId\":1,\"values\":[{\"year\":\"2025\",\"val\":0,\"attrId\":99}]}]}"));

        var data = Data(await CallAsync(handler, "get_statistics", new() { ["unitId"] = "011212161011", ["variableIds"] = new[] { 5 } }));

        var value = data.GetProperty("series")[0].GetProperty("values")[0];
        Assert.Contains("unknown", value.GetProperty("flag").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchStatistics_AFailedSubjectLookup_FallsBackToTheSubjectId()
    {
        var handler = new StubHttpMessageHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/variables/search", StringComparison.Ordinal)
            ? StubHttpMessageHandler.Json(HttpStatusCode.OK, VariablesJson())
            : StubHttpMessageHandler.Json((HttpStatusCode)429, "{}"));

        var data = Data(await CallAsync(handler, "search_statistics", new() { ["query"] = "ludność" }));

        Assert.StartsWith("P1: ogółem", data.GetProperty("variables")[0].GetProperty("label").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindStatisticalUnit_ReportsTheTotalWhenResultsAreCut()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            "{\"totalRecords\":250,\"results\":[" + string.Join(",", Enumerable.Range(1, 30).Select(i => "{\"id\":\"" + (100000000000L + i).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\",\"name\":\"Nowa " + i + "\",\"level\":6}")) + "]}"));

        var data = Data(await CallAsync(handler, "find_statistical_unit", new() { ["name"] = "Nowa" }));

        Assert.Equal(250, data.GetProperty("totalMatches").GetInt32());
        Assert.Equal(20, data.GetProperty("units").GetArrayLength());
    }

    [Fact]
    public async Task FindStatisticalUnit_LocalityLevel_IsToolError()
    {
        var handler = Fixture("bdl/units-search-krakow.json");

        var result = await CallAsync(handler, "find_statistical_unit", new() { ["name"] = "Kraków", ["level"] = 7 });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GetStatistics_UnknownUnit_SaysNotFound()
    {
        var data = Data(await CallAsync(Fixture("bdl/empty-unit.json"), "get_statistics", new() { ["unitId"] = "999999999999", ["variableIds"] = new[] { 72305 } }));

        Assert.False(data.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task GetStatistics_OverTwentyIds_IsToolError()
    {
        var handler = Fixture("bdl/data-by-unit-krakow.json");

        var result = await CallAsync(handler, "get_statistics", new() { ["unitId"] = "011212161011", ["variableIds"] = Enumerable.Range(1, 21).ToArray() });

        Assert.True(result.IsError);
        Assert.Contains("20", Text(result), StringComparison.Ordinal);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task Quota_MapsToHintAboutTheClientIdVariable()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json((HttpStatusCode)429, "{\"result\":\"limit\"}"));

        var result = await CallAsync(handler, "get_statistics", new() { ["unitId"] = "011212161011", ["variableIds"] = new[] { 72305 } });

        Assert.True(result.IsError);
        Assert.Contains("POLISHOPENDATA_BDL_CLIENT_ID", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientId_IsSentAsHeader_WhenSupplied()
    {
        var handler = Fixture("bdl/units-search-krakow.json");

        _ = Data(await CallAsync(handler, "find_statistical_unit", new() { ["name"] = "Kraków" }, clientId: "my-key"));

        Assert.Equal("my-key", handler.Requests.Single().ClientId);
    }

    [Fact]
    public async Task NoClientId_SendsNoHeader()
    {
        var handler = Fixture("bdl/units-search-krakow.json");

        _ = Data(await CallAsync(handler, "find_statistical_unit", new() { ["name"] = "Kraków" }));

        Assert.Null(handler.Requests.Single().ClientId);
    }

    [Fact]
    public async Task Quota_NamesTheResetTime_WhenGusSendsIt()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = StubHttpMessageHandler.Json((HttpStatusCode)429, "{}");
            response.Headers.TryAddWithoutValidation("X-Rate-Limit-Reset", "2026-11-20T08:57:02Z");
            return response;
        });

        var result = await CallAsync(handler, "get_statistics", new() { ["unitId"] = "011212161011", ["variableIds"] = new[] { 72305 } });

        Assert.True(result.IsError);
        Assert.Contains("2026-11-20 08:57 UTC", Text(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("find_statistical_unit")]
    [InlineData("search_statistics")]
    public async Task Quota_OnFindAndSearch_AlsoPointsAtTheClientIdVariable(string tool)
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json((HttpStatusCode)429, "{}"));
        var args = tool == "find_statistical_unit" ? new Dictionary<string, object?> { ["name"] = "Krak\u00f3w" } : new Dictionary<string, object?> { ["query"] = "ludno\u015b\u0107" };

        var result = await CallAsync(handler, tool, args);

        Assert.True(result.IsError);
        Assert.Contains("POLISHOPENDATA_BDL_CLIENT_ID", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatistics_MissingUnitId_IsToolError()
    {
        var handler = Fixture("bdl/data-by-unit-krakow.json");

        var result = await CallAsync(handler, "get_statistics", new() { ["variableIds"] = new[] { 72305 } });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GetStatistics_NoVariableIds_IsToolError()
    {
        var handler = Fixture("bdl/data-by-unit-krakow.json");

        var result = await CallAsync(handler, "get_statistics", new() { ["unitId"] = "011212161011", ["variableIds"] = Array.Empty<int>() });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task FindStatisticalUnit_LevelOutOfRange_IsToolError()
    {
        var handler = Fixture("bdl/units-search-krakow.json");

        var result = await CallAsync(handler, "find_statistical_unit", new() { ["name"] = "Krak\u00f3w", ["level"] = 9 });

        Assert.True(result.IsError);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task InvalidClientIdVariable_FailsAtStartupAndNamesTheVariable()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => Fixture("bdl/units-search-krakow.json")),
            null,
            TestContext.Current.CancellationToken,
            bdlClientId: "bad key"));

        Assert.Contains("POLISHOPENDATA_BDL_CLIENT_ID", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bad key", ex.ToString(), StringComparison.Ordinal);
    }
}
