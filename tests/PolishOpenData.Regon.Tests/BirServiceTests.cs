using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.Bir.Wire;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

public class BirServiceTests
{
    private const string Key = "dummy-key-not-a-secret";
    private const string SecondSid = "sidsidsidsid00000002";
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    // The service over the fake BIR server, with a frozen clock and, unless a test says otherwise, no local limit.
    private sealed class Harness : IDisposable
    {
        public Harness(FakeBirServer server, RegonRateLimit mode = RegonRateLimit.Off, TimeSpan? maxWait = null)
        {
            Server = server;
            Clock = new FakeTimeProvider(Start);
            Session = new BirSession(Clock, TimeSpan.FromMinutes(30));
            Limiter = new RegonRateLimiter(Clock, mode, maxWait ?? TimeSpan.FromSeconds(5));
            Http = new HttpClient(server.Handler);
            Service = new BirService(new BirTransport(Http), RegonClientOptions.TestAddress, Key, Session, Limiter);
        }

        public FakeBirServer Server { get; }

        public FakeTimeProvider Clock { get; }

        public BirSession Session { get; }

        public RegonRateLimiter Limiter { get; }

        public HttpClient Http { get; }

        public BirService Service { get; }

        public void Dispose()
        {
            Http.Dispose();
            Session.Dispose();
        }
    }

    private static string[] Operations(FakeBirServer server) => server.Calls.Select(c => c.Operation).ToArray();

    private static HttpResponseMessage EmptyResult(string operation) =>
        RegonTestResponses.Response(HttpStatusCode.OK, RegonTestResponses.Mtom(operation, null));

    [Fact]
    public async Task Search_logs_in_once_and_sends_the_sid_header()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus");
        using var harness = new Harness(server);
        var ct = TestContext.Current.CancellationToken;

