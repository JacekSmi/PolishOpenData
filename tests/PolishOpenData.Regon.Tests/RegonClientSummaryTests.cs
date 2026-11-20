using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Time.Testing;

namespace PolishOpenData.Bir.Tests;

public class RegonClientSummaryTests
{
    private const string Summary = "DanePobierzRaportZbiorczy";
    private const string Entities = "BIR11AktualizowanePodmiotyPrawneOrazDzialalnosciOsFizycznych";
    private const string LocalUnits = "BIR11SkresloneJednostkiLokalne";
    private const string Yesterday = "2026-10-02";
    private const string SecondSid = "sidsidsidsid00000002";
    private static readonly DateOnly YesterdayDate = new(2026, 10, 2);
    private static readonly string[] EntityNumbers = ["000331501", "123456785", "876543216", "112233447", "556677887"];
    private static readonly string[] LocalUnitNumbers = ["12345678500010", "87654321600024", "11223344700016"];

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero));

    private static RegonClientOptions TestOptions() => new() { Environment = RegonEnvironment.Test, RateLimit = RegonRateLimit.Off };

    private static RegonClient CreateClient(FakeBirServer server, FakeTimeProvider? clock = null) =>
        new(new HttpClient(server.Handler), TestOptions(), clock ?? Clock());

    private static FakeBirServer AnswerSummary(string inner) =>
        new FakeBirServer().Respond(call => call.Operation == Summary
            ? RegonTestResponses.Response(HttpStatusCode.OK, RegonTestResponses.Mtom(Summary, inner))
            : null);

    private static string Rows(params string[] numbers) =>
        "<root>" + string.Concat(numbers.Select(n => "<dane><regon>" + n + "</regon></dane>")) + "</root>";

    private static Task<RegonResult<IReadOnlyList<Regon>>> Yesterdays(RegonClient client, RegonSummaryReport report = RegonSummaryReport.AktualizowanePodmiotyPrawneOrazDzialalnosciOsFizycznych) =>
        client.GetSummaryReportAsync(YesterdayDate, report, TestContext.Current.CancellationToken);

    // ---- mapping ----

    [Fact]
    public async Task NineDigitList_MapsToRegon_InOrder()
    {
        var server = new FakeBirServer().Summary(Yesterday, Entities, "synthetic-summary-entities");

        var result = await Yesterdays(CreateClient(server));

        Assert.Equal(EntityNumbers.Select(n => Regon.Parse(n)), result.Value);
        var call = Assert.Single(server.Calls, c => c.Operation == Summary);
        Assert.Equal(Yesterday, call.Arg("pDataRaportu"));
        Assert.Equal(Entities, call.Arg("pNazwaRaportu"));
    }

    [Fact]
    public async Task FourteenDigitList_MapsToRegon()
    {
        var server = new FakeBirServer().Summary(Yesterday, LocalUnits, "synthetic-summary-local-units");

        var result = await Yesterdays(CreateClient(server), RegonSummaryReport.SkresloneJednostkiLokalne);

        Assert.Equal(LocalUnitNumbers.Select(n => Regon.Parse(n)), result.Value);
        Assert.All(result.Value, r => Assert.Equal(14, r.Length));
    }

    [Fact]
    public async Task Provenance_EnvironmentAndRetrievedAt_DataAsOfNullUntilDataDateRead()
    {
        var clock = Clock();
        var server = new FakeBirServer().Summary(Yesterday, Entities, "synthetic-summary-entities").StanDanych("19-10-2018");
        var client = CreateClient(server, clock);

        var before = await Yesterdays(client);
        await client.GetDataDateAsync(TestContext.Current.CancellationToken);
        var after = await Yesterdays(client);

        Assert.Equal(RegonEnvironment.Test, before.Environment);
        Assert.Equal(clock.GetUtcNow(), before.RetrievedAt);
        Assert.Null(before.DataAsOf);
        Assert.Equal(new DateOnly(2018, 10, 19), after.DataAsOf);
    }

    [Fact]
    public async Task Duplicates_AreKept()
    {
        var server = AnswerSummary(Rows("123456785", "000331501", "123456785"));

        var result = await Yesterdays(CreateClient(server));

        Assert.Equal(new[] { "123456785", "000331501", "123456785" }.Select(n => Regon.Parse(n)), result.Value);
    }

    [Fact]
    public void FixtureNumbersHaveValidCheckDigits()
    {
        foreach (var (fixture, length) in new[] { ("synthetic-summary-entities", 9), ("synthetic-summary-local-units", 14) })
        {
            var numbers = XDocument.Parse(RegonTestResponses.Inner(fixture)).Descendants("regon").Select(e => e.Value).ToList();
            Assert.NotEmpty(numbers);
            Assert.All(numbers, n =>
            {
                Assert.True(Regon.TryParse(n, out var regon));
                Assert.Equal(length, regon.Length);
            });
        }
    }

    // ---- dates ----

    [Fact]
    public async Task Today_And_Tomorrow_Throw_WithZeroRequests()
    {
        var server = new FakeBirServer();
        var client = CreateClient(server);
        var ct = TestContext.Current.CancellationToken;

        foreach (var day in new[] { new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 4) })
        {
            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetSummaryReportAsync(day, RegonSummaryReport.NoweJednostkiLokalne, ct));
            Assert.Equal("day", ex.ParamName);
        }

        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task ClockAt2330Utc_WarsawIsNextDay_TodayThrows_YesterdayIsSent()
    {
        // 23:30 UTC on 2 October is 01:30 on 3 October in Warsaw (CEST): the UTC date would still allow 3 October.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 23, 30, 0, TimeSpan.Zero));
        var server = new FakeBirServer().Summary(Yesterday, Entities, "synthetic-summary-entities");
        var client = CreateClient(server, clock);
        var ct = TestContext.Current.CancellationToken;
        const RegonSummaryReport report = RegonSummaryReport.AktualizowanePodmiotyPrawneOrazDzialalnosciOsFizycznych;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetSummaryReportAsync(new DateOnly(2026, 10, 3), report, ct));
        Assert.Empty(server.Calls);

        var result = await client.GetSummaryReportAsync(YesterdayDate, report, ct);

        Assert.Equal(5, result.Value.Count);
        Assert.Equal(1, server.Count(Summary));
    }

    [Fact]
    public async Task Yesterday_30DaysBack_And_2014_11_07_AreSent()
    {
        var server = new FakeBirServer()
            .Summary("2026-10-02", LocalUnits, "synthetic-summary-local-units")
            .Summary("2026-09-03", LocalUnits, "synthetic-summary-local-units")
            .Summary("2014-11-07", LocalUnits, "synthetic-summary-local-units");
        var client = CreateClient(server);
        var ct = TestContext.Current.CancellationToken;

        foreach (var day in new[] { new DateOnly(2026, 10, 2), new DateOnly(2026, 9, 3), new DateOnly(2014, 11, 7) })
        {
            var result = await client.GetSummaryReportAsync(day, RegonSummaryReport.SkresloneJednostkiLokalne, ct);
            Assert.Equal(3, result.Value.Count);
        }

        Assert.Equal(new[] { "2026-10-02", "2026-09-03", "2014-11-07" }, server.Calls.Where(c => c.Operation == Summary).Select(c => c.Arg("pDataRaportu")));
    }

    [Fact]
    public async Task UndefinedReport_Throws_WithZeroRequests()
    {
        var server = new FakeBirServer();

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Yesterdays(CreateClient(server), (RegonSummaryReport)999));

        Assert.Equal("report", ex.ParamName);
        Assert.Empty(server.Calls);
    }

    // ---- in-band codes ----

    [Theory]
    [InlineData("synthetic-summary-error-4")]
    [InlineData("synthetic-summary-error-4-weekend")]
    public async Task Code4_PlainAndWeekendText_AreEmptyLists_OneDataRequest_NoSecondLogin(string fixture)
    {
        var server = new FakeBirServer().Summary(Yesterday, Entities, fixture);

        var result = await Yesterdays(CreateClient(server));

        Assert.Empty(result.Value);
        Assert.Equal(1, server.Count(Summary));
        Assert.Equal(1, server.Count("Zaloguj"));
        Assert.Equal(2, server.Calls.Count);
    }

    [Fact]
    public async Task Code101_IsTransient_ExactlyOneSummaryRequest()
    {
        var server = new FakeBirServer().Summary(Yesterday, Entities, "synthetic-summary-error-101");

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Yesterdays(CreateClient(server)));

        Assert.True(ex.IsTransient);
        Assert.Equal("101", ex.ErrorCode);
        Assert.Equal(1, server.Count(Summary));
        Assert.Equal(1, server.Count("Zaloguj"));
    }

    [Theory]
    [InlineData("synthetic-summary-error-102", "102")]
    [InlineData("synthetic-summary-error-103", "103")]
    [InlineData("synthetic-summary-error-104", "104")]
    [InlineData(null, "999")]
    public async Task Codes_102_103_104_AndUnknown_AreNotTransient_AndCarryTheCode(string? fixture, string code)
    {
        var server = fixture is null
            ? AnswerSummary("<root><dane><ErrorCode>999</ErrorCode><ErrorMessageEn>Something new.</ErrorMessageEn></dane></root>")
            : new FakeBirServer().Summary(Yesterday, Entities, fixture);

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Yesterdays(CreateClient(server)));

        Assert.False(ex.IsTransient);
        Assert.Equal(code, ex.ErrorCode);
        Assert.Contains("DanePobierzRaportZbiorczy", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("update the library", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, server.Count(Summary));
    }

    [Fact]
    public async Task Code105_CarriesTheUpdateTheLibraryHint()
    {
        var server = new FakeBirServer().Summary(Yesterday, Entities, "synthetic-summary-error-105");

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Yesterdays(CreateClient(server)));

        Assert.False(ex.IsTransient);
        Assert.Equal("105", ex.ErrorCode);
        Assert.EndsWith("GUS no longer accepts this report name; update the library.", ex.Message, StringComparison.Ordinal);
    }

    // ---- empty answer ----

    [Fact]
    public async Task EmptyResult_OneSummaryRequestOneLogin_NoResend_Transient()
    {
        // a live session that answers empty (the 35 s hang seen on test): the session is renewed, the request is not resent
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus").Respond(call => call.Operation == Summary
            ? RegonTestResponses.Response(HttpStatusCode.OK, RegonTestResponses.Mtom(Summary, null))
            : null);
        var client = CreateClient(server);
        await client.FindByNipAsync(Nip.Parse("5261040828"), TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Yesterdays(client));

        Assert.True(ex.IsTransient);
        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Null(ex.ErrorCode);
        Assert.Null(ex.ResponseSnippet);
        Assert.Contains("the session was renewed", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, server.Count(Summary));
        Assert.Equal(2, server.Count("Zaloguj"));   // the first login of the search plus exactly one renewal
        Assert.Equal(0, server.Count("Wyloguj"));
        Assert.Equal("Zaloguj", server.Calls[server.Calls.Count - 1].Operation);
    }

    [Fact]
    public async Task AfterEmptyResult_TheNextCallUsesTheNewSid()
    {
        // a dead session answers empty too: the fake does that after ExpireSessions
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus").Summary(Yesterday, Entities, "synthetic-summary-entities");
        var client = CreateClient(server);
        await client.FindByNipAsync(Nip.Parse("5261040828"), TestContext.Current.CancellationToken);
        server.ExpireSessions();

        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Yesterdays(client));
        var result = await Yesterdays(client);

        Assert.True(ex.IsTransient);
        Assert.Equal(5, result.Value.Count);
        var summaries = server.Calls.Where(c => c.Operation == Summary).ToList();
        Assert.Equal(2, summaries.Count);
        Assert.Equal(FakeBirServer.FirstSid, summaries[0].Sid);
        Assert.Equal(SecondSid, summaries[1].Sid);
        Assert.Equal(2, server.Count("Zaloguj"));
    }

    [Fact]
    public async Task RenewLoginFailure_PropagatesUnchanged()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus");
        var client = CreateClient(server);
        await client.FindByNipAsync(Nip.Parse("5261040828"), TestContext.Current.CancellationToken);
        server.ExpireSessions().RejectKey();

        await Assert.ThrowsAsync<RegonAuthenticationException>(() => Yesterdays(client));

        Assert.Equal(1, server.Count(Summary));
        Assert.Equal(2, server.Count("Zaloguj"));
    }

    // ---- rows ----

    [Fact]
    public async Task BadCheckDigit_Throws_NullSnippet()
    {
        var ex = await RowFailure(Rows("000331501", "123456786"));

        Assert.Null(ex.ResponseSnippet);
        Assert.Contains("could not be read", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingRegonElement_Throws_NullSnippet()
    {
        var ex = await RowFailure("<root><dane><regon>000331501</regon></dane><dane><nip>5261040828</nip></dane></root>");

        Assert.Null(ex.ResponseSnippet);
        Assert.Contains("could not be read", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreadableXml_Throws_NullSnippet()
    {
        var ex = await RowFailure("<root><dane><regon>000331501</regon></dane><dane><regon>123456785</dane></root>");

        Assert.Null(ex.ResponseSnippet);
        Assert.Contains("could not be read", ex.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<XmlException>(ex.InnerException);
    }

    [Fact]
    public async Task ErrorSnippetNeverHoldsRows()
    {
        string[] bodies =
        [
            Rows("000331501", "123456785", "876543216", "123456786"),
            "<root><dane><regon>000331501</regon></dane><dane><regon>123456785</regon></dane><dane><regon>876543216</regon></dane><dane><regon></regon></dane></root>",
            "<root><dane><regon>000331501</regon></dane><dane><regon>123456785</regon></dane><dane><regon>876543216</regon></dane><dane></root>",
        ];

        foreach (var body in bodies)
        {
            var ex = await RowFailure(body);

            Assert.Null(ex.ResponseSnippet);
            foreach (var number in new[] { "000331501", "123456785", "876543216", "123456786" })
            {
                Assert.DoesNotContain(number, ex.ToString(), StringComparison.Ordinal);
            }
        }
    }

    private static async Task<PolishOpenDataApiException> RowFailure(string inner)
    {
        var server = AnswerSummary(inner);
        var ex = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => Yesterdays(CreateClient(server)));
        Assert.False(ex.IsTransient);
        Assert.Equal(1, server.Count(Summary));
        return ex;
    }

    // ---- cancellation ----

    [Fact]
    public async Task CancelDuringSend_LeavesTheSessionUsable()
    {
        // The stub's responder is synchronous: it cancels the caller's token while "waiting" and throws as HttpClient would.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus").Respond(call =>
        {
            if (call.Operation != Summary)
            {
                return null;
            }

            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var clock = Clock();
        using var session = new BirSession(clock, TimeSpan.FromMinutes(30));
        var client = new RegonClient(new HttpClient(server.Handler), TestOptions(), clock, session, new RegonRateLimiter(clock, RegonRateLimit.Off, TimeSpan.Zero));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetSummaryReportAsync(YesterdayDate, RegonSummaryReport.NoweJednostkiLokalne, cts.Token));
        Assert.Equal(FakeBirServer.FirstSid, session.CurrentSid);

        var found = await client.FindByNipAsync(Nip.Parse("5261040828"), TestContext.Current.CancellationToken);

        Assert.Single(found.Value);
        Assert.Equal(1, server.Count("Zaloguj"));
        Assert.Equal(FakeBirServer.FirstSid, server.Calls.Single(c => c.Operation == "DaneSzukajPodmioty").Sid);
        Assert.Equal(FakeBirServer.FirstSid, session.CurrentSid);
    }
}
