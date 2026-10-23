using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using PolishOpenData.Geo;
using PolishOpenData.Rcn;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Mcp.Tests;

/// <summary>property_prices through a real MCP session; UUG answers from the recorded fixtures, RCN from generated pages shaped like the recorded premises fixture.</summary>
public sealed class PropertyToolsTests : IAsyncDisposable
{
    private const string KwSentence = "Never returns or accepts land-registry (KW) numbers or owner data.";
    private const string UnresolvedNote = "The address did not resolve to a single address point; pick one of the candidates or give the full address with a house number.";

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly Pl1992Point Centre = new Wgs84Point(52.2229, 21.0231).ToPl1992();
    private static readonly Pl1992Point KruczaPoint = new(637659.46, 486969.16);

    private readonly StubHttpMessageHandler _stub;
    private McpTestSession? _session;
    private List<Row> _rows;

    public PropertyToolsTests()
    {
        _stub = new StubHttpMessageHandler(Respond);
        _rows = DefaultRows(Centre);
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    /// <summary>A premises row to generate: the position is PL-1992, <c>Date</c> is yyyy-MM-dd.</summary>
    private sealed record Row(string DocumentNumber, double Easting, double Northing, string Date, decimal Price, decimal Area, string Share = "1/1", string Function = "mieszkalna", string? Total = null);

    private static List<Row> DefaultRows(Pl1992Point c) =>
    [
        new("A/1", c.Easting + 100, c.Northing, "2026-05-28", 800000, 40),               // 20,000 per m²
        new("B/1", c.Easting, c.Northing + 200, "2026-05-10", 600000, 50),               // 12,000
        new("C/1", c.Easting - 150, c.Northing - 150, "2026-04-01", 900000, 45),         // 20,000
        new("D/1", c.Easting + 300, c.Northing - 100, "2026-02-02", 500000, 25),         // 20,000
        new("E/1", c.Easting - 100, c.Northing + 100, "2026-01-15", 700000, 35, "1/64"), // a share: left out of the sample
    ];

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        if (uri.Host == "services.gugik.gov.pl")
        {
            var json =
                uri.Query.Contains("Krucza%2050", StringComparison.Ordinal) ? "address-krucza-50.json"
                : uri.Query.Contains("Krucza", StringComparison.Ordinal) ? "street-krucza.json"
                : "none.json";
            return StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "uug/" + json);
        }

        Assert.Equal("mapy.geoportal.gov.pl", uri.Host);
        var query = HttpUtility.ParseQueryString(uri.Query);
        if (query["typeNames"] == "ms:dzialki")
        {
            return Gml(Fixture.Read("rcn/parcels-warsaw.xml"));
        }

