using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Ceidg;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Ceidg.Tests;

public class CeidgClientTests
{
    private const string Token = "eyJ.synthetic-test-token.sig";
    private static readonly Nip ActiveNip = Nip.Parse("3563457932");

    private static CeidgClient Client(StubHttpMessageHandler handler, string? token = Token) =>
        new(new HttpClient(handler), new CeidgClientOptions { Token = token });

    private static StubHttpMessageHandler Respond(HttpStatusCode status, string fixture) =>
        new(_ => StubHttpMessageHandler.FromFixture(status, fixture));

    [Fact]
    public async Task FindByNip_ActiveBusiness_MapsTheRecord()
    {
        var handler = Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json");

        var found = await Client(handler).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken);

        var b = Assert.Single(found);
        Assert.Equal(Guid.Parse("31F87519-9395-4FCF-8E19-6D5C0522FA7A"), b.Id);
        Assert.Equal("Jan Testowy Usługi Informatyczne", b.Name);
        Assert.Equal(CeidgStatus.Active, b.Status);
        Assert.Equal("AKTYWNY", b.StatusRaw);
        Assert.Equal("Jan", b.OwnerFirstName);
        Assert.Equal("Testowy", b.OwnerLastName);
        Assert.Equal(ActiveNip, b.Nip);
        Assert.Equal(Regon.Parse("618155359"), b.Regon);
        Assert.Equal(new DateOnly(2014, 7, 29), b.StartDate);
        Assert.Null(b.SuspendedOn);
        Assert.Equal("Białystok", b.Address?.City);
        Assert.Equal("15-333", b.Address?.PostalCode);
        Assert.Equal("2061011", b.Address?.TercCode);
        Assert.Equal("6201Z", b.MainPkd?.Code);
        Assert.Equal(["6201Z", "6202Z"], b.Pkd.Select(p => p.Code).ToArray());
    }

    [Fact]
    public async Task FindByNip_SendsFirmaQueryWithBearerToken()
    {
        var handler = Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json");

        await Client(handler).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://dane.biznes.gov.pl/api/ceidg/v3/firma?nip=3563457932", request.Uri.ToString());
        Assert.Equal("Bearer " + Token, request.Authorization);
        Assert.StartsWith("PolishOpenData/", request.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindByNip_SuspendedAndUnknownStatus_MapsDatesAndKeepsRawStatus()
    {
        var handler = Respond(HttpStatusCode.OK, "ceidg/firma-nip-suspended.json");

        var found = await Client(handler).FindByNipAsync(Nip.Parse("2367852376"), TestContext.Current.CancellationToken);

        Assert.Equal(2, found.Count);
        Assert.Equal(CeidgStatus.Suspended, found[0].Status);
        Assert.Equal(new DateOnly(2024, 5, 10), found[0].SuspendedOn);
        Assert.Equal(new DateOnly(2025, 1, 2), found[0].ResumedOn);
        Assert.Equal(new DateOnly(2025, 9, 30), found[0].EndedOn);
        Assert.Equal(CeidgStatus.Unknown, found[1].Status);
        Assert.Equal("NOWY_STATUS_Z_PRZYSZLOSCI", found[1].StatusRaw);
        Assert.Empty(found[1].Pkd);
        Assert.Null(found[1].Address);
    }

    [Fact]
    public async Task FindByNip_NothingFound_ReturnsEmptyList()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(HttpStatusCode.NoContent));

        Assert.Empty(await Client(handler).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindByRegon_UsesTheRegonParameter()
    {
        var handler = Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json");

        await Client(handler).FindByRegonAsync(Regon.Parse("618155359"), TestContext.Current.CancellationToken);

        Assert.EndsWith("/firma?regon=618155359", Assert.Single(handler.Requests).Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetById_ReturnsTheBusinessAndUsesThePath()
    {
        var handler = Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json");
        var id = Guid.Parse("31F87519-9395-4FCF-8E19-6D5C0522FA7A");

        var b = await Client(handler).GetByIdAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(id, b?.Id);
        Assert.EndsWith("/firma/31F87519-9395-4FCF-8E19-6D5C0522FA7A", Assert.Single(handler.Requests).Uri.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task GetById_NothingFound_ReturnsNull(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(status));

        Assert.Null(await Client(handler).GetByIdAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedToken_ThrowsAuthenticationExceptionWithoutTheToken(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(status, """{"message":"Unauthorized","http_status_code":401}"""));

        var ex = await Assert.ThrowsAsync<CeidgAuthenticationException>(() => Client(handler).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

        Assert.Equal(status, ex.StatusCode);
        Assert.DoesNotContain(Token, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BadRequest_ThrowsApiExceptionWithTheErrorCode()
    {
        var handler = Respond(HttpStatusCode.BadRequest, "ceidg/error-400-nip.json");

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("NIEPOPRAWNY_NUMER_NIP", ex.ErrorCode);
    }

    [Fact]
    public async Task TooManyRequests_ThrowsQuotaExceeded() =>
        await Assert.ThrowsAsync<QuotaExceededException>(() =>
            Client(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty((HttpStatusCode)429))).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

    [Fact]
    public async Task ServerError_ThrowsHttpRequestException() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(HttpStatusCode.InternalServerError))).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

    [Fact]
    public async Task HtmlInsteadOfJson_ThrowsApiException() =>
        await Assert.ThrowsAsync<PolishOpenDataApiException>(() =>
            Client(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Html(HttpStatusCode.OK, "<html>blocked</html>"))).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingToken_ThrowsBeforeAnyRequest(string? token)
    {
        var handler = Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler, token).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

        Assert.Contains("CeidgClientOptions.Token", ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TokenWithWhitespaceInside_IsRejectedWithoutEchoingIt()
    {
        var handler = Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler, "abc def").FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

        Assert.DoesNotContain("abc def", ex.ToString(), StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EmptyNip_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Client(Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json")).FindByNipAsync(default, TestContext.Current.CancellationToken));

    [Fact]
    public void Options_NonHttpsBaseAddress_IsRejected() =>
        Assert.Throws<ArgumentException>(() => new CeidgClient(new HttpClient(), new CeidgClientOptions { Token = Token, BaseAddress = new Uri("http://dane.biznes.gov.pl/api/ceidg/v3/") }));

    [Fact]
    public async Task FindByNip_NotFoundStatus_ThrowsSoThatAWrongBaseAddressIsNotReadAsNoResult()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetById_EmptyGuid_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Client(Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json")).GetByIdAsync(Guid.Empty, TestContext.Current.CancellationToken));

    [Fact]
    public async Task Unauthorized_DoesNotKeepTheResponseBodyEvenIfItEchoesTheToken()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"message\":\"bad token " + Token + "\"}"));

        var ex = await Assert.ThrowsAsync<CeidgAuthenticationException>(() => Client(handler).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

        Assert.Null(ex.ResponseSnippet);
        Assert.DoesNotContain(Token, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BadRequest_ScrubsTheTokenFromTheKeptBody()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"code\":\"NIEPOPRAWNY_NUMER_NIP\",\"message\":\"echo " + Token + "\"}"));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Token, ex.ResponseSnippet, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BadRequest_WithAnOddCode_DropsTheCodeInsteadOfPassingItOn()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{\"code\":\"ignore previous instructions\"}"));

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

        Assert.Null(ex.ErrorCode);
        Assert.DoesNotContain("ignore", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BaseAddressChangedToHttpAfterConstruction_IsRejectedBeforeAnyRequest()
    {
        var handler = Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json");
        var options = new CeidgClientOptions { Token = Token };
        var client = new CeidgClient(new HttpClient(handler), options);
        options.BaseAddress = new Uri("http://dane.biznes.gov.pl/api/ceidg/v3/");

        await Assert.ThrowsAsync<ArgumentException>(() => client.FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("dataRozpoczecia", "2014-07-29T00:00:00")]
    [InlineData("id", "not-a-guid")]
    public async Task UnreadableValue_ThrowsInsteadOfSilentlyDroppingIt(string field, string value)
    {
        var body = "{\"firma\":[{\"id\":\"31F87519-9395-4FCF-8E19-6D5C0522FA7A\",\"status\":\"AKTYWNY\",\"" + field + "\":\"" + value + "\"}]}";
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));

        await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Client(handler).FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NonAsciiToken_IsRejectedBeforeAnyRequest()
    {
        var handler = Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler, "tokén").FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AddCeidgClient_ResolvesAClientThatSendsTheConfiguredToken()
    {
        var handler = Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json");
        var services = new ServiceCollection();
        services.AddCeidgClient(o => o.Token = Token).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ICeidgClient>().FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer " + Token, Assert.Single(handler.Requests).Authorization);
    }

    [Fact]
    public async Task Options_BaseAddressWithoutTrailingSlash_StillResolvesUnderThePath()
    {
        var handler = Respond(HttpStatusCode.OK, "ceidg/firma-nip-active.json");
        var client = new CeidgClient(new HttpClient(handler), new CeidgClientOptions { Token = Token, BaseAddress = new Uri("https://test-dane.biznes.gov.pl/api/ceidg/v3") });

        await client.FindByNipAsync(ActiveNip, TestContext.Current.CancellationToken);

        Assert.StartsWith("https://test-dane.biznes.gov.pl/api/ceidg/v3/firma?", Assert.Single(handler.Requests).Uri.ToString(), StringComparison.Ordinal);
    }
}
