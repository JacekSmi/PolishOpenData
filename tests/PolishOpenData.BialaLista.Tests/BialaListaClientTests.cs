using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.BialaLista;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.BialaLista.Tests;

public class BialaListaClientTests
{
    private const string Orlen = "7740001454";
    private const string OrlenAccount = "06160011271843983820000034";
    private const string WrongAccount = "16160011271234567890123456";

    private static HttpResponseMessage Route(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        return path switch
        {
            "/api/search/nip/7740001454" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/search-nip-orlen.json"),
            "/api/search/nip/9999999982" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/search-nip-notfound.json"),
            "/api/search/nips/7740001454,5260251049,9999999982" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/search-nips-mixed.json"),
            "/api/search/regon/610188201" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/search-regon-orlen.json"),
            "/api/search/bank-account/" + OrlenAccount => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/search-bank-account-orlen.json"),
            "/api/check/nip/7740001454/bank-account/" + OrlenAccount => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/check-nip-tak.json"),
            "/api/check/nip/7740001454/bank-account/" + WrongAccount => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/check-nip-nie.json"),
            "/api/check/regon/610188201/bank-account/" + OrlenAccount => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/check-regon-tak.json"),
            _ => StubHttpMessageHandler.FromFixture(HttpStatusCode.NotFound, "bialalista/error-404-wl190-unknown-route.json"),
        };
    }

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero));

    private static (BialaListaClient Client, StubHttpMessageHandler Stub) Create(
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null,
        BialaListaClientOptions? options = null,
        TimeProvider? clock = null)
    {
        var stub = new StubHttpMessageHandler(responder ?? Route);
        return (new BialaListaClient(new HttpClient(stub), options, clock ?? Clock()), stub);
    }

    [Fact]
    public async Task Finds_by_nip_with_warsaw_date()
    {
        var (client, stub) = Create();
        var result = await client.FindByNipAsync(Nip.Parse(Orlen), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ORLEN SPÓŁKA AKCYJNA", result.Value!.Name);
        Assert.Equal("MVRqg-98jk3i1", result.RequestId);
        Assert.Empty(result.UnknownFields);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 36, 1, TimeSpan.Zero), result.RequestDateTime.ToUniversalTime());
        Assert.Equal("https://wl-api.mf.gov.pl/api/search/nip/7740001454?date=2026-09-24", stub.RequestUris.Single().ToString());
        Assert.StartsWith("PolishOpenData/", stub.UserAgents[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Passes_explicit_date()
    {
        var (client, stub) = Create();
        await client.FindByNipAsync(Nip.Parse(Orlen), new DateOnly(2026, 1, 15), TestContext.Current.CancellationToken);
        Assert.Equal("?date=2026-01-15", stub.RequestUris.Single().Query);
    }

    [Fact]
    public async Task Not_found_returns_null_with_request_id()
    {
        var (client, _) = Create();
        var result = await client.FindByNipAsync(Nip.Parse("9999999982"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(result.Value);
        Assert.Equal("h1zOr-98jk3jj", result.RequestId);
    }

    [Fact]
    public async Task Batch_search_by_nips()
    {
        var (client, stub) = Create();
        var result = await client.FindByNipsAsync(
            [Nip.Parse(Orlen), Nip.Parse("5260251049"), Nip.Parse("9999999982")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, result.Value.Count);   // the fixture also carries an invalid-NIP entry
        Assert.Equal("MgWii-98jk3j7", result.RequestId);
        Assert.Equal("/api/search/nips/7740001454,5260251049,9999999982", stub.RequestUris.Single().AbsolutePath);
    }

    [Fact]
    public async Task Batch_search_by_regons()
    {
        var (client, stub) = Create(_ => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """
            {"result":{"entries":[
              {"identifier":"610188201","subjects":[{"name":"ORLEN SPÓŁKA AKCYJNA","nip":"7740001454","regon":"610188201","statusVat":"Czynny"}]},
              {"identifier":"545772924","subjects":[]}],
             "requestId":"RG-1","requestDateTime":"24-09-2026 10:00:00"}}
            """));
        var result = await client.FindByRegonsAsync(
            [Regon.Parse("610188201"), Regon.Parse("545772924")],
            cancellationToken: TestContext.Current.CancellationToken);

        var uri = stub.RequestUris.Single();
        Assert.Equal("/api/search/regons/610188201,545772924", uri.AbsolutePath);
        Assert.Equal("?date=2026-09-24", uri.Query);
        Assert.Equal("RG-1", result.RequestId);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero), result.RequestDateTime.ToUniversalTime());
        Assert.Equal(new[] { "610188201", "545772924" }, result.Value.Select(e => e.Identifier));
        var orlen = Assert.Single(result.Value[0].Subjects);
        Assert.Equal(Nip.Parse(Orlen), orlen.Nip);
        Assert.Equal(Regon.Parse("610188201"), orlen.Regon);
        Assert.Equal(VatStatus.Active, orlen.VatStatus);
        Assert.Empty(result.Value[1].Subjects);
        Assert.Null(result.Value[1].Error);
    }

    [Fact]
    public async Task Batch_search_by_bank_accounts()
    {
        var (client, stub) = Create(_ => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """
            {"result":{"entries":[
              {"identifier":"06160011271843983820000034","subjects":[{"name":"ORLEN SPÓŁKA AKCYJNA","nip":"7740001454","statusVat":"Czynny","accountNumbers":["06160011271843983820000034"]}]},
              {"identifier":"16160011271234567890123456","subjects":[]}],
             "requestId":"BA-1","requestDateTime":"24-09-2026 10:00:00"}}
            """));
        var result = await client.FindByBankAccountsAsync(
            [Nrb.Parse(OrlenAccount), Nrb.Parse(WrongAccount)],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("/api/search/bank-accounts/" + OrlenAccount + "," + WrongAccount, stub.RequestUris.Single().AbsolutePath);
        Assert.Equal("BA-1", result.RequestId);
        Assert.Equal(new[] { OrlenAccount, WrongAccount }, result.Value.Select(e => e.Identifier));
        var orlen = Assert.Single(result.Value[0].Subjects);
        Assert.Equal("ORLEN SPÓŁKA AKCYJNA", orlen.Name);
        Assert.Equal(Nrb.Parse(OrlenAccount), Assert.Single(orlen.AccountNumbers));
        Assert.Empty(result.Value[1].Subjects);
    }

    [Fact]
    public async Task Regon_and_account_batches_are_validated()
    {
        var (client, stub) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => client.FindByRegonsAsync([], cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => client.FindByRegonsAsync([default(Regon)], cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => client.FindByBankAccountsAsync(Enumerable.Repeat(Nrb.Parse(OrlenAccount), 31).ToArray(), cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => client.FindByBankAccountsAsync([default(Nrb)], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(stub.RequestUris);
    }

    [Fact]
    public async Task Batch_size_is_validated()
    {
        var (client, stub) = Create();
        var tooMany = Enumerable.Repeat(Nip.Parse(Orlen), 31).ToArray();
        await Assert.ThrowsAsync<ArgumentException>(() => client.FindByNipsAsync(tooMany, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => client.FindByNipsAsync([], cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => client.FindByNipsAsync([default(Nip)], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(stub.RequestUris);
    }

    [Fact]
    public async Task Finds_by_regon_and_bank_account()
    {
        var (client, _) = Create();
        var byRegon = await client.FindByRegonAsync(Regon.Parse("610188201"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Nip.Parse(Orlen), byRegon.Value!.Nip);

        var byAccount = await client.FindByBankAccountAsync(Nrb.Parse(OrlenAccount), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("ORLEN SPÓŁKA AKCYJNA", Assert.Single(byAccount.Value).Name);
        Assert.Equal("BcPla-98jk3l4", byAccount.RequestId);
    }

    [Fact]
    public async Task Checks_bank_accounts()
    {
        var (client, _) = Create();
        var assigned = await client.CheckBankAccountAsync(Nip.Parse(Orlen), Nrb.Parse(OrlenAccount), cancellationToken: TestContext.Current.CancellationToken);
        var notAssigned = await client.CheckBankAccountAsync(Nip.Parse(Orlen), Nrb.Parse(WrongAccount), cancellationToken: TestContext.Current.CancellationToken);
        var byRegon = await client.CheckBankAccountAsync(Regon.Parse("610188201"), Nrb.Parse(OrlenAccount), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(assigned.Value);
        Assert.Equal("NiR01-98jk3mi", assigned.RequestId);
        Assert.False(notAssigned.Value);
        Assert.True(byRegon.Value);
    }

    [Theory]
    [InlineData("bialalista/error-400-wl103-future-date.json", 400, "WL-103", false)]
    [InlineData("bialalista/error-400-wl118-old-date.json", 400, "WL-118", false)]
    [InlineData("bialalista/synthetic-error-400-wl195-db-updated.json", 400, "WL-195", true)]
    [InlineData("bialalista/error-404-wl190-unknown-route.json", 404, "WL-190", false)]
    public async Task Upstream_errors_carry_code_and_transience(string fixture, int status, string code, bool transient)
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.FromFixture((HttpStatusCode)status, fixture));
        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.FindByNipAsync(Nip.Parse(Orlen), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal((HttpStatusCode)status, ex.StatusCode);
        Assert.Equal(code, ex.ErrorCode);
        Assert.Equal(transient, ex.IsTransient);
        Assert.Contains(code, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Daily_limit_is_a_quota_exception_and_blocks_further_calls()
    {
        var clock = Clock();
        var calls = 0;
        var (client, stub) = Create(
            _ =>
            {
                calls++;
                return StubHttpMessageHandler.FromFixture((HttpStatusCode)429, "bialalista/synthetic-error-429-wl191-limit.json");
            },
            new BialaListaClientOptions { TrackQuota = true },
            clock);

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => client.FindByNipAsync(Nip.Parse(Orlen), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("WL-191", ex.ErrorCode);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero), ex.ResetsAt);

        // the tracker now refuses locally, without another HTTP request
        await Assert.ThrowsAsync<QuotaExceededException>(() => client.CheckBankAccountAsync(Nip.Parse(Orlen), Nrb.Parse(OrlenAccount), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
        Assert.Single(stub.RequestUris);
    }

    [Fact]
    public async Task Unknown_envelope_fields_are_reported()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"result":{"accountAssigned":"TAK","requestDateTime":"24-09-2026 02:37:54","requestId":"X1","newEvidenceField":"abc"}}"""));
        var result = await client.CheckBankAccountAsync(Nip.Parse(Orlen), Nrb.Parse(OrlenAccount), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Value);
        Assert.Equal(new[] { "newEvidenceField" }, result.UnknownFields);
    }

    [Theory]
    [InlineData("<html><body>Przerwa techniczna</body></html>")]
    [InlineData("""{"result":{"subject":{"name":"X",""")]                                 // cut off
    [InlineData("")]
    [InlineData("""{"result":{"subject":{"name":"X","registrationLegalDate":"05.07.1993"}}}""")] // date not in yyyy-MM-dd
    [InlineData("""{"result":{"subject":{"name":"X","hasVirtualAccounts":"TAK"}}}""")]          // string for a bool
    public async Task Malformed_success_response_is_an_api_exception_with_status_and_snippet(string body)
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.FindByNipAsync(Nip.Parse(Orlen), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Equal(body, ex.ResponseSnippet);
        Assert.Null(ex.ErrorCode);
        Assert.False(ex.IsTransient);
        Assert.Contains("Biała Lista", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_batch_and_check_responses_are_api_exceptions()
    {
        const string Body = """{"result":{"entries":[{"identifier":"7740001454","subjects":[{"removalDate":"31.03.2026"}]}]}}""";
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, Body));
        var batch = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.FindByNipsAsync([Nip.Parse(Orlen)], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Equal(Body, batch.ResponseSnippet);

        var (checkClient, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, "<html></html>"));
        var check = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => checkClient.CheckBankAccountAsync(Nip.Parse(Orlen), Nrb.Parse(OrlenAccount), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        Assert.Equal("<html></html>", check.ResponseSnippet);
    }

    [Theory]
    [InlineData("""{"result":null}""")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task Success_without_a_result_is_an_api_exception_with_snippet(string body)
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var search = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.FindByBankAccountAsync(Nrb.Parse(OrlenAccount), cancellationToken: TestContext.Current.CancellationToken));
        var check = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.CheckBankAccountAsync(Nip.Parse(Orlen), Nrb.Parse(OrlenAccount), cancellationToken: TestContext.Current.CancellationToken));

        foreach (var ex in new[] { search, check })
        {
            Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
            Assert.Equal(body, ex.ResponseSnippet);
            Assert.Contains("without a result", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Server_error_is_an_http_request_exception()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, """{"code":"WL-100","message":"x"}"""));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.FindByNipAsync(Nip.Parse(Orlen), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Registers_through_dependency_injection_with_shared_tracker()
    {
        var stub = new StubHttpMessageHandler(Route);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock());
        services.AddBialaListaClient(o => o.TrackQuota = true).ConfigurePrimaryHttpMessageHandler(() => stub);
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IBialaListaClient>();
        var second = provider.GetRequiredService<IBialaListaClient>();
        await first.FindByNipAsync(Nip.Parse(Orlen), cancellationToken: TestContext.Current.CancellationToken);
        await second.FindByNipAsync(Nip.Parse(Orlen), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, provider.GetRequiredService<BialaListaQuotaTracker>().SearchesUsedToday);
        Assert.Equal("?date=2026-09-24", stub.RequestUris[0].Query);
    }
}
