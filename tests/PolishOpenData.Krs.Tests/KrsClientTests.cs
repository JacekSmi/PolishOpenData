using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Krs;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Krs.Tests;

public class KrsClientTests
{
    private static HttpResponseMessage Route(HttpRequestMessage request)
    {
        var url = request.RequestUri!.PathAndQuery;
        return url switch
        {
            "/api/krs/OdpisAktualny/0000028860?rejestr=P&format=json" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "krs/current-P-0000028860-orlen.json"),
            "/api/krs/OdpisAktualny/0000030897?rejestr=S&format=json" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "krs/current-S-0000030897-wosp.json"),
            "/api/krs/OdpisAktualny/0000106150?rejestr=P&format=json" => StubHttpMessageHandler.Empty(HttpStatusCode.NoContent),
            "/api/krs/OdpisPelny/0000106150?rejestr=P&format=json" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "krs/full-P-0000106150-removed-trimmed.json"),
            "/api/krs/Biuletyn/2026-09-22" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "krs/biuletyn-2026-09-22.json"),
            "/api/krs/BiuletynGodzinowy/2026-09-22?godzinaOd=10&godzinaDo=11" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "krs/biuletyn-godzinowy-2026-09-22-10-11.json"),
            _ => StubHttpMessageHandler.FromFixture(HttpStatusCode.NotFound, "krs/not-found-404.json"),
        };
    }

    private static (KrsClient Client, StubHttpMessageHandler Stub) Create(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var stub = new StubHttpMessageHandler(responder ?? Route);
        return (new KrsClient(new HttpClient(stub)), stub);
    }

    [Fact]
    public async Task Finds_company_in_register_p()
    {
        var (client, stub) = Create();
        var result = await client.GetCurrentExtractAsync(KrsNumber.Parse("28860"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(KrsLookupStatus.Found, result.Status);
        Assert.Equal(KrsRegister.Entrepreneurs, result.Register);
        Assert.Equal("ORLEN SPÓŁKA AKCYJNA", result.Extract!.Dane!.Dzial1!.DanePodmiotu!.Nazwa);
        var uri = Assert.Single(stub.RequestUris);
        Assert.Equal("https://api-krs.ms.gov.pl/api/krs/OdpisAktualny/0000028860?rejestr=P&format=json", uri.ToString());
        Assert.StartsWith("PolishOpenData/", stub.UserAgents[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Falls_back_to_register_s_on_404()
    {
        var (client, stub) = Create();
        var result = await client.GetCurrentExtractAsync(KrsNumber.Parse("30897"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(KrsLookupStatus.Found, result.Status);
        Assert.Equal(KrsRegister.Associations, result.Register);
        Assert.Equal(2, stub.RequestUris.Count);
        Assert.EndsWith("rejestr=S&format=json", stub.RequestUris[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removed_entity_returns_removed_without_fallback()
    {
        var (client, stub) = Create();
        var result = await client.GetCurrentExtractAsync(KrsNumber.Parse("106150"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(KrsLookupStatus.Removed, result.Status);
        Assert.Equal(KrsRegister.Entrepreneurs, result.Register);
        Assert.Null(result.Extract);
        Assert.Single(stub.RequestUris);
    }

    [Fact]
    public async Task Not_found_in_both_registers()
    {
        var (client, stub) = Create();
        var result = await client.GetCurrentExtractAsync(KrsNumber.Parse("1"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(KrsLookupStatus.NotFound, result.Status);
        Assert.Null(result.Register);
        Assert.Null(result.Extract);
        Assert.Equal(2, stub.RequestUris.Count);
    }

    [Fact]
    public async Task Explicit_register_is_the_only_one_asked()
    {
        var (client, stub) = Create();
        var result = await client.GetCurrentExtractAsync(KrsNumber.Parse("30897"), KrsRegister.Associations, TestContext.Current.CancellationToken);

        Assert.Equal(KrsLookupStatus.Found, result.Status);
        Assert.Single(stub.RequestUris);
    }

    [Fact]
    public async Task Full_extract_reports_removal()
    {
        var (client, _) = Create();
        var result = await client.GetFullExtractAsync(KrsNumber.Parse("106150"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(KrsLookupStatus.Found, result.Status);
        Assert.True(result.Extract!.IsRemoved);
        Assert.Equal(new DateOnly(2022, 8, 12), result.Extract.RemovedOn);
    }

    [Fact]
    public async Task Daily_change_feed_is_padded_and_deduplicated()
    {
        var (client, _) = Create();
        var changed = new List<KrsNumber>();
        await foreach (var krs in client.GetChangedAsync(new DateOnly(2026, 9, 22), TestContext.Current.CancellationToken))
        {
            changed.Add(krs);
        }

        Assert.Equal(3338, changed.Count);
        Assert.Equal("0000002561", changed[0].ToString());
        Assert.Contains(KrsNumber.Parse("1268296"), changed);
        Assert.Equal(changed.Count, changed.Distinct().Count());
    }

    [Fact]
    public async Task Hourly_change_feed()
    {
        var (client, stub) = Create();
        var changed = new List<KrsNumber>();
        await foreach (var krs in client.GetChangedAsync(new DateOnly(2026, 9, 22), 10, 11, TestContext.Current.CancellationToken))
        {
            changed.Add(krs);
        }

        Assert.Equal(372, changed.Count);
        Assert.Equal("/api/krs/BiuletynGodzinowy/2026-09-22?godzinaOd=10&godzinaDo=11", stub.RequestUris[0].PathAndQuery);
    }

    [Fact]
    public void Hourly_change_feed_validates_hours()
    {
        var (client, _) = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => client.GetChangedAsync(new DateOnly(2026, 9, 22), 12, 11, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.GetChangedAsync(new DateOnly(2026, 9, 22), -1, 11, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.GetChangedAsync(new DateOnly(2026, 9, 22), 1, 24, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Server_error_is_an_http_request_exception()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Empty(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCurrentExtractAsync(KrsNumber.Parse("28860"), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Client_error_is_an_api_exception_with_snippet()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{"title":"Bad Request"}"""));
        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetCurrentExtractAsync(KrsNumber.Parse("28860"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("Bad Request", ex.ResponseSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_is_a_quota_exception()
    {
        var (client, _) = Create(_ =>
        {
            var response = StubHttpMessageHandler.Empty((HttpStatusCode)429);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => client.GetCurrentExtractAsync(KrsNumber.Parse("28860"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
    }

    [Fact]
    public async Task Empty_number_is_rejected()
    {
        var (client, _) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetCurrentExtractAsync(default, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Registers_through_dependency_injection()
    {
        var stub = new StubHttpMessageHandler(Route);
        var services = new ServiceCollection();
        services.AddKrsClient(o => o.BaseAddress = new Uri("https://krs.example.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => stub);
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IKrsClient>();
        var result = await client.GetCurrentExtractAsync(KrsNumber.Parse("28860"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(KrsLookupStatus.Found, result.Status);
        Assert.Equal("krs.example.test", stub.RequestUris[0].Host);
    }
}
