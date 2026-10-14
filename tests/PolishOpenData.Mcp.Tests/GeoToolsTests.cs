using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using PolishOpenData.Geo;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Mcp.Tests;

/// <summary>geocode_address and find_parcel through a real MCP session; ULDK and UUG answer from the recorded fixtures.</summary>
public sealed class GeoToolsTests : IAsyncDisposable
{
    private const string ParcelId = "146510_8.0507.41/1";
    private const string KwSentence = "Never returns or accepts land-registry (KW) numbers or owner data.";

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly StubHttpMessageHandler _stub = new(Respond);
    private McpTestSession? _session;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        var query = uri.Query;
        if (uri.Host == "uldk.gugik.gov.pl")
        {
            var fixture =
                query.Contains("srid=4326", StringComparison.Ordinal) ? "parcel-146510_8.0507.41_1-wgs84-geom.txt"
                : query.Contains("GetParcelByIdOrNr", StringComparison.Ordinal) ? "count-krzewina-134.txt"
                : query.Contains("GetParcelById&id=999999", StringComparison.Ordinal) ? "not-found.txt"
                : "xy-warsaw.txt";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture.Read("uldk/" + fixture), Encoding.UTF8, "text/plain") };
        }

        var json =
            query.Contains("Krucza%2050", StringComparison.Ordinal) ? "address-krucza-50.json"
            : query.Contains("Krucza", StringComparison.Ordinal) ? "street-krucza.json"
            : "none.json";
        return StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "uug/" + json);
    }

    private async Task<McpTestSession> SessionAsync() =>
        _session ??= await McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => _stub),
            new FakeTimeProvider(Now),
            TestContext.Current.CancellationToken);

    private async Task<CallToolResult> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        await (await SessionAsync()).Client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

    private Task<CallToolResult> FindAsync(params (string Name, object? Value)[] arguments) =>
        CallAsync("find_parcel", arguments.ToDictionary(a => a.Name, a => a.Value));

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static JsonElement Data(CallToolResult result)
    {
        Assert.True(result.IsError != true, Text(result));
        return result.StructuredContent!.Value;
    }

    [Fact]
    public async Task GeocodeAddress_returns_wgs84_and_kind()
    {
        var data = Data(await CallAsync("geocode_address", new() { ["address"] = "Warszawa, Krucza 50" }));

        var match = Assert.Single(data.GetProperty("matches").EnumerateArray());
        Assert.Equal("address", match.GetProperty("kind").GetString());
        Assert.Equal("Warszawa", match.GetProperty("city").GetString());
        Assert.Equal("ulica Krucza", match.GetProperty("street").GetString());
        Assert.Equal("50", match.GetProperty("number").GetString());
        Assert.Equal("00-025", match.GetProperty("postalCode").GetString());
        Assert.Equal("146501", match.GetProperty("teryt").GetString());
        var expected = new Pl1992Point(637659.46, 486969.16).ToWgs84();
        Assert.Equal(expected.Latitude, match.GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(expected.Longitude, match.GetProperty("longitude").GetDouble(), 6);
        Assert.Equal(0.76, match.GetProperty("accuracy").GetDouble());
        var source = data.GetProperty("source");
        Assert.Contains("UUG", source.GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal(Now, source.GetProperty("retrievedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task GeocodeAddress_street_only_is_kind_street()
    {
        var data = Data(await CallAsync("geocode_address", new() { ["address"] = "Warszawa, Krucza" }));

        Assert.Equal("street", Assert.Single(data.GetProperty("matches").EnumerateArray()).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task GeocodeAddress_blank_address_is_a_tool_error()
    {
        var result = await CallAsync("geocode_address", new() { ["address"] = "  " });

        Assert.True(result.IsError == true);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task FindParcel_by_id()
    {
        var data = Data(await FindAsync(("parcelId", ParcelId)));

        var parcel = Assert.Single(data.GetProperty("parcels").EnumerateArray());
        Assert.Equal(ParcelId, parcel.GetProperty("parcelId").GetString());
        Assert.Equal("41/1", parcel.GetProperty("number").GetString());
        Assert.Equal("5-05-07", parcel.GetProperty("region").GetString());
        Assert.Equal("Warszawa (miasto)", parcel.GetProperty("commune").GetString());
        Assert.Equal("powiat Warszawa", parcel.GetProperty("county").GetString());
        Assert.Equal("mazowieckie", parcel.GetProperty("voivodeship").GetString());
        var center = new Pl1992Point((637928.301690837 + 638215.32061222) / 2, (485970.884417901 + 486033.096243337) / 2).ToWgs84();
        Assert.Equal(center.Latitude, parcel.GetProperty("center").GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(center.Longitude, parcel.GetProperty("center").GetProperty("longitude").GetDouble(), 6);
        Assert.Equal(JsonValueKind.Null, parcel.GetProperty("geometryWkt").ValueKind);   // only with includeGeometry
        var source = Assert.Single(data.GetProperty("sources").EnumerateArray());
        Assert.Contains("ULDK", source.GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal(Now, source.GetProperty("retrievedAt").GetDateTimeOffset());
        Assert.Single(_stub.RequestUris);
    }

    [Fact]
    public async Task FindParcel_by_id_with_geometry_returns_wgs84_wkt()
    {
        var data = Data(await FindAsync(("parcelId", ParcelId), ("includeGeometry", true)));

        var wkt = Assert.Single(data.GetProperty("parcels").EnumerateArray()).GetProperty("geometryWkt").GetString()!;
        Assert.StartsWith("POLYGON((21.02", wkt, StringComparison.Ordinal);
        Assert.Contains(_stub.RequestUris, u => u.Query.Contains("srid=4326", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindParcel_by_coordinates_sends_wgs84()
    {
        var data = Data(await FindAsync(("latitude", 52.2229), ("longitude", 21.0231)));

        Assert.Equal(ParcelId, Assert.Single(data.GetProperty("parcels").EnumerateArray()).GetProperty("parcelId").GetString());
        Assert.Contains("xy=21.0231,52.2229,4326", Assert.Single(_stub.RequestUris).Query, StringComparison.Ordinal);   // longitude first on the wire
    }

    [Fact]
    public async Task FindParcel_by_address_geocodes_then_finds()
    {
        var data = Data(await FindAsync(("address", "Warszawa, Krucza 50")));

        Assert.Equal(ParcelId, Assert.Single(data.GetProperty("parcels").EnumerateArray()).GetProperty("parcelId").GetString());
        var matched = data.GetProperty("matchedAddress");
        Assert.Equal("address", matched.GetProperty("kind").GetString());
        Assert.Equal("50", matched.GetProperty("number").GetString());
        Assert.Equal(0.76, matched.GetProperty("accuracy").GetDouble());
        Assert.Equal("An address point marks a building entrance; the property may span more than this parcel.", data.GetProperty("note").GetString());
        Assert.Equal(2, data.GetProperty("sources").GetArrayLength());
        Assert.Equal(2, _stub.RequestUris.Count);
        Assert.Equal("services.gugik.gov.pl", _stub.RequestUris[0].Host);
        Assert.Equal("uldk.gugik.gov.pl", _stub.RequestUris[1].Host);
    }

    [Fact]
    public async Task FindParcel_by_street_only_returns_candidates()
    {
        var data = Data(await FindAsync(("address", "Warszawa, Krucza")));

        Assert.Equal(0, data.GetProperty("parcels").GetArrayLength());
        var candidate = Assert.Single(data.GetProperty("candidates").EnumerateArray());
        Assert.Equal("street", candidate.GetProperty("kind").GetString());
        Assert.Equal("The address did not resolve to a single address point; pick one of the candidates or give the full address with a house number.", data.GetProperty("note").GetString());
        Assert.Equal("services.gugik.gov.pl", Assert.Single(_stub.RequestUris).Host);   // no parcel was looked up
    }

    [Fact]
    public async Task FindParcel_by_region_and_number_lists_every_match_without_geometry()
    {
        var data = Data(await FindAsync(("regionAndNumber", "Krzewina 134"), ("includeGeometry", true)));

        var parcel = Assert.Single(data.GetProperty("parcels").EnumerateArray());
        Assert.Equal("022503_5.0003.134", parcel.GetProperty("parcelId").GetString());
        Assert.Equal(JsonValueKind.Null, parcel.GetProperty("geometryWkt").ValueKind);
        Assert.Contains("not available", data.GetProperty("note").GetString(), StringComparison.Ordinal);
        Assert.Single(_stub.RequestUris);
    }

    [Fact]
    public async Task FindParcel_unknown_parcel_is_an_empty_list()
    {
        var data = Data(await FindAsync(("parcelId", "999999_9.0001.1")));

        Assert.Equal(0, data.GetProperty("parcels").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(data.GetProperty("note").GetString()));
    }

    [Fact]
    public async Task FindParcel_requires_exactly_one_input()
    {
        const string expected = "Give exactly one of: parcelId, latitude+longitude, address, regionAndNumber.";
        var none = await FindAsync();
        var two = await FindAsync(("parcelId", ParcelId), ("address", "Warszawa, Krucza 50"));
        var coordinatesAndId = await FindAsync(("parcelId", ParcelId), ("latitude", 52.2), ("longitude", 21.0));

        foreach (var result in new[] { none, two, coordinatesAndId })
        {
            Assert.True(result.IsError == true);
            Assert.Equal(expected, Text(result));
        }

        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task FindParcel_one_coordinate_alone_is_a_tool_error()
    {
        var result = await FindAsync(("latitude", 52.2));

        Assert.True(result.IsError == true);
        Assert.Contains("latitude and longitude together", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindParcel_invalid_parcel_id_is_a_tool_error()
    {
        var result = await FindAsync(("parcelId", "not-a-parcel"));

        Assert.True(result.IsError == true);
        Assert.Contains("not-a-parcel", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task FindParcel_swapped_coordinates_is_a_tool_error()
    {
        var result = await FindAsync(("latitude", 21.0), ("longitude", 52.0));

        Assert.True(result.IsError == true);
        var text = Text(result);
        Assert.Contains("48.9", text, StringComparison.Ordinal);
        Assert.Contains("55.0", text, StringComparison.Ordinal);
        Assert.Contains("14.0", text, StringComparison.Ordinal);
        Assert.Contains("24.2", text, StringComparison.Ordinal);
        Assert.Contains("latitude comes first", text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Block_page_is_a_tool_error_that_says_blocked()
    {
        var blocked = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Html(HttpStatusCode.OK, Fixture.Read("uldk/block-page.html")));
        await using var session = await McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => blocked),
            new FakeTimeProvider(Now),
            TestContext.Current.CancellationToken);

        var result = await session.Client.CallToolAsync("find_parcel", new Dictionary<string, object?> { ["parcelId"] = ParcelId }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError == true);
        Assert.Contains("blocked", Text(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tool_texts_never_mention_kw_lookup()
    {
        var tools = await (await SessionAsync()).Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        foreach (var name in new[] { "find_parcel", "geocode_address" })
        {
            var tool = tools.Single(t => t.Name == name);
            var texts = new List<string> { tool.Description };
            texts.AddRange(tool.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Value.GetProperty("description").GetString()!));
            foreach (var text in texts)
            {
                // The abbreviation may appear only in the negative sentence.
                var rest = text.Replace(KwSentence, string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain("KW", rest, StringComparison.Ordinal);
                Assert.DoesNotContain("land-registry", rest, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("księg", rest, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Contains(KwSentence, tool.Description, StringComparison.Ordinal);
            Assert.Contains("WGS-84", tool.Description, StringComparison.Ordinal);
            Assert.Contains("EGiB", tool.Description, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Upstream_failure_is_retried_once()
    {
        // Real clock: the resilience handler's retry delay runs on the TimeProvider.
        var failing = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var session = await McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => failing),
            null,
            TestContext.Current.CancellationToken);

        var result = await session.Client.CallToolAsync("find_parcel", new Dictionary<string, object?> { ["parcelId"] = ParcelId }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError == true);
        Assert.Equal(2, failing.RequestUris.Count);   // the first attempt and exactly one retry
    }
}
