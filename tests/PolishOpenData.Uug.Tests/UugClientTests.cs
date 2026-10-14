using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Geo;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Uug.Tests;

public class UugClientTests
{
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage FromFixture(string fixture) => Json(Fixture.Read("uug/" + fixture));

    private static (UugClient Client, StubHttpMessageHandler Stub) Create(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var stub = new StubHttpMessageHandler(responder);
        return (new UugClient(new HttpClient(stub)), stub);
    }

    [Fact]
    public async Task Geocode_address_parses_and_converts_to_wgs84()
    {
        var (client, stub) = Create(_ => FromFixture("address-krucza-50.json"));

        var match = Assert.Single(await client.GeocodeAsync("Warszawa, Krucza 50", TestContext.Current.CancellationToken));

        Assert.Equal(UugMatchKind.Address, match.Kind);
        Assert.Equal("Warszawa", match.City);
        Assert.Null(match.CityPart);
        Assert.Equal("ulica Krucza", match.Street);
        Assert.Equal("50", match.Number);
        Assert.Equal("00-025", match.PostalCode);
        Assert.Equal("146501", match.Teryt);
        Assert.Equal("0918123", match.Simc);
        Assert.Equal("10013", match.Ulic);
        Assert.Equal(0.76, match.Accuracy);
        Assert.Null(match.DistanceMeters);
        var expected = new Pl1992Point(637659.46, 486969.16).ToWgs84();
        Assert.Equal(expected.Latitude, match.Location.Latitude, 6);
        Assert.Equal(expected.Longitude, match.Location.Longitude, 6);
        Assert.Equal("?request=GetAddress&address=Warszawa%2C%20Krucza%2050", Assert.Single(stub.RequestUris).Query);
    }

    [Fact]
    public async Task Geocode_street_only_returns_street_kind()
    {
        var (client, _) = Create(_ => FromFixture("street-krucza.json"));

        var match = Assert.Single(await client.GeocodeAsync("Warszawa, Krucza", TestContext.Current.CancellationToken));

        Assert.Equal(UugMatchKind.Street, match.Kind);
        Assert.Equal("ulica Krucza", match.Street);
        Assert.Null(match.Number);
        // the representative point (x/y), not the line
        var expected = new Pl1992Point(637773.92, 486567.32).ToWgs84();
        Assert.Equal(expected.Latitude, match.Location.Latitude, 6);
        Assert.Equal(expected.Longitude, match.Location.Longitude, 6);
    }

    [Fact]
    public async Task Geocode_no_results_returns_empty()
    {
        var (client, _) = Create(_ => FromFixture("none.json"));

        Assert.Empty(await client.GeocodeAsync("Qwertyuiop 999", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Geocode_orders_results_by_numeric_key()
    {
        var (client, _) = Create(_ => Json("""{"type":"city","results":{"10":{"city":"C","x":"500000","y":"500000"},"2":{"city":"B","x":"500000","y":"500000"},"1":{"city":"A","x":"500000","y":"500000"}}}"""));

        var matches = await client.GeocodeAsync("x", TestContext.Current.CancellationToken);

        Assert.Equal(["A", "B", "C"], matches.Select(m => m.City));
        Assert.All(matches, m => Assert.Equal(UugMatchKind.City, m.Kind));
    }

    [Fact]
    public async Task Geocode_html_block_page_throws_blocked()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Html(HttpStatusCode.OK, Fixture.Read("uug/block-page.html")));

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GeocodeAsync("Warszawa, Krucza 50", TestContext.Current.CancellationToken));

        Assert.Equal("blocked", exception.ErrorCode);
        Assert.True(exception.IsTransient);
        Assert.Contains("Request Rejected", exception.ResponseSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Geocode_encodes_polish_letters()
    {
        var (client, stub) = Create(_ => FromFixture("none.json"));

        _ = await client.GeocodeAsync("Łódź, Piotrkowska 104", TestContext.Current.CancellationToken);

        Assert.Contains("address=%C5%81%C3%B3d%C5%BA%2C%20Piotrkowska%20104", Assert.Single(stub.RequestUris).Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Geocode_blank_address_throws(string? address)
    {
        var (client, _) = Create(_ => FromFixture("none.json"));

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.GeocodeAsync(address!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reverse_sends_lon_lat_and_reads_lon_lat()
    {
        var (client, stub) = Create(_ => FromFixture("reverse-zurawia.json"));

        var match = await client.ReverseGeocodeAsync(new Wgs84Point(52.2290, 21.0198), TestContext.Current.CancellationToken);

        var query = Uri.UnescapeDataString(Assert.Single(stub.RequestUris).Query);
        Assert.Contains("request=GetAddressReverse", query, StringComparison.Ordinal);
        Assert.Contains("location=POINT(21.0198 52.229)", query, StringComparison.Ordinal);
        Assert.Contains("srid=4326", query, StringComparison.Ordinal);
        Assert.NotNull(match);
        Assert.Equal(UugMatchKind.Address, match.Kind);
        Assert.Equal("ulica Żurawia", match.Street);
        Assert.Equal("3/5", match.Number);
        Assert.Equal(52.2290812705384, match.Location.Latitude);
        Assert.Equal(21.0197433738576, match.Location.Longitude);
        Assert.Equal(9.83, match.DistanceMeters!.Value, 2);
        Assert.Null(match.Accuracy);
    }

    [Fact]
    public async Task Reverse_none_returns_null()
    {
        var (client, _) = Create(_ => FromFixture("reverse-none.json"));

        Assert.Null(await client.ReverseGeocodeAsync(new Wgs84Point(54.9, 18.0), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Requests_send_the_user_agent()
    {
        var (client, stub) = Create(_ => FromFixture("none.json"));

        _ = await client.GeocodeAsync("x", TestContext.Current.CancellationToken);

        Assert.StartsWith("PolishOpenData/", stub.UserAgents[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AddUugClient_limits_connections_to_4()
    {
        using var provider = new ServiceCollection().AddUugClient().Services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(IUugClient));
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        Assert.Equal(4, Assert.IsType<HttpClientHandler>(handler).MaxConnectionsPerServer);
    }
}