        var count = int.Parse(query["count"]!, CultureInfo.InvariantCulture);
        var start = int.Parse(query["startIndex"]!, CultureInfo.InvariantCulture);
        return Gml(Page(_rows.Skip(start).Take(count)));
    }

    private static HttpResponseMessage Gml(string xml) =>
        new(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/gml+xml") };

    private static string Page(IEnumerable<Row> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version='1.0' encoding=\"UTF-8\" ?>\n<wfs:FeatureCollection xmlns:ms=\"http://mapserver.gis.umn.edu/mapserver\" xmlns:gml=\"http://www.opengis.net/gml/3.2\" xmlns:wfs=\"http://www.opengis.net/wfs/2.0\" numberMatched=\"unknown\">\n");
        var i = 0;
        foreach (var row in rows)
        {
            i++;
            var e = row.Easting.ToString("R", CultureInfo.InvariantCulture);
            var n = row.Northing.ToString("R", CultureInfo.InvariantCulture);
            var id = "lokale." + (1000 + i).ToString(CultureInfo.InvariantCulture);
            var guid = "00000000-0000-4000-8000-" + i.ToString("D12", CultureInfo.InvariantCulture);
            var price = row.Price.ToString(CultureInfo.InvariantCulture);
            var area = row.Area.ToString(CultureInfo.InvariantCulture);
            var total = row.Total ?? price;
            sb.Append(CultureInfo.InvariantCulture, $"""
                <wfs:member><ms:lokale gml:id="{id}">
                  <gml:boundedBy><gml:Envelope srsName="urn:ogc:def:crs:EPSG::2180"><gml:lowerCorner>{n} {e}</gml:lowerCorner><gml:upperCorner>{n} {e}</gml:upperCorner></gml:Envelope></gml:boundedBy>
                  <ms:GEOMETRIA><gml:Point gml:id="{id}.1" srsName="urn:ogc:def:crs:EPSG::2180"><gml:pos>{n} {e}</gml:pos></gml:Point></ms:GEOMETRIA>
                  <ms:gid>{1000 + i}</ms:gid>
                  <ms:TERYT>1465</ms:TERYT>
                  <ms:TRAN_LOKALNY_ID_IIP>{guid}</ms:TRAN_LOKALNY_ID_IIP>
                  <ms:TRAN_RODZAJ_TRANS>wolnyRynek</ms:TRAN_RODZAJ_TRANS>
                  <ms:TRAN_RODZAJ_RYNKU>rynekWtorny</ms:TRAN_RODZAJ_RYNKU>
                  <ms:TRAN_SPRZEDAJACY>osobaFizyczna</ms:TRAN_SPRZEDAJACY>
                  <ms:TRAN_KUPUJACY>osobaFizyczna</ms:TRAN_KUPUJACY>
                  <ms:TRAN_CENA_BRUTTO>{total}</ms:TRAN_CENA_BRUTTO>
                  <ms:DOK_OZNACZENIE>{SecurityElement.Escape(row.DocumentNumber)}</ms:DOK_OZNACZENIE>
                  <ms:DOK_DATA gml:id="{id}.DOK_DATA"><gml:timePosition>{row.Date}</gml:timePosition></ms:DOK_DATA>
                  <ms:DOK_TWORCA>NOTARY-REDACTED</ms:DOK_TWORCA>
                  <ms:NIER_RODZAJ>nieruchomoscLokalowa</ms:NIER_RODZAJ>
                  <ms:NIER_PRAWO>wlasnoscLokaluWrazZPrawemZwiazanym</ms:NIER_PRAWO>
                  <ms:NIER_UDZIAL>{row.Share}</ms:NIER_UDZIAL>
                  <ms:NIER_CENA_BRUTTO>{price}</ms:NIER_CENA_BRUTTO>
                  <ms:LOK_FUNKCJA>{row.Function}</ms:LOK_FUNKCJA>
                  <ms:LOK_LICZBA_IZB>2</ms:LOK_LICZBA_IZB>
                  <ms:LOK_NR_KOND>3</ms:LOK_NR_KOND>
                  <ms:LOK_POW_UZYT>{area}</ms:LOK_POW_UZYT>
                  <ms:LOK_CENA_BRUTTO>{price}</ms:LOK_CENA_BRUTTO>
                  <ms:LOK_ADRES>MSC:Testowo;UL:ulica Przykładowa;NR_PORZ:{i}</ms:LOK_ADRES>
                </ms:lokale></wfs:member>

                """);
        }

        sb.Append("</wfs:FeatureCollection>\n");
        return sb.ToString();
    }

    private async Task<McpTestSession> SessionAsync() =>
        _session ??= await McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => _stub),
            new FakeTimeProvider(Now),
            TestContext.Current.CancellationToken);

    private async Task<CallToolResult> PricesAsync(params (string Name, object? Value)[] arguments) =>
        await (await SessionAsync()).Client.CallToolAsync("property_prices", arguments.ToDictionary(a => a.Name, a => a.Value), cancellationToken: TestContext.Current.CancellationToken);

    private Task<CallToolResult> AtCentreAsync(params (string Name, object? Value)[] more) =>
        PricesAsync([("latitude", 52.2229), ("longitude", 21.0231), .. more]);

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static JsonElement Data(CallToolResult result)
    {
        Assert.True(result.IsError != true, Text(result));
        return result.StructuredContent!.Value;
    }

    private IEnumerable<Uri> RcnRequests => _stub.RequestUris.Where(u => u.Host == "mapy.geoportal.gov.pl");

    /// <summary>The decoded <c>filter</c> parameter of the first RCN request.</summary>
    private string Filter() => HttpUtility.ParseQueryString(RcnRequests.First().Query)["filter"]!;

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static List<string> Notes(JsonElement data) => [.. data.GetProperty("notes").EnumerateArray().Select(n => n.GetString()!)];

    [Fact]
    public async Task Tool_is_listed_with_the_kw_sentence()
    {
        var tools = await (await SessionAsync()).Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var tool = tools.Single(t => t.Name == "property_prices");
        Assert.Contains(KwSentence, tool.Description, StringComparison.Ordinal);
        Assert.Contains("RCN", tool.Description, StringComparison.Ordinal);
        Assert.Contains("WGS-84", tool.Description, StringComparison.Ordinal);
        Assert.Contains("not a valuation", tool.Description, StringComparison.Ordinal);
        Assert.Equal(true, tool.ProtocolTool.Annotations?.ReadOnlyHint);
        var properties = tool.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            ["address", "dateFrom", "dateTo", "kind", "latitude", "longitude", "market", "maxTransactions", "powiat", "premisesFunction", "radiusMeters"],
            properties);
    }

    [Fact]
    public async Task Coordinates_query_pins_the_bbox_axis_order()
    {
        Data(await AtCentreAsync());

        // northing first, as EPSG:2180 demands in the urn form
        Assert.Contains("<gml:lowerCorner>" + Number(Centre.Northing - 500) + " " + Number(Centre.Easting - 500) + "</gml:lowerCorner>", Filter(), StringComparison.Ordinal);
        Assert.Contains("<gml:upperCorner>" + Number(Centre.Northing + 500) + " " + Number(Centre.Easting + 500) + "</gml:upperCorner>", Filter(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Address_is_geocoded_then_queried()
    {
        _rows = DefaultRows(KruczaPoint);

        var data = Data(await PricesAsync(("address", "Warszawa, Krucza 50")));

        Assert.Equal("address", data.GetProperty("area").GetProperty("kind").GetString());
        Assert.Equal(500, data.GetProperty("area").GetProperty("radiusMeters").GetInt32());
        Assert.Equal("50", data.GetProperty("matchedAddress").GetProperty("number").GetString());
        Assert.Equal(5, data.GetProperty("transactionCount").GetInt32());
        var sources = data.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(2, sources.Count);
        Assert.Contains("UUG", sources[0].GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal("Rejestr Cen Nieruchomości (RCN), GUGiK", sources[1].GetProperty("name").GetString());
        Assert.Equal("https://mapy.geoportal.gov.pl/wss/service/rcn", sources[1].GetProperty("endpoint").GetString());
        Assert.Equal(Now, sources[1].GetProperty("retrievedAt").GetDateTimeOffset());
        Assert.Single(_stub.RequestUris, u => u.Host == "services.gugik.gov.pl");
        Assert.Single(RcnRequests);
    }

    [Fact]
    public async Task Street_only_address_returns_candidates_and_no_rcn_request()
    {
        var data = Data(await PricesAsync(("address", "Warszawa, Krucza")));

        Assert.Equal("street", Assert.Single(data.GetProperty("candidates").EnumerateArray()).GetProperty("kind").GetString());
        Assert.Contains(UnresolvedNote, Notes(data));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("matchedAddress").ValueKind);
        Assert.Equal(0, data.GetProperty("transactions").GetArrayLength());
        Assert.Equal("services.gugik.gov.pl", Assert.Single(_stub.RequestUris).Host);
    }

    [Fact]
    public async Task Default_period_is_the_last_24_months()
    {
        var data = Data(await AtCentreAsync());

        Assert.Contains("<fes:Literal>2024-10-14</fes:Literal>", Filter(), StringComparison.Ordinal);
        Assert.Contains("<fes:Literal>2026-10-14</fes:Literal>", Filter(), StringComparison.Ordinal);
        Assert.Equal("2024-10-14", data.GetProperty("period").GetProperty("from").GetString());
        Assert.Equal("2026-10-14", data.GetProperty("period").GetProperty("to").GetString());
    }

    [Fact]
    public async Task Explicit_period_and_market_are_sent()
    {
        Data(await AtCentreAsync(("dateFrom", "2025-01-01"), ("dateTo", "2025-06-30"), ("market", "primary")));

        Assert.Contains("<fes:Literal>2025-01-01</fes:Literal>", Filter(), StringComparison.Ordinal);
        Assert.Contains("<fes:Literal>2025-06-30</fes:Literal>", Filter(), StringComparison.Ordinal);
        Assert.Contains("TRAN_RODZAJ_RYNKU</fes:ValueReference><fes:Literal>rynekPierwotny", Filter(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Default_function_is_residential()
    {
        var data = Data(await AtCentreAsync());

        Assert.Contains("LOK_FUNKCJA</fes:ValueReference><fes:Literal>mieszkalna", Filter(), StringComparison.Ordinal);
        Assert.Equal("premises", data.GetProperty("kind").GetString());
        Assert.Equal("residential", data.GetProperty("premisesFunction").GetString());
    }

    [Fact]
    public async Task Function_any_omits_the_filter()
    {
        var data = Data(await AtCentreAsync(("premisesFunction", "any")));

        Assert.DoesNotContain("LOK_FUNKCJA", Filter(), StringComparison.Ordinal);
        Assert.Equal("any", data.GetProperty("premisesFunction").GetString());
    }

    [Theory]
    [InlineData("garage")]
    [InlineData("any")]
    public async Task Function_with_parcels_is_an_error(string function)
    {
        var result = await PricesAsync(("powiat", "1465"), ("kind", "parcels"), ("premisesFunction", function));

        Assert.True(result.IsError == true);
        Assert.Contains("premisesFunction", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Rows_outside_the_radius_are_dropped()
    {
        _rows.Insert(0, new("OUT-FAR/1", Centre.Easting + 900, Centre.Northing, "2026-06-01", 800000, 40));
        _rows.Insert(0, new("OUT-CORNER/1", Centre.Easting + 450, Centre.Northing + 450, "2026-06-02", 800000, 40));   // inside the square, 636 m away

        var data = Data(await AtCentreAsync(("maxTransactions", 50)));

        Assert.Equal(5, data.GetProperty("transactionCount").GetInt32());
        var documents = data.GetProperty("transactions").EnumerateArray().Select(t => t.GetProperty("documentNumber").GetString()).ToList();
        Assert.DoesNotContain(documents, d => d!.StartsWith("OUT", StringComparison.Ordinal));
        Assert.Equal("2026-05-28", data.GetProperty("newestTransaction").GetString());   // the dropped rows are newer
    }

    [Fact]
    public async Task Statistics_are_rounded_and_flag_small_samples()
    {
        _rows =
        [
            new("S/1", Centre.Easting, Centre.Northing, "2026-05-01", 1000, 10),    // 100 per m²
            new("S/2", Centre.Easting, Centre.Northing, "2026-04-01", 2010, 10),    // 201
            new("S/3", Centre.Easting, Centre.Northing, "2026-03-01", 3000, 10),    // 300
        ];

        var data = Data(await AtCentreAsync());

        var stats = data.GetProperty("statistics");
        Assert.Equal(3, stats.GetProperty("count").GetInt32());
        Assert.Equal(201m, stats.GetProperty("medianPerSquareMetre").GetDecimal());
        Assert.Equal(151m, stats.GetProperty("q1PerSquareMetre").GetDecimal());   // 150.5 rounds away from zero
        Assert.Equal(251m, stats.GetProperty("q3PerSquareMetre").GetDecimal());   // 250.5
        Assert.True(stats.GetProperty("smallSample").GetBoolean());
        Assert.Contains(Notes(data), n => n.Contains("fewer than 5", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Statistics_count_what_they_exclude()
    {
        var data = Data(await AtCentreAsync());

        var stats = data.GetProperty("statistics");
        Assert.Equal(4, stats.GetProperty("count").GetInt32());
        Assert.Equal(20000m, stats.GetProperty("medianPerSquareMetre").GetDecimal());
        Assert.Equal(1, stats.GetProperty("excludedShare").GetInt32());
        Assert.Equal(0, stats.GetProperty("excludedMissingPriceOrArea").GetInt32());
        Assert.Equal(0, stats.GetProperty("excludedNonMarketKind").GetInt32());
        Assert.Equal(0, stats.GetProperty("excludedOtherRight").GetInt32());
        Assert.Contains(Notes(data), n => n.Contains("usable area", StringComparison.Ordinal) && n.Contains("excluded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Truncated_result_asks_to_narrow()
    {
        _rows = [.. Enumerable.Range(0, 1001).Select(i => new Row("T/" + i, Centre.Easting, Centre.Northing, "2026-05-01", 800000, 40))];

        var result = await AtCentreAsync();

        Assert.True(result.IsError == true);
        Assert.Contains("narrow", Text(result), StringComparison.Ordinal);
        Assert.Contains("1,000", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_states_the_newest_transaction_date()
    {
        var data = Data(await AtCentreAsync());

        Assert.Equal("2026-05-28", data.GetProperty("newestTransaction").GetString());
        Assert.Contains(Notes(data), n => n.Contains("2026-05-28", StringComparison.Ordinal) && n.Contains("delay", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Empty_result_says_there_are_no_transactions_in_the_period()
    {
        _rows = [];

        var data = Data(await AtCentreAsync());

        Assert.Equal(JsonValueKind.Null, data.GetProperty("newestTransaction").ValueKind);
        Assert.Equal(0, data.GetProperty("transactionCount").GetInt32());
        Assert.Equal(0, data.GetProperty("statistics").GetProperty("count").GetInt32());
        Assert.Contains(Notes(data), n => n.Contains("no transactions in this period", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Transactions_are_newest_first_and_capped()
    {
        var data = Data(await AtCentreAsync(("maxTransactions", 2)));

        var listed = data.GetProperty("transactions").EnumerateArray().ToList();
        Assert.Equal(2, listed.Count);
        Assert.Equal(["2026-05-28", "2026-05-10"], listed.Select(t => t.GetProperty("date").GetString()).ToArray());
        Assert.Equal(5, data.GetProperty("transactionCount").GetInt32());
        Assert.Equal(3, data.GetProperty("transactionsOmitted").GetInt32());
        var first = listed[0];
        Assert.Equal("secondary", first.GetProperty("market").GetString());
        Assert.Equal("wolnyRynek", first.GetProperty("kind").GetString());
        Assert.Equal("osobaFizyczna", first.GetProperty("sellerType").GetString());
        Assert.Equal(800000m, first.GetProperty("totalPrice").GetDecimal());
        Assert.Equal(800000m, first.GetProperty("price").GetDecimal());
        Assert.Equal(20000m, first.GetProperty("pricePerSquareMetre").GetDecimal());
        Assert.Equal(40m, first.GetProperty("usableAreaSquareMetres").GetDecimal());
        Assert.Equal("residential", first.GetProperty("function").GetString());
        Assert.Equal(2, first.GetProperty("rooms").GetInt32());
        Assert.Equal(3, first.GetProperty("floor").GetInt32());
        Assert.Equal("A/1", first.GetProperty("documentNumber").GetString());
        Assert.Contains("Przykładowa", first.GetProperty("address").GetString(), StringComparison.Ordinal);
        var position = new Pl1992Point(Centre.Easting + 100, Centre.Northing).ToWgs84();
        Assert.Equal(position.Latitude, first.GetProperty("position").GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(position.Longitude, first.GetProperty("position").GetProperty("longitude").GetDouble(), 6);
        Assert.False(first.TryGetProperty("parcelId", out _));   // nulls are omitted
    }

    [Fact]
    public async Task A_missing_or_unreadable_deed_total_is_left_out_and_the_rows_still_count()
    {
        _rows =
        [
            new("N/1", Centre.Easting, Centre.Northing, "2026-05-01", 800000, 40, Total: ""),
            new("N/2", Centre.Easting, Centre.Northing, "2026-04-01", 600000, 30, Total: "brak"),
        ];

        var data = Data(await AtCentreAsync());

        var listed = data.GetProperty("transactions").EnumerateArray().ToList();
        Assert.Equal(2, listed.Count);
        Assert.All(listed, t => Assert.False(t.TryGetProperty("totalPrice", out _)));   // nulls are omitted
        Assert.Equal(800000m, listed[0].GetProperty("price").GetDecimal());
        Assert.Equal(20000m, listed[0].GetProperty("pricePerSquareMetre").GetDecimal());
        Assert.Equal(2, data.GetProperty("statistics").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Premises_notes_warn_that_the_total_price_repeats_for_a_deed_with_several_premises()
    {
        var notes = Notes(Data(await AtCentreAsync()));

        var note = Assert.Single(notes, n => n.Contains("one deed", StringComparison.Ordinal));
        Assert.Contains("totalPrice", note, StringComparison.Ordinal);
        Assert.Contains("repeated on each row", note, StringComparison.Ordinal);
        Assert.Contains("pricePerSquareMetre", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zero_max_transactions_lists_none_but_keeps_the_statistics()
    {
        var data = Data(await AtCentreAsync(("maxTransactions", 0)));

        Assert.Equal(0, data.GetProperty("transactions").GetArrayLength());
        Assert.Equal(5, data.GetProperty("transactionsOmitted").GetInt32());
        Assert.Equal(4, data.GetProperty("statistics").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Parcels_kind_has_no_statistics_and_lists_parcel_ids()
    {
        var data = Data(await PricesAsync(("powiat", "1465"), ("kind", "parcels")));

        Assert.Equal(JsonValueKind.Null, data.GetProperty("statistics").ValueKind);
        Assert.Equal("powiat", data.GetProperty("area").GetProperty("kind").GetString());
        Assert.Equal("1465", data.GetProperty("area").GetProperty("powiat").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("premisesFunction").ValueKind);
        // the statistics note and the premises price note would promise what this kind does not have
        Assert.DoesNotContain(Notes(data), n => n.Contains("Statistics use", StringComparison.Ordinal) || n.Contains("Price per m²", StringComparison.Ordinal));
        Assert.Contains(Notes(data), n => n.Contains("No statistics are given for this kind", StringComparison.Ordinal));
        var first = data.GetProperty("transactions").EnumerateArray().First();
        Assert.Equal("146516_8.0659.3/7", first.GetProperty("parcelId").GetString());
        Assert.Equal(1907m, first.GetProperty("landAreaSquareMetres").GetDecimal());
        Assert.Equal(1614230m, first.GetProperty("price").GetDecimal());
        Assert.False(first.TryGetProperty("rooms", out _));
        Assert.Equal("ms:dzialki", HttpUtility.ParseQueryString(RcnRequests.First().Query)["typeNames"]);
        Assert.Contains("TERYT</fes:ValueReference><fes:Literal>1465", Filter(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Output_has_no_notary_name_or_raw_fields()
    {
        var result = await AtCentreAsync();

        Data(result);
        Assert.DoesNotContain("NOTARY-REDACTED", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain("DOK_TWORCA", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain("\"fields\"", Text(result), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("WA1M/00012345/1")]
    [InlineData("wa1m 00012345 9")]
    public async Task Kw_shaped_address_is_refused_without_any_request(string kw)
    {
        var result = await PricesAsync(("address", kw));

        Assert.True(result.IsError == true);
        Assert.Contains("property_prices never accepts land-registry (KW) numbers", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain(kw, Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Exactly_one_input_is_required()
    {
        const string expected = "Give exactly one of: address, latitude+longitude, powiat.";
        var none = await PricesAsync();
        var two = await PricesAsync(("address", "Warszawa, Krucza 50"), ("powiat", "1465"));
        var coordinatesAndPowiat = await PricesAsync(("latitude", 52.2), ("longitude", 21.0), ("powiat", "1465"));

        foreach (var result in new[] { none, two, coordinatesAndPowiat })
        {
            Assert.True(result.IsError == true);
            Assert.Equal(expected, Text(result));
        }

        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Latitude_and_longitude_come_together()
    {
        var result = await PricesAsync(("latitude", 52.2));

        Assert.True(result.IsError == true);
        Assert.Contains("latitude and longitude together", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Swapped_coordinates_are_an_error()
    {
        var result = await PricesAsync(("latitude", 21.0), ("longitude", 52.0));

        Assert.True(result.IsError == true);
        Assert.Contains("latitude comes first", Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Radius_is_refused_with_powiat()
    {
        var result = await PricesAsync(("powiat", "1465"), ("radiusMeters", 500));

        Assert.True(result.IsError == true);
        Assert.Contains("radiusMeters", Text(result), StringComparison.Ordinal);
        Assert.Contains("powiat", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Theory]
    [InlineData(49)]
    [InlineData(2501)]
    public async Task Radius_out_of_range_is_an_error(int radius)
    {
        var result = await AtCentreAsync(("radiusMeters", radius));

        Assert.True(result.IsError == true);
        Assert.Contains("50 to 2500", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Theory]
    [InlineData("14.10.2026", null)]
    [InlineData(null, "yesterday")]
    public async Task Bad_dates_are_errors(string? from, string? to)
    {
        var result = await AtCentreAsync(("dateFrom", from), ("dateTo", to));

        Assert.True(result.IsError == true);
        Assert.Contains("YYYY-MM-DD", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Reversed_dates_are_an_error()
    {
        var result = await AtCentreAsync(("dateFrom", "2026-06-01"), ("dateTo", "2026-01-01"));

        Assert.True(result.IsError == true);
        Assert.Contains("dateFrom is after dateTo", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Theory]
    [InlineData("146")]
    [InlineData("abcd")]
    [InlineData("14650")]
    public async Task Powiat_must_be_four_digits(string powiat)
    {
        var result = await PricesAsync(("powiat", powiat));

        Assert.True(result.IsError == true);
        Assert.Contains("four-digit", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Explicit_null_kind_and_market_behave_as_the_defaults()
    {
        var data = Data(await AtCentreAsync(("kind", null), ("market", null)));

        Assert.Equal("premises", data.GetProperty("kind").GetString());
        Assert.Equal("any", data.GetProperty("market").GetString());
        Assert.Equal(5, data.GetProperty("transactionCount").GetInt32());
        Assert.DoesNotContain("TRAN_RODZAJ_RYNKU", Filter(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kind_and_market_still_advertise_premises_and_any_as_their_defaults()
    {
        var tools = await (await SessionAsync()).Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var properties = tools.Single(t => t.Name == "property_prices").JsonSchema.GetProperty("properties");
        Assert.Equal("premises", properties.GetProperty("kind").GetProperty("default").GetString());
        Assert.Equal("any", properties.GetProperty("market").GetProperty("default").GetString());
    }

    /// <summary>An <see cref="IRcnClient"/> that refuses every query with <paramref name="exception"/>, as the library's own check does.</summary>
    private sealed class RefusingRcnClient(Exception exception) : IRcnClient
    {
        public Task<RcnTransactions> GetTransactionsAsync(RcnQuery query, CancellationToken cancellationToken = default) => throw exception;
    }

    private static Task<CallToolResult> CentreCallAsync(Exception refusal) =>
        new PropertyTools(new RefusingRcnClient(refusal), null!, TimeProvider.System).PropertyPrices(latitude: 52.2229, longitude: 21.0231);

    [Fact]
    public async Task The_librarys_area_limit_is_a_tool_error_about_the_radius()
    {
        var result = await CentreCallAsync(new ArgumentOutOfRangeException("Area", "The area is above 25 km²."));

        Assert.True(result.IsError == true);
        Assert.Contains("smaller radius", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_argument_exceptions_are_not_reported_as_an_area_that_is_too_large()
    {
        Exception[] refusals =
        [
            new ArgumentException("Give an Area or a PowiatCode.", "Area"),   // not the area limit: a programming error
            new ArgumentException("The powiat code must be exactly four digits.", "PowiatCode"),
            new ArgumentOutOfRangeException("MaxRecords", 0, "MaxRecords must be 1 to 5000."),
        ];

        foreach (var refusal in refusals)
        {
            var thrown = await Assert.ThrowsAsync(refusal.GetType(), () => CentreCallAsync(refusal));
            Assert.Same(refusal, thrown);
        }
    }

    [Theory]
    [InlineData("kind", "flats")]
    [InlineData("market", "both")]
    [InlineData("premisesFunction", "office")]
    [InlineData("maxTransactions", 51)]
    [InlineData("maxTransactions", -1)]
    public async Task Other_bad_values_are_errors(string name, object value)
    {
        var result = await AtCentreAsync((name, value));

        Assert.True(result.IsError == true);
        Assert.Contains(name, Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Theory]
    [InlineData(2500)]
    [InlineData(50)]
    public async Task Radius_limits_are_accepted_where_floating_point_widens_the_square(int radius)
    {
        // Around 19.36 E (west of Lodz) the easting is close to 524,288 (2^19): corners 2.5 km either side lie in different binades, so a
        // 5,000 m square can come out as 5000.00000000006 m wide. Pick the first grid point where that really happens.
        (double Latitude, double Longitude)? found = null;
        for (var lat = 51.60; lat < 51.90 && found is null; lat += 0.0013)
        {
            for (var lon = 19.32; lon < 19.40 && found is null; lon += 0.0007)
            {
                var p = new Wgs84Point(lat, lon).ToPl1992();
                var width = (p.Easting + 2500) - (p.Easting - 2500);
                var height = (p.Northing + 2500) - (p.Northing - 2500);
                if (p.Easting - 2500 < 524288 && p.Easting + 2500 > 524288 && width * height > 25_000_000d)
                {
                    found = (lat, lon);
                }
            }
        }

        Assert.NotNull(found);
        var (latitude, longitude) = found.Value;
        _rows = [new("W/1", new Wgs84Point(latitude, longitude).ToPl1992().Easting, new Wgs84Point(latitude, longitude).ToPl1992().Northing, "2026-05-01", 800000, 40)];

        var data = Data(await PricesAsync(("latitude", latitude), ("longitude", longitude), ("radiusMeters", radius)));

        Assert.Equal(1, data.GetProperty("transactionCount").GetInt32());
        Assert.Single(RcnRequests);
    }

    [Fact]
    public async Task Rcn_429_reports_gugik_rate_limit()
    {
        var limited = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        await using var session = await McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => limited),
            null,
            TestContext.Current.CancellationToken);

        var result = await session.Client.CallToolAsync(
            "property_prices",
            new Dictionary<string, object?> { ["powiat"] = "1465" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError == true);
        Assert.Contains("GUGiK is rate-limiting requests", Text(result), StringComparison.Ordinal);
        Assert.Single(limited.RequestUris);   // 429 means slow down: sent once, no retry
    }
}
