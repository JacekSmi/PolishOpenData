using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bdl.Tests;

public class BdlClientTests
{
    private const string ClientId = "11111111-2222-3333-4444-555555555555";

    private static BdlClient Client(StubHttpMessageHandler handler, BdlClientOptions? options = null) =>
        new(new HttpClient(handler), options);

    private static StubHttpMessageHandler Respond(HttpStatusCode status, string fixture) =>
        new(_ => StubHttpMessageHandler.FromFixture(status, fixture));

    [Fact]
    public async Task SearchUnits_SendsQueryAndMaps()
    {
        var handler = Respond(HttpStatusCode.OK, "bdl/units-search-krakow.json");

        var units = await Client(handler).SearchUnitsAsync("Kraków", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, units.Count);
        var url = handler.RequestUris.Single().AbsoluteUri;
        Assert.StartsWith("https://bdl.stat.gov.pl/api/v1/units/search?name=Krak%C3%B3w", url, StringComparison.Ordinal);
        Assert.Contains("page-size=100", url, StringComparison.Ordinal);
        Assert.Contains("format=json", url, StringComparison.Ordinal);
        Assert.Contains("lang=pl", url, StringComparison.Ordinal);
        Assert.DoesNotContain("level=", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchUnits_WithLevel_AddsLevel()
    {
        var handler = Respond(HttpStatusCode.OK, "bdl/units-search-krakow.json");

        _ = await Client(handler).SearchUnitsAsync("Kraków", BdlLevel.Gmina, TestContext.Current.CancellationToken);

        Assert.Contains("level=6", handler.RequestUris.Single().Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("K")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task SearchUnits_RejectsShortName(string? name)
    {
        var handler = Respond(HttpStatusCode.OK, "bdl/units-search-krakow.json");

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Client(handler).SearchUnitsAsync(name!, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task ClientId_SentAsHeaderOnlyWhenSet()
    {
        var withKey = Respond(HttpStatusCode.OK, "bdl/units-search-krakow.json");
        var withoutKey = Respond(HttpStatusCode.OK, "bdl/units-search-krakow.json");

        _ = await Client(withKey, new BdlClientOptions { ClientId = ClientId }).SearchUnitsAsync("Kraków", cancellationToken: TestContext.Current.CancellationToken);
        _ = await Client(withoutKey).SearchUnitsAsync("Kraków", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ClientId, withKey.Requests.Single().ClientId);
        Assert.Null(withoutKey.Requests.Single().ClientId);
        Assert.DoesNotContain(ClientId, withKey.RequestUris.Single().ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lang_En_SendsLangEn()
    {
        var handler = Respond(HttpStatusCode.OK, "bdl/units-search-krakow.json");

        _ = await Client(handler, new BdlClientOptions { Language = BdlLanguage.En }).SearchUnitsAsync("Kraków", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("lang=en", handler.RequestUris.Single().Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "bdl/error-400.json", "9")]
    [InlineData(HttpStatusCode.PreconditionFailed, "bdl/error-412.json", null)]
    public async Task ClientError_ThrowsApiException(HttpStatusCode status, string fixture, string? code)
    {
        var handler = Respond(status, fixture);

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).SearchUnitsAsync("Kraków", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(status, ex.StatusCode);
        if (code is not null)
        {
            Assert.Equal(code, ex.ErrorCode);
        }

        Assert.NotNull(ex.ErrorCode);
    }

    [Theory]
    [InlineData("""{"result":"limit","reason":"too many"}""")]
    [InlineData("<html>Too Many Requests</html>")]
    public async Task Status429_ThrowsQuota_AndNeverLeaksTheClientId(string body)
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json((HttpStatusCode)429, body));

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() =>
            Client(handler, new BdlClientOptions { ClientId = ClientId }).SearchUnitsAsync("Kraków", cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain(ClientId, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiException_NeverContainsTheClientId()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"errors\":[{\"errorCode\":9,\"errorReason\":\"echo " + ClientId + "\"}]}"));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() =>
            Client(handler, new BdlClientOptions { ClientId = ClientId }).SearchUnitsAsync("Kraków", cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain(ClientId, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUnit_Found_Maps()
    {
        var handler = Respond(HttpStatusCode.OK, "bdl/unit-krakow.json");

        var unit = await Client(handler).GetUnitAsync("011212161011", TestContext.Current.CancellationToken);

        Assert.Equal("Kraków", unit!.Name);
        Assert.Equal(BdlLevel.Gmina, unit.Level);
        Assert.Contains("/units/011212161011?", handler.RequestUris.Single().ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUnit_NotFound_ReturnsNull()
    {
        var handler = Respond(HttpStatusCode.NotFound, "bdl/error-404.json");

        Assert.Null(await Client(handler).GetUnitAsync("999999999999", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetUnit_EmptyId_Throws()
    {
        var handler = Respond(HttpStatusCode.OK, "bdl/unit-krakow.json");

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Client(handler).GetUnitAsync(" ", TestContext.Current.CancellationToken));
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task GetSubject_Maps()
    {
        var handler = Respond(HttpStatusCode.OK, "bdl/subject-P2137.json");

        var s = await Client(handler).GetSubjectAsync("P2137", TestContext.Current.CancellationToken);

        Assert.Equal("Ludność wg grup wieku i płci", s!.Name);
        Assert.Equal(new[] { "Wiek", "Płeć" }, s.Dimensions);
    }

    [Fact]
    public async Task GetSubject_NotFound_ReturnsNull()
    {
        var handler = Respond(HttpStatusCode.NotFound, "bdl/error-404.json");

        Assert.Null(await Client(handler).GetSubjectAsync("P9999999", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetVariable_MapsYears_AndNotFoundIsNull()
    {
        var found = await Client(Respond(HttpStatusCode.OK, "bdl/variable-72305.json")).GetVariableAsync(72305, TestContext.Current.CancellationToken);
        var missing = await Client(Respond(HttpStatusCode.NotFound, "bdl/error-404.json")).GetVariableAsync(99999999, TestContext.Current.CancellationToken);

        Assert.Contains(2025, found!.Years!);
        Assert.Null(missing);
    }

    [Fact]
    public async Task SearchVariables_ReturnsTotalAndPassesSubject()
    {
        var handler = Respond(HttpStatusCode.OK, "bdl/variables-search-ludnosc.json");

        var page = await Client(handler).SearchVariablesAsync("ludność ogółem", "P2137", TestContext.Current.CancellationToken);

        Assert.Equal(57, page.TotalRecords);
        Assert.Equal(5, page.Items.Count);
        Assert.Contains("subject-id=P2137", handler.RequestUris.Single().Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreadableBody_ThrowsApiException()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, "{"));

        await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).SearchUnitsAsync("Kraków", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ServerError_IsTransient()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(HttpStatusCode.BadGateway));

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).SearchUnitsAsync("Kraków", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_RejectsHttpBaseAddress()
    {
        Assert.Throws<ArgumentException>(() => new BdlClient(new HttpClient(), new BdlClientOptions { BaseAddress = new Uri("http://bdl.stat.gov.pl/api/v1/") }));
    }
}
