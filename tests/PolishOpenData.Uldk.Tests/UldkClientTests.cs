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
        Assert.Equal("5-05-07", parcel.Region);
        Assert.Equal("Warszawa (miasto)", parcel.Commune);
        Assert.Equal("powiat Warszawa", parcel.County);
        Assert.Equal("mazowieckie", parcel.Voivodeship);
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