        var first = await harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct);
        var second = await harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct);

        Assert.Single(first);
        Assert.Equal("000331501", first[0]["Regon"]);
        Assert.Single(second);
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty", "DaneSzukajPodmioty" }, Operations(server));
        Assert.Null(server.Calls[0].Sid);
        Assert.Equal(Key, server.Calls[0].Arg("pKluczUzytkownika"));
        Assert.Equal(FakeBirServer.FirstSid, server.Calls[1].Sid);
        Assert.Equal(FakeBirServer.FirstSid, server.Calls[2].Sid);
        Assert.Equal(FakeBirServer.FirstSid, server.Handler.Requests[1].Sid);
        Assert.Equal("5261040828", server.Calls[1].Arg("Nip"));
    }

    [Fact]
    public async Task Empty_result_triggers_exactly_one_login_and_one_retry()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus");
        using var harness = new Harness(server);
        var ct = TestContext.Current.CancellationToken;
        await harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct);
        server.ExpireSessions();

        var rows = await harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct);

        Assert.Single(rows);
        Assert.Equal(
            new[] { "Zaloguj", "DaneSzukajPodmioty", "DaneSzukajPodmioty", "Zaloguj", "DaneSzukajPodmioty" },
            Operations(server));
        Assert.Equal(FakeBirServer.FirstSid, server.Calls[2].Sid);
        Assert.Equal(SecondSid, server.Calls[4].Sid);
        Assert.Equal(SecondSid, harness.Session.CurrentSid);
    }

    [Fact]
    public async Task Second_empty_result_means_not_found_for_a_search()
    {
        // search-empty is the recorded <DaneSzukajPodmiotyResult/>: empty even with a valid session.
        var server = new FakeBirServer().Search("Nip", "0000000000", "search-empty");
        using var harness = new Harness(server);

        var rows = await harness.Service.SearchAsync(BirSearchField.Nip, "0000000000", 1, TestContext.Current.CancellationToken);

        Assert.Empty(rows);
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty", "Zaloguj", "DaneSzukajPodmioty" }, Operations(server));
    }

    [Fact]
    public async Task Second_empty_report_result_throws()
    {
        var server = new FakeBirServer().Respond(call =>
            string.Equals(call.Operation, "DanePobierzPelnyRaport", StringComparison.Ordinal) ? EmptyResult("DanePobierzPelnyRaport") : null);
        using var harness = new Harness(server);

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() =>
            harness.Service.ReportAsync("000331501", "BIR12OsPrawna", TestContext.Current.CancellationToken));

        Assert.Contains("twice", ex.Message, StringComparison.Ordinal);
        Assert.Contains("BIR12OsPrawna", ex.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "Zaloguj", "DanePobierzPelnyRaport", "Zaloguj", "DanePobierzPelnyRaport" }, Operations(server));
    }

    [Fact]
    public async Task Rejected_key_throws_the_authentication_exception_and_is_not_retried()
    {
        var server = new FakeBirServer().RejectKey();
        using var harness = new Harness(server);

        var ex = await Assert.ThrowsAsync<RegonAuthenticationException>(() =>
            harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, TestContext.Current.CancellationToken));

        Assert.IsAssignableFrom<PolishOpenDataApiException>(ex);
        Assert.DoesNotContain(Key, ex.ToString(), StringComparison.Ordinal);
        Assert.Equal(new[] { "Zaloguj" }, Operations(server));
        Assert.Null(harness.Session.CurrentSid);
    }

    [Fact]
    public async Task Error_code_4_is_not_found()
    {
        var server = new FakeBirServer().Search("Nip", "0000000000", "synthetic-search-notfound");
        using var harness = new Harness(server);

        var rows = await harness.Service.SearchAsync(BirSearchField.Nip, "0000000000", 1, TestContext.Current.CancellationToken);

        Assert.Empty(rows);
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty" }, Operations(server));
    }

    [Fact]
    public async Task Error_code_5_throws_with_the_error_code()
    {
        var server = new FakeBirServer().Report("14726216300027", "BIR121JednLokalnaOsPrawnej", "synthetic-report-error-5");
        using var harness = new Harness(server);

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() =>
            harness.Service.ReportAsync("14726216300027", "BIR121JednLokalnaOsPrawnej", TestContext.Current.CancellationToken));

        Assert.Equal("5", ex.ErrorCode);
        Assert.False(ex.IsTransient);
        Assert.StartsWith("GUS REGON reported error 5:", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.ResponseSnippet);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(11)]
    [InlineData(21)]
    [InlineData(22)]
    public async Task Tolerated_report_codes_return_an_empty_list(int code)
    {
        var fixture = "synthetic-report-error-" + code.ToString(CultureInfo.InvariantCulture);
        var server = new FakeBirServer().Report("000331501", "BIR12OsPrawnaPkd", fixture);
        using var harness = new Harness(server);

        var rows = await harness.Service.ReportAsync("000331501", "BIR12OsPrawnaPkd", TestContext.Current.CancellationToken);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Code_101_is_transient()
    {
        // Invented answer: only the code matters (api-facts.md section 9 names 101 as GUS's "try again in a few minutes" guard).
        const string inner = "<root><dane><ErrorCode>101</ErrorCode><ErrorMessageEn>Try again in a few minutes.</ErrorMessageEn></dane></root>";
        var server = new FakeBirServer().Respond(call =>
            string.Equals(call.Operation, "DaneSzukajPodmioty", StringComparison.Ordinal)
                ? RegonTestResponses.Response(HttpStatusCode.OK, RegonTestResponses.Mtom("DaneSzukajPodmioty", inner))
                : null);
        using var harness = new Harness(server);

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() =>
            harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, TestContext.Current.CancellationToken));

        Assert.Equal("101", ex.ErrorCode);
        Assert.True(ex.IsTransient);
        Assert.StartsWith("GUS REGON reported error 101:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_weight_is_the_number_of_ids()
    {
        var server = new FakeBirServer().Search("Nipy", "5261040828,1234563218,7740001454", "synthetic-search-batch-nips");
        using var harness = new Harness(server, RegonRateLimit.Strictest);

        var rows = await harness.Service.SearchAsync(BirSearchField.Nipy, "5261040828,1234563218,7740001454", 3, TestContext.Current.CancellationToken);

        Assert.Equal(2, rows.Count);
        Assert.Equal(1 + 3, harness.Limiter.WeightInLastMinute);
        Assert.Equal(2, harness.Limiter.CallsInLastSecond);
    }

    [Fact]
    public async Task Logout_sends_wyloguj_once_and_clears_the_session()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus");
        using var harness = new Harness(server);
        var ct = TestContext.Current.CancellationToken;
        await harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct);

        await harness.Service.LogoutAsync(ct);
        await harness.Service.LogoutAsync(ct);

        Assert.Equal(1, server.Count("Wyloguj"));
        var logout = server.Calls.Single(c => string.Equals(c.Operation, "Wyloguj", StringComparison.Ordinal));
        Assert.Equal(FakeBirServer.FirstSid, logout.Arg("pIdentyfikatorSesji"));
        Assert.Null(harness.Session.CurrentSid);

        await harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct);

        Assert.Equal(2, server.Count("Zaloguj"));
    }

    [Fact]
    public async Task Logout_without_a_session_sends_nothing()
    {
        using var harness = new Harness(new FakeBirServer());

        await harness.Service.LogoutAsync(TestContext.Current.CancellationToken);

        Assert.Empty(harness.Server.Handler.Requests);
        Assert.Empty(harness.Server.Calls);
    }

    [Fact]
    public async Task Logout_swallows_transport_errors()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus").Respond(call =>
        {
            if (string.Equals(call.Operation, "Wyloguj", StringComparison.Ordinal))
            {
                throw new HttpRequestException("network down");
            }

            return null;
        });
        using var harness = new Harness(server);
        var ct = TestContext.Current.CancellationToken;
        await harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct);

        await harness.Service.LogoutAsync(ct);

        Assert.Equal(1, server.Count("Wyloguj"));
        Assert.Null(harness.Session.CurrentSid);
    }

    [Fact]
    public async Task Logout_swallows_an_http_client_timeout()
    {
        // HttpClient.Timeout surfaces as TaskCanceledException while the caller's token is not cancelled
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus").Respond(call =>
            string.Equals(call.Operation, "Wyloguj", StringComparison.Ordinal) ? throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.") : null);
        using var harness = new Harness(server);
        var ct = TestContext.Current.CancellationToken;
        await harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct);

        await harness.Service.LogoutAsync(ct);

        Assert.Equal(1, server.Count("Wyloguj"));
        Assert.Null(harness.Session.CurrentSid);
    }

    [Fact]
    public async Task Logout_swallows_a_request_that_gus_rejects()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus").Respond(call =>
            string.Equals(call.Operation, "Wyloguj", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Html(HttpStatusCode.UnsupportedMediaType, "<html><body>Unsupported Media Type</body></html>")
                : null);
        using var harness = new Harness(server);
        var ct = TestContext.Current.CancellationToken;
        await harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct);

        await harness.Service.LogoutAsync(ct);

        Assert.Equal(1, server.Count("Wyloguj"));
    }

    [Fact]
    public async Task Logout_does_not_swallow_cancellation()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus");
        using var harness = new Harness(server);
        await harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Service.LogoutAsync(cts.Token));
    }

    [Fact]
    public async Task Login_and_data_calls_all_count_against_the_limiter()
    {
        var server = new FakeBirServer()
            .Search("Nip", "5261040828", "search-regon-gus")
            .Report("000331501", "BIR12OsPrawna", "synthetic-report-osprawna-gus")
            .StanDanych("19-10-2018");
        using var harness = new Harness(server, RegonRateLimit.Strictest, TimeSpan.Zero);
        var ct = TestContext.Current.CancellationToken;

        await harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct);   // login + search
        await harness.Service.ReportAsync("000331501", "BIR12OsPrawna", ct);          // report: the third request in this instant

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => harness.Service.GetValueAsync("StanDanych", ct));

        Assert.Equal(3, harness.Limiter.CallsInLastSecond);
        Assert.Equal(3, server.Calls.Count);
        Assert.NotNull(ex.RetryAfter);
    }

    [Fact]
    public async Task GetValue_returns_the_trimmed_text()
    {
        var server = new FakeBirServer().StanDanych("  19-10-2018\r\n");
        using var harness = new Harness(server);

        var text = await harness.Service.GetValueAsync("StanDanych", TestContext.Current.CancellationToken);

        Assert.Equal("19-10-2018", text);
        Assert.Equal("StanDanych", server.Calls[1].Arg("pNazwaParametru"));
    }

    [Fact]
    public async Task The_key_never_appears_in_an_exception_message()
    {
        var ct = TestContext.Current.CancellationToken;
        var failures = new System.Collections.Generic.List<Exception>();

        var rejected = new FakeBirServer().RejectKey();
        using (var harness = new Harness(rejected))
        {
            failures.Add(await Assert.ThrowsAnyAsync<Exception>(() => harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct)));
        }

        var inBand = new FakeBirServer().Report("14726216300027", "BIR121JednLokalnaOsPrawnej", "synthetic-report-error-5");
        using (var harness = new Harness(inBand))
        {
            failures.Add(await Assert.ThrowsAnyAsync<Exception>(() => harness.Service.ReportAsync("14726216300027", "BIR121JednLokalnaOsPrawnej", ct)));
        }

        var emptyTwice = new FakeBirServer().Respond(call =>
            string.Equals(call.Operation, "DanePobierzPelnyRaport", StringComparison.Ordinal) ? EmptyResult("DanePobierzPelnyRaport") : null);
        using (var harness = new Harness(emptyTwice))
        {
            failures.Add(await Assert.ThrowsAnyAsync<Exception>(() => harness.Service.ReportAsync("000331501", "BIR12OsPrawna", ct)));
        }

        var badSid = new FakeBirServer().Respond(call =>
            string.Equals(call.Operation, "Zaloguj", StringComparison.Ordinal)
                ? RegonTestResponses.Response(HttpStatusCode.OK, RegonTestResponses.Mtom("Zaloguj", "not a valid sid!"))
                : null);
        using (var harness = new Harness(badSid))
        {
            failures.Add(await Assert.ThrowsAnyAsync<Exception>(() => harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, ct)));
        }

        Assert.Equal(4, failures.Count);
        foreach (var failure in failures)
        {
            Assert.DoesNotContain(Key, failure.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(FakeBirServer.FirstSid, failure.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_session_id_that_cannot_go_into_a_header_is_refused()
    {
        var server = new FakeBirServer().Respond(call =>
            string.Equals(call.Operation, "Zaloguj", StringComparison.Ordinal)
                ? RegonTestResponses.Response(HttpStatusCode.OK, RegonTestResponses.Mtom("Zaloguj", "not a valid sid!"))
                : null);
        using var harness = new Harness(server);

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() =>
            harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, TestContext.Current.CancellationToken));

        Assert.IsNotType<RegonAuthenticationException>(ex);
        Assert.Null(harness.Session.CurrentSid);
        Assert.Equal(new[] { "Zaloguj" }, Operations(server));
    }

    [Fact]
    public async Task An_unreadable_result_text_throws_an_api_exception_with_the_xml_error_inside()
    {
        const string broken = "<root><dane><Regon>000331501</dane></root>";
        var server = new FakeBirServer().Respond(call =>
            string.Equals(call.Operation, "DaneSzukajPodmioty", StringComparison.Ordinal)
                ? RegonTestResponses.Response(HttpStatusCode.OK, RegonTestResponses.Mtom("DaneSzukajPodmioty", broken))
                : null);
        using var harness = new Harness(server);

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() =>
            harness.Service.SearchAsync(BirSearchField.Nip, "5261040828", 1, TestContext.Current.CancellationToken));

        Assert.IsAssignableFrom<XmlException>(ex.InnerException);
        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Contains("000331501", ex.ResponseSnippet, StringComparison.Ordinal);
    }
}
