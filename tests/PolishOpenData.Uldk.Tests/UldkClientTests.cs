using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Geo;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Uldk.Tests;

public class UldkClientTests
{
    private static readonly ParcelId Parcel = ParcelId.Parse("146510_8.0507.41/1");

    private static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private static HttpResponseMessage FromFixture(string fixture) => Text(Fixture.Read("uldk/" + fixture));

    private static string Query(Uri uri) => Uri.UnescapeDataString(uri.Query);

    private static (UldkClient Client, StubHttpMessageHandler Stub) Create(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var stub = new StubHttpMessageHandler(responder);
        return (new UldkClient(new HttpClient(stub)), stub);
    }

    [Fact]
    public async Task GetParcelById_parses_all_fields()
    {
        var (client, _) = Create(_ => FromFixture("parcel-146510_8.0507.41_1.txt"));

        var parcel = await client.GetParcelByIdAsync(Parcel, ParcelGeometryKind.Pl1992, TestContext.Current.CancellationToken);

        Assert.NotNull(parcel);
        Assert.Equal("146510_8.0507.41/1", parcel.Id.ToString());
        Assert.Equal("41/1", parcel.Number);
        Assert.Equal("5-05-07", parcel.RegionName);
        Assert.Equal("Warszawa (miasto)", parcel.CommuneName);
        Assert.Equal("powiat Warszawa", parcel.CountyName);
        Assert.Equal("mazowieckie", parcel.VoivodeshipName);
        Assert.Equal("powiat m. Warszawa (wfs)", parcel.DataSource);
        Assert.Equal(637928.30, parcel.Extent.Min.Easting, 2);
        Assert.Equal(485970.88, parcel.Extent.Min.Northing, 2);
        Assert.Equal(638215.32, parcel.Extent.Max.Easting, 2);
        Assert.Equal(486033.10, parcel.Extent.Max.Northing, 2);
        Assert.NotNull(parcel.Geometry);
        Assert.Equal(2180, parcel.Geometry.Srid);
        Assert.StartsWith("POLYGON((638144.500192729 486022.681429996,", parcel.Geometry.Wkt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetParcelById_without_geometry_requests_no_geom_wkt()
    {
        var (client, stub) = Create(_ => FromFixture("xy-warsaw.txt"));

        var parcel = await client.GetParcelByIdAsync(Parcel, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(parcel!.Geometry);
        var uri = Assert.Single(stub.RequestUris);
        Assert.Equal("?request=GetParcelById&id=146510_8.0507.41/1&result=id,parcel,region,commune,county,voivodeship,geom_extent,datasource&srid=2180", Query(uri));
    }

    [Fact]
    public async Task GetParcelById_not_found_returns_null()
    {
        var (client, _) = Create(_ => FromFixture("not-found.txt"));

        Assert.Null(await client.GetParcelByIdAsync(ParcelId.Parse("146501_8.0108.27"), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetParcelById_html_block_page_throws_blocked()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Html(HttpStatusCode.OK, Fixture.Read("uldk/block-page.html")));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetParcelByIdAsync(Parcel, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("blocked", exception.ErrorCode);
        Assert.True(exception.IsTransient);
        Assert.Contains("Request Rejected", exception.ResponseSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetParcelById_minus_one_other_message_throws_uldk_error()
    {
        var (client, _) = Create(_ => Text("-1 niepoprawny identyfikator\n"));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetParcelByIdAsync(Parcel, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("uldk_error", exception.ErrorCode);
        Assert.Equal("niepoprawny identyfikator", exception.Message);
    }

    [Theory]
    [InlineData("garbage\n")]
    [InlineData("0\n146510_8.0507.41/1|41/1|5-05-07|W|p|m|637928.3,485970.8,abc,486033.1|s|SRID=2180;POLYGON((1 2))\n")]
    [InlineData("0\n146510_8.0507.41/1|41/1|5-05-07|W|p|m|0,0,1,1|s|SRID=2180;POLYGON((1 2))\n")]
    [InlineData("0\n146510_8.0507.41/1|41/1|5-05-07|W|p|m|637928.3,485970.8,638215.3,486033.1|s|POLYGON((1 2))\n")]
    [InlineData("0\n146510_8.0507.41/1|41/1|5-05-07|W|p|m|638215.3,485970.8,637928.3,486033.1|s|SRID=2180;POLYGON((1 2))\n")]   // min east of max
    [InlineData("0\n146510_8.0507.41/1|41/1|5-05-07|W|p|m|100000,100000,100010,100010|s|SRID=2180;POLYGON((1 2))\n")]   // centre is on the grid but not in WGS-84 Poland
    public async Task GetParcelById_unreadable_answer_throws_api_exception_with_http_200(string body)
    {
        var (client, _) = Create(_ => Text(body));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetParcelByIdAsync(Parcel, ParcelGeometryKind.Pl1992, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);   // answered, but unusable: not "rejected"
        Assert.Null(exception.ErrorCode);
    }

    [Fact]
    public async Task GetParcelAt_default_point_throws_before_any_request()
    {
        var (client, stub) = Create(_ => FromFixture("xy-warsaw.txt"));

        var wgs84 = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetParcelAtAsync(default(Wgs84Point), cancellationToken: TestContext.Current.CancellationToken));
        var pl1992 = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetParcelAtAsync(default(Pl1992Point), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("point", wgs84.ParamName);
        Assert.Equal("point", pl1992.ParamName);

        Assert.Empty(stub.RequestUris);
    }

    [Fact]
    public async Task GetParcelById_non_success_status_throws_api_exception()
    {
        var (client, _) = Create(_ => Text("bad", HttpStatusCode.BadRequest));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetParcelByIdAsync(Parcel, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task GetParcelById_escapes_the_slash_and_sheet_in_the_id()
    {
        var (client, stub) = Create(_ => FromFixture("not-found.txt"));

        _ = await client.GetParcelByIdAsync(ParcelId.Parse("146510_8.0507.AR_2.41/1"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("id=146510_8.0507.AR_2.41%2F1&", stub.RequestUris[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetParcelAt_pl1992_sends_easting_first()
    {
        var (client, stub) = Create(_ => FromFixture("xy-warsaw.txt"));

        var parcel = await client.GetParcelAtAsync(new Pl1992Point(638000, 486000), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("146510_8.0507.41/1", parcel!.Id.ToString());
        var query = Query(Assert.Single(stub.RequestUris));
        Assert.Contains("request=GetParcelByXY", query, StringComparison.Ordinal);
        Assert.Contains("xy=638000,486000,2180", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetParcelAt_wgs84_sends_longitude_first()
    {
        var (client, stub) = Create(_ => FromFixture("xy-warsaw.txt"));

        _ = await client.GetParcelAtAsync(new Wgs84Point(52.2229, 21.0231), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("xy=21.0231,52.2229,4326", Query(Assert.Single(stub.RequestUris)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetParcelAt_not_found_returns_null()
    {
        var (client, _) = Create(_ => FromFixture("not-found.txt"));

        Assert.Null(await client.GetParcelAtAsync(new Pl1992Point(638000, 486000), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetParcelById_wgs84_geometry_makes_second_request_with_srid_4326()
    {
        var (client, stub) = Create(request => FromFixture(Query(request.RequestUri!).Contains("result=geom_wkt", StringComparison.Ordinal)
            ? "parcel-146510_8.0507.41_1-wgs84-geom.txt"
            : "xy-warsaw.txt"));

        var parcel = await client.GetParcelByIdAsync(Parcel, ParcelGeometryKind.Wgs84, TestContext.Current.CancellationToken);

        Assert.Equal(2, stub.RequestUris.Count);
        Assert.Contains("srid=2180", Query(stub.RequestUris[0]), StringComparison.Ordinal);
        Assert.Equal("?request=GetParcelById&id=146510_8.0507.41/1&result=geom_wkt&srid=4326", Query(stub.RequestUris[1]));
        Assert.Equal(4326, parcel!.Geometry!.Srid);
        Assert.StartsWith("POLYGON((21.0230574318539 52.2229074776239", parcel.Geometry.Wkt, StringComparison.Ordinal);
        Assert.Equal(637928.30, parcel.Extent.Min.Easting, 2);
    }

    [Fact]
    public async Task GetParcelById_pl1992_geometry_requests_geom_wkt_in_one_request()
    {
        var (client, stub) = Create(_ => FromFixture("parcel-146510_8.0507.41_1.txt"));

        _ = await client.GetParcelByIdAsync(Parcel, ParcelGeometryKind.Pl1992, TestContext.Current.CancellationToken);

        var query = Query(Assert.Single(stub.RequestUris));
        Assert.Contains("&result=id,parcel,region,commune,county,voivodeship,geom_extent,datasource,geom_wkt&", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetParcelAt_wgs84_geometry_asks_for_the_geometry_of_the_parcel_found()
    {
        var (client, stub) = Create(request => FromFixture(Query(request.RequestUri!).Contains("result=geom_wkt", StringComparison.Ordinal)
            ? "parcel-146510_8.0507.41_1-wgs84-geom.txt"
            : "xy-warsaw.txt"));

        var parcel = await client.GetParcelAtAsync(new Wgs84Point(52.2229, 21.0231), ParcelGeometryKind.Wgs84, TestContext.Current.CancellationToken);

        Assert.Equal(2, stub.RequestUris.Count);
        Assert.Contains("xy=21.0231,52.2229,4326", Query(stub.RequestUris[0]), StringComparison.Ordinal);
        Assert.Equal("?request=GetParcelById&id=146510_8.0507.41/1&result=geom_wkt&srid=4326", Query(stub.RequestUris[1]));
        Assert.Equal(4326, parcel!.Geometry!.Srid);
    }

    [Fact]
    public async Task GetParcelById_wgs84_geometry_missing_in_second_answer_throws_api_exception()
    {
        var (client, _) = Create(request => FromFixture(Query(request.RequestUri!).Contains("result=geom_wkt", StringComparison.Ordinal)
            ? "count-not-found.txt"
            : "xy-warsaw.txt"));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetParcelByIdAsync(Parcel, ParcelGeometryKind.Wgs84, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Contains("disappeared between the two requests", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answer_with_a_utf8_byte_order_mark_is_parsed()
    {
        var (client, _) = Create(_ => Text("\uFEFF" + Fixture.Read("uldk/xy-warsaw.txt")));

        var parcel = await client.GetParcelByIdAsync(Parcel, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("146510_8.0507.41/1", parcel!.Id.ToString());
    }

    [Fact]
    public async Task FindParcels_returns_every_match()
    {
        var (client, stub) = Create(_ => FromFixture("count-krzewina-134.txt"));

        var parcels = await client.FindParcelsAsync("Krzewina 134", TestContext.Current.CancellationToken);

        var parcel = Assert.Single(parcels);
        Assert.Equal("022503_5.0003.134", parcel.Id.ToString());
        Assert.Equal("Bogatynia", parcel.CommuneName);
        Assert.Null(parcel.Geometry);
        Assert.Equal("?request=GetParcelByIdOrNr&id=Krzewina 134&result=id,parcel,region,commune,county,voivodeship,geom_extent,datasource&srid=2180", Query(Assert.Single(stub.RequestUris)));
    }

    [Fact]
    public async Task FindParcels_encodes_name_with_space_and_diacritics()
    {
        var (client, stub) = Create(_ => FromFixture("count-not-found.txt"));

        _ = await client.FindParcelsAsync("Stara Wieś 756", TestContext.Current.CancellationToken);

        Assert.Contains("id=Stara%20Wie%C5%9B%20756&", stub.RequestUris[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindParcels_encodes_a_full_parcel_id_with_a_slash()
    {
        var (client, stub) = Create(_ => FromFixture("count-not-found.txt"));

        _ = await client.FindParcelsAsync("146510_8.0507.41/1", TestContext.Current.CancellationToken);

        Assert.Contains("id=146510_8.0507.41%2F1&", stub.RequestUris[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindParcels_not_found_returns_empty()
    {
        var (client, _) = Create(_ => FromFixture("count-not-found.txt"));

        Assert.Empty(await client.FindParcelsAsync("Qwertyuiop 99999", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task FindParcels_blank_text_throws(string? text) =>
        _ = await Assert.ThrowsAsync<ArgumentException>(() => CreateClient().FindParcelsAsync(text!, TestContext.Current.CancellationToken));

    private static UldkClient CreateClient() => Create(_ => FromFixture("count-not-found.txt")).Client;

    [Fact]
    public async Task Requests_send_the_user_agent()
    {
        var (client, stub) = Create(_ => FromFixture("xy-warsaw.txt"));

        _ = await client.GetParcelByIdAsync(Parcel, cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith("PolishOpenData/", stub.UserAgents[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AddUldkClient_limits_connections_to_4()
    {
        using var provider = new ServiceCollection().AddUldkClient().Services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(IUldkClient));
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        Assert.Equal(4, Assert.IsType<HttpClientHandler>(handler).MaxConnectionsPerServer);
    }
}
