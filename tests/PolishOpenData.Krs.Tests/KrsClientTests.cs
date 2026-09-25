using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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

    [Theory]
    [InlineData("<html><body>Przerwa techniczna</body></html>")]
    [InlineData("""{"odpis":{"rodzaj":"Aktualny","naglowekA":""")]              // cut off
    [InlineData("")]
    [InlineData("""{"odpis":{"naglowekA":{"stanZDnia":"2026-09-17"}}}""")]     // date not in dd.MM.yyyy
    [InlineData("""{"odpis":{"naglowekA":{"numerOstatniegoWpisu":"many"}}}""")] // string for a number
    [InlineData("""{"odpis":{"naglowekA":{"dataCzasOdpisu":"01.01.0001 00:00:00"}}}""")] // before year 1 in UTC
    public async Task Malformed_current_extract_is_an_api_exception_with_status_and_snippet(string body)
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetCurrentExtractAsync(KrsNumber.Parse("28860"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Equal(body, ex.ResponseSnippet);
        Assert.Contains("KRS", ex.Message, StringComparison.Ordinal);
        Assert.IsType<JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task Malformed_value_names_its_json_path()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"odpis":{"naglowekA":{"stanZDnia":"2026-09-17"}}}"""));
        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetCurrentExtractAsync(KrsNumber.Parse("28860"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("HTTP 200", ex.Message, StringComparison.Ordinal);
        Assert.Contains("2026-09-17", ex.Message, StringComparison.Ordinal);
        Assert.Contains("$.odpis.naglowekA.stanZDnia", ex.Message, StringComparison.Ordinal);
        Assert.Equal("$.odpis.naglowekA.stanZDnia", Assert.IsType<JsonException>(ex.InnerException).Path);
    }

    [Fact]
    public async Task Byte_order_mark_before_the_json_is_accepted()
    {
        var json = Encoding.UTF8.GetBytes(Fixture.Read("krs/current-P-0001268296.json"));
        var body = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(json).ToArray();
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        var result = await client.GetCurrentExtractAsync(KrsNumber.Parse("1268296"), KrsRegister.Entrepreneurs, TestContext.Current.CancellationToken);

        Assert.Equal(KrsLookupStatus.Found, result.Status);
        Assert.Equal("0001268296", result.Extract!.NaglowekA!.NumerKrs);
    }

    [Fact]
    public async Task Malformed_full_extract_is_an_api_exception()
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"odpis":{"naglowekP":{"wpis":[{"dataWpisu":"12/08/2022"}]}}}"""));
        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetFullExtractAsync(KrsNumber.Parse("106150"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Contains("12/08/2022", ex.ResponseSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Long_malformed_body_is_cut_to_512_characters()
    {
        var body = "<html>" + new string('x', 5000);
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetCurrentExtractAsync(KrsNumber.Parse("28860"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(body.Substring(0, 512), ex.ResponseSnippet);
    }

    [Theory]
    [InlineData("""{"odpis":null}""")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task Success_without_an_extract_is_an_api_exception_with_snippet(string body)
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetCurrentExtractAsync(KrsNumber.Parse("28860"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Equal(body, ex.ResponseSnippet);
        Assert.Contains("without an extract", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"numery":["0000028860"]}""")]
    [InlineData("""["0000028860",""")]
    public async Task Malformed_change_feed_is_an_api_exception(string body)
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(async () =>
        {
            await foreach (var _ in client.GetChangedAsync(new DateOnly(2026, 9, 22), TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Equal(body, ex.ResponseSnippet);
    }

    // Not errors, as documented on GetChangedAsync: JSON null reads as a day without changes, and array items that are
    // not KRS numbers (null included) are skipped.
    [Theory]
    [InlineData("null", "")]
    [InlineData("[]", "")]
    [InlineData("""["0000028860",null,"abc","28860"]""", "0000028860")]
    public async Task Null_change_feed_is_no_changes_and_other_items_are_skipped(string body, string expected)
    {
        var (client, _) = Create(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        var daily = new List<string>();
        await foreach (var krs in client.GetChangedAsync(new DateOnly(2026, 9, 22), TestContext.Current.CancellationToken))
        {
            daily.Add(krs.ToString());
        }

        var hourly = new List<string>();
        await foreach (var krs in client.GetChangedAsync(new DateOnly(2026, 9, 22), 10, 11, TestContext.Current.CancellationToken))
        {
            hourly.Add(krs.ToString());
        }

        Assert.Equal(expected, string.Join(",", daily));
        Assert.Equal(expected, string.Join(",", hourly));
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
