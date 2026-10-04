using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

public class RegonClientDetailsTests
{
    private const string GusRegon = "000331501";
    private const string LocalUnitRegon = "14726216300027";
    private const string NaturalRegon = "999999990";

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero));

    private static RegonClientOptions TestOptions() => new() { Environment = RegonEnvironment.Test, RateLimit = RegonRateLimit.Off };

    private static RegonClient CreateClient(FakeBirServer server, FakeTimeProvider? clock = null) =>
        new(new HttpClient(server.Handler), TestOptions(), clock ?? Clock());

    // GUS itself: the recorded search row (type P, silo 6) and the synthetic BIR12OsPrawna report.
    private static FakeBirServer LegalPersonServer() => new FakeBirServer()
        .Search("Regon", GusRegon, "search-regon-gus")
        .Report(GusRegon, "BIR12OsPrawna", "synthetic-report-osprawna-gus");

    // A sole trader with two silos (CEIDG and agricultural), as the hand-made fixtures describe (research-gus-bir/api-facts.md 5.3 and 5.4, rows 1 to 3).
    private static FakeBirServer NaturalPersonServer() => new FakeBirServer()
        .Search("Regon", NaturalRegon, "synthetic-search-natural-person")
        .Report(NaturalRegon, "BIR12OsFizycznaDaneOgolne", "synthetic-report-fiz-daneogolne")
        .Report(NaturalRegon, "BIR12OsFizycznaDzialalnoscCeidg", "synthetic-report-fiz-ceidg")
        .Report(NaturalRegon, "BIR12OsFizycznaDzialalnoscRolnicza", "synthetic-report-fiz-rolnicza");

    // A local unit of a legal person whose BIR121 report the (fake) service does not know: ErrorCode 5 (api-facts.md 5.4 row 13a and 8.1).
    private static FakeBirServer LocalUnitWithoutBir121Server() => new FakeBirServer()
        .Search("Regon", LocalUnitRegon, "synthetic-search-local-unit-lp")
        .Report(LocalUnitRegon, "BIR121JednLokalnaOsPrawnej", "synthetic-report-error-5")
        .Respond(call => call.Arg("pNazwaRaportu") == "BIR12JednLokalnaOsPrawnej"
            ? RegonTestResponses.Response(
                HttpStatusCode.OK,
                RegonTestResponses.Mtom(
                    "DanePobierzPelnyRaport",
                    "<root><dane><lokpraw_regon14>14726216300027</lokpraw_regon14><lokpraw_nazwa>ODDZIAŁ TESTOWY W WARSZAWIE</lokpraw_nazwa></dane></root>"))
            : null);

    private static string[] ReportsRequested(FakeBirServer server) =>
        server.Calls.Where(c => c.Operation == "DanePobierzPelnyRaport").Select(c => c.Arg("pNazwaRaportu")!).ToArray();

    [Fact]
    public async Task Details_of_a_legal_person_cost_login_search_and_report()
    {
        var server = LegalPersonServer();
        var client = CreateClient(server);

        var result = await client.GetDetailsAsync(Regon.Parse(GusRegon), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Zaloguj", "DaneSzukajPodmioty", "DanePobierzPelnyRaport"], server.Calls.Select(c => c.Operation));
        var details = Assert.Single(result.Value);
        Assert.Equal(RegonEnvironment.Test, result.Environment);
        Assert.Equal(RegonEntityType.LegalPerson, details.Type);
        Assert.Equal(RegonSilo.LegalPerson, details.Silo);
        Assert.Equal(Regon.Parse(GusRegon), details.Regon);
        Assert.Equal(Nip.Parse("5261040828"), details.Nip);
        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", details.Name);
        Assert.Equal("125", details.LegalFormCode);
        Assert.Equal("00-925", details.Address.PostalCode);
        Assert.Null(details.Pkd);
        Assert.Null(details.LocalUnits);
        Assert.Null(details.Partners);
    }

    [Fact]
    public async Task Details_send_the_regon_length_the_report_needs()
    {
        var legalPerson = LegalPersonServer();
        await CreateClient(legalPerson).GetDetailsAsync(Regon.Parse(GusRegon), cancellationToken: TestContext.Current.CancellationToken);

        // A local unit is found by its 14 digits; its report takes the 14 digits.
        var localUnit = new FakeBirServer()
            .Search("Regon", LocalUnitRegon, "synthetic-search-local-unit-lp")
            .Report(LocalUnitRegon, "BIR121JednLokalnaOsPrawnej", "synthetic-report-jednlokalna-lp-121");
        await CreateClient(localUnit).GetDetailsAsync(Regon.Parse(LocalUnitRegon), cancellationToken: TestContext.Current.CancellationToken);

        // The 9-digit base number of that unit goes to the reports of the legal person.
        var fromEntity = new FakeBirServer().Report("147262163", "BIR12OsPrawna", "synthetic-report-osprawna-gus");
        var unitEntity = new RegonEntity { Regon = Regon.Parse(LocalUnitRegon), Type = RegonEntityType.LegalPerson, Silo = RegonSilo.LegalPerson };
        await CreateClient(fromEntity).GetDetailsAsync(unitEntity, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([GusRegon], legalPerson.Calls.Where(c => c.Operation == "DanePobierzPelnyRaport").Select(c => c.Arg("pRegon")));
        Assert.Equal([LocalUnitRegon], localUnit.Calls.Where(c => c.Operation == "DanePobierzPelnyRaport").Select(c => c.Arg("pRegon")));
        Assert.Equal(["147262163"], fromEntity.Calls.Where(c => c.Operation == "DanePobierzPelnyRaport").Select(c => c.Arg("pRegon")));
    }

    [Fact]
    public async Task A_natural_person_with_two_silos_returns_two_details_sharing_one_general_report()
    {
        var server = NaturalPersonServer();
        var client = CreateClient(server);

        var result = await client.GetDetailsAsync(Regon.Parse(NaturalRegon), cancellationToken: TestContext.Current.CancellationToken);

        // login, search, general report (once), CEIDG activity, agricultural activity
        Assert.Equal(5, server.Calls.Count);
        Assert.Equal(
            ["BIR12OsFizycznaDaneOgolne", "BIR12OsFizycznaDzialalnoscCeidg", "BIR12OsFizycznaDzialalnoscRolnicza"],
            ReportsRequested(server));
        Assert.Equal(2, result.Value.Count);

        var ceidg = result.Value[0];
        Assert.Equal(RegonEntityType.NaturalPerson, ceidg.Type);
        Assert.Equal(RegonSilo.Ceidg, ceidg.Silo);
        Assert.Equal("JAN KOWALSKI", ceidg.Name);
        Assert.Equal(Regon.Parse(NaturalRegon), ceidg.Regon);
        Assert.Equal(Nip.Parse("9999999999"), ceidg.Nip);
        Assert.Equal("ul. Test-Prosta", ceidg.Address.Street);
        Assert.Equal(new DateOnly(2005, 3, 1), ceidg.ActivityStartDate);
        Assert.Equal(new DateOnly(2012, 5, 1), ceidg.SuspensionDate);
        Assert.Equal(new DateOnly(2012, 9, 1), ceidg.ResumptionDate);
        Assert.True(ceidg.HasCeidgActivity);
        Assert.False(ceidg.HasOtherActivity);
        Assert.Equal(1, ceidg.LocalUnitCount);

        var agricultural = result.Value[1];
        Assert.Equal(RegonSilo.Agricultural, agricultural.Silo);
        Assert.Equal("Piaseczno", agricultural.Address.City);
        Assert.Equal("05-500", agricultural.Address.PostalCode);
        Assert.Equal(new DateOnly(2020, 12, 31), agricultural.EndDate);
        Assert.Equal(new DateOnly(2020, 12, 31), agricultural.RegonRemovalDate);   // from the misspelt fiz_dataSkresleniaDzialalanosciZRegon
    }

    [Fact]
    public async Task Include_pkd_adds_one_request_and_fills_the_list()
    {
        var server = LegalPersonServer().Report(GusRegon, "BIR12OsPrawnaPkd", "synthetic-report-osprawna-pkd");
        var client = CreateClient(server);

        var result = await client.GetDetailsAsync(Regon.Parse(GusRegon), RegonInclude.Pkd, TestContext.Current.CancellationToken);

        Assert.Equal(4, server.Calls.Count);
        var pkd = Assert.Single(result.Value).Pkd;
        Assert.NotNull(pkd);
        Assert.Equal(["8411Z", "7220Z", "6201Z"], pkd.Select(p => p.Code));
        Assert.Equal([true, false, false], pkd.Select(p => p.IsPrimary));
        Assert.Equal(["2007", "2007", "2025"], pkd.Select(p => p.Version));
        Assert.All(pkd, p => Assert.Null(p.Silo));
    }

    [Fact]
    public async Task Include_pkd_is_filtered_by_silo_for_natural_persons()
    {
        var server = NaturalPersonServer().Report(NaturalRegon, "BIR12OsFizycznaPkd", "synthetic-report-fiz-pkd");
        var client = CreateClient(server);

        var result = await client.GetDetailsAsync(Regon.Parse(NaturalRegon), RegonInclude.Pkd, TestContext.Current.CancellationToken);

        // The PKD report lists every silo (api-facts.md 5.4, row 6) and is fetched once for both rows.
        Assert.Equal(
            ["BIR12OsFizycznaDaneOgolne", "BIR12OsFizycznaDzialalnoscCeidg", "BIR12OsFizycznaPkd", "BIR12OsFizycznaDzialalnoscRolnicza"],
            ReportsRequested(server));
        Assert.Equal(["6201Z"], result.Value[0].Pkd!.Select(p => p.Code));
        Assert.Equal(RegonSilo.Ceidg, Assert.Single(result.Value[0].Pkd!).Silo);
        Assert.Equal(["0111Z"], result.Value[1].Pkd!.Select(p => p.Code));
    }

    [Fact]
    public async Task Include_local_units_for_a_legal_person()
    {
        var server = LegalPersonServer().Report(GusRegon, "BIR12OsPrawnaListaJednLokalnych", "synthetic-report-osprawna-lista-jednostek");
        var client = CreateClient(server);

        var result = await client.GetDetailsAsync(Regon.Parse(GusRegon), RegonInclude.LocalUnits, TestContext.Current.CancellationToken);

        var units = Assert.Single(result.Value).LocalUnits;
        Assert.NotNull(units);
        Assert.Equal(2, units.Count);
        Assert.Equal(Regon.Parse("00033150100017"), units[0].Regon);
        Assert.Equal("ODDZIAŁ TESTOWY 1", units[0].Name);
        Assert.Equal(new DateOnly(1990, 1, 2), units[0].FormationDate);
        Assert.Equal("00-925", units[0].Address.PostalCode);
        Assert.Null(units[0].EndDate);
        Assert.Equal("Gdańsk", units[1].Address.City);
        Assert.Equal("80-001", units[1].Address.PostalCode);
        Assert.Equal(new DateOnly(2020, 1, 31), units[1].EndDate);
    }

    [Fact]
    public async Task Include_partners_tolerates_not_a_civil_partnership()
    {
        // ErrorCode 21 ("not a civil partnership", api-facts.md 8.1) means this entity has no partners: an empty list, not an error.
        var server = LegalPersonServer().Report(GusRegon, "BIR12OsPrawnaSpCywilnaWspolnicy", "synthetic-report-error-21");
        var client = CreateClient(server);

        var result = await client.GetDetailsAsync(Regon.Parse(GusRegon), RegonInclude.Partners, TestContext.Current.CancellationToken);

        var partners = Assert.Single(result.Value).Partners;
        Assert.NotNull(partners);
        Assert.Empty(partners);
    }

    [Fact]
    public async Task Pkd_of_a_removed_entity_is_empty_not_an_error()
    {
        var server = LegalPersonServer().Report(GusRegon, "BIR12OsPrawnaPkd", "synthetic-report-error-11");
        var client = CreateClient(server);

        var result = await client.GetDetailsAsync(Regon.Parse(GusRegon), RegonInclude.Pkd, TestContext.Current.CancellationToken);

        var pkd = Assert.Single(result.Value).Pkd;
        Assert.NotNull(pkd);
        Assert.Empty(pkd);
    }

    [Fact]
    public async Task A_local_unit_prefers_bir121_and_falls_back_on_error_5()
    {
        // The service knows BIR121: one report request, and the NIP of the local unit comes with it.
        var known = new FakeBirServer()
            .Search("Regon", LocalUnitRegon, "synthetic-search-local-unit-lp")
            .Report(LocalUnitRegon, "BIR121JednLokalnaOsPrawnej", "synthetic-report-jednlokalna-lp-121");
        var knownSession = new BirSession(Clock(), TimeSpan.FromMinutes(30));
        var knownClient = new RegonClient(new HttpClient(known.Handler), TestOptions(), Clock(), knownSession, new RegonRateLimiter(Clock(), RegonRateLimit.Off, TimeSpan.Zero));

        var withBir121 = Assert.Single((await knownClient.GetDetailsAsync(Regon.Parse(LocalUnitRegon), cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.Equal(["BIR121JednLokalnaOsPrawnej"], ReportsRequested(known));
        Assert.Equal(RegonEntityType.LegalPersonLocalUnit, withBir121.Type);
        Assert.Equal(Regon.Parse(LocalUnitRegon), withBir121.Regon);
        Assert.Equal(Nip.Parse("5260251049"), withBir121.Nip);
        Assert.Equal("0000135115", withBir121.RegisterNumber);
        Assert.True(knownSession.Bir121Supported);

        // GUS answers ErrorCode 5: the client asks for the older report and remembers that BIR121 is not served.
        var unknown = LocalUnitWithoutBir121Server();
        var unknownSession = new BirSession(Clock(), TimeSpan.FromMinutes(30));
        var unknownClient = new RegonClient(new HttpClient(unknown.Handler), TestOptions(), Clock(), unknownSession, new RegonRateLimiter(Clock(), RegonRateLimit.Off, TimeSpan.Zero));

        var withFallback = Assert.Single((await unknownClient.GetDetailsAsync(Regon.Parse(LocalUnitRegon), cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.Equal(["BIR121JednLokalnaOsPrawnej", "BIR12JednLokalnaOsPrawnej"], ReportsRequested(unknown));
        Assert.Equal("ODDZIAŁ TESTOWY W WARSZAWIE", withFallback.Name);
        Assert.Equal(Regon.Parse(LocalUnitRegon), withFallback.Regon);
        Assert.Null(withFallback.Nip);
        Assert.False(unknownSession.Bir121Supported);
    }

    [Fact]
    public async Task Bir121_unsupported_is_remembered_across_clients_sharing_the_session()
    {
        var server = LocalUnitWithoutBir121Server();
        var clock = Clock();
        var session = new BirSession(clock, TimeSpan.FromMinutes(30));
        var limiter = new RegonRateLimiter(clock, RegonRateLimit.Off, TimeSpan.Zero);
        var first = new RegonClient(new HttpClient(server.Handler), TestOptions(), clock, session, limiter);
        var second = new RegonClient(new HttpClient(server.Handler), TestOptions(), clock, session, limiter);

        await first.GetDetailsAsync(Regon.Parse(LocalUnitRegon), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["BIR121JednLokalnaOsPrawnej", "BIR12JednLokalnaOsPrawnej"], ReportsRequested(server));

        await second.GetDetailsAsync(Regon.Parse(LocalUnitRegon), cancellationToken: TestContext.Current.CancellationToken);

        // The second client goes straight to the older report: one more request, not two.
        Assert.Equal(
            ["BIR121JednLokalnaOsPrawnej", "BIR12JednLokalnaOsPrawnej", "BIR12JednLokalnaOsPrawnej"],
            ReportsRequested(server));
        Assert.Equal(1, server.Count("Zaloguj"));
    }

    [Fact]
    public async Task Details_from_an_entity_skip_the_search()
    {
        var server = new FakeBirServer().Report(GusRegon, "BIR12OsPrawna", "synthetic-report-osprawna-gus");
        var client = CreateClient(server);
        var entity = new RegonEntity { Regon = Regon.Parse(GusRegon), Type = RegonEntityType.LegalPerson, Silo = RegonSilo.LegalPerson, Name = "GŁÓWNY URZĄD STATYSTYCZNY" };

        var result = await client.GetDetailsAsync(entity, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Zaloguj", "DanePobierzPelnyRaport"], server.Calls.Select(c => c.Operation));
        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", Assert.Single(result.Value).Name);
    }

    [Fact]
    public async Task Details_not_found_returns_an_empty_list()
    {
        var server = new FakeBirServer().SearchNotFound("Regon", GusRegon);
        var client = CreateClient(server);

        var result = await client.GetDetailsAsync(Regon.Parse(GusRegon), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Value);
        Assert.Equal(["Zaloguj", "DaneSzukajPodmioty"], server.Calls.Select(c => c.Operation));
    }

    [Fact]
    public async Task Details_entity_without_a_regon_throws()
    {
        var server = new FakeBirServer();
        var client = CreateClient(server);
        var ct = TestContext.Current.CancellationToken;

        var noRegon = new RegonEntity { Type = RegonEntityType.LegalPerson };
        var unknownType = new RegonEntity { Regon = Regon.Parse(GusRegon) };

        Assert.Equal("entity", (await Assert.ThrowsAsync<ArgumentException>(() => client.GetDetailsAsync(noRegon, cancellationToken: ct))).ParamName);
        Assert.Equal("entity", (await Assert.ThrowsAsync<ArgumentException>(() => client.GetDetailsAsync(unknownType, cancellationToken: ct))).ParamName);
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetDetailsAsync((RegonEntity)null!, cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetDetailsAsync(default(Regon), cancellationToken: ct));
        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task Include_not_applicable_leaves_the_property_null()
    {
        var server = new FakeBirServer()
            .Search("Regon", "99999999000026", "synthetic-search-local-unit-lf")
            .Report("99999999000026", "BIR12JednLokalnaOsFizycznej", "synthetic-report-jednlokalna-lf");
        var client = CreateClient(server);

        var result = await client.GetDetailsAsync(Regon.Parse("99999999000026"), RegonInclude.LocalUnits | RegonInclude.Partners, TestContext.Current.CancellationToken);

        var details = Assert.Single(result.Value);
        Assert.Equal(RegonEntityType.NaturalPersonLocalUnit, details.Type);
        Assert.Equal("JAN KOWALSKI", details.Name);
        Assert.Equal(Regon.Parse("99999999000026"), details.Regon);
        Assert.Equal("654321", details.RegisterNumber);
        Assert.False(details.ActivityNotStarted);
        Assert.Null(details.LocalUnits);
        Assert.Null(details.Partners);
        Assert.Equal(["BIR12JednLokalnaOsFizycznej"], ReportsRequested(server));
    }

    [Fact]
    public async Task GetReport_returns_one_record_per_row()
    {
        var server = new FakeBirServer().Report(GusRegon, "BIR12OsPrawnaPkd", "synthetic-report-osprawna-pkd");
        var client = CreateClient(server);

        var result = await client.GetReportAsync(Regon.Parse(GusRegon), RegonReport.OsPrawnaPkd, TestContext.Current.CancellationToken);

        Assert.Equal(["Zaloguj", "DanePobierzPelnyRaport"], server.Calls.Select(c => c.Operation));
        Assert.Equal(RegonEnvironment.Test, result.Environment);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal(["8411Z", "7220Z", "6201Z"], result.Value.Select(r => r["praw_pkdKod"]));
        Assert.Equal("Działalność testowa A", result.Value[0]["praw_pkdNazwa"]);
        Assert.Equal("2025", result.Value[2]["praw_pkdWersja"]);
        Assert.Null(result.Value[0]["praw_nip"]);
        Assert.Equal(4, result.Value[0].Fields.Count);
    }

    [Theory]
    [InlineData("14726216300027", RegonReport.OsPrawna)]
    [InlineData("14726216300027", RegonReport.OsFizycznaPkd)]
    [InlineData("000331501", RegonReport.JednLokalnaOsPrawnej)]
    [InlineData("000331501", RegonReport.JednLokalnaOsFizycznejPkd)]
    [InlineData(null, RegonReport.OsPrawna)]
    public async Task GetReport_validates_the_regon_length(string? regon, RegonReport report)
    {
        var server = new FakeBirServer();
        var client = CreateClient(server);
        var value = regon is null ? default : Regon.Parse(regon);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => client.GetReportAsync(value, report, TestContext.Current.CancellationToken));

        Assert.Equal("regon", exception.ParamName);
        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task GetReport_unknown_regon_returns_an_empty_list()
    {
        var server = new FakeBirServer().Report(GusRegon, "BIR12OsPrawna", "synthetic-report-error-4");
        var client = CreateClient(server);

        var result = await client.GetReportAsync(Regon.Parse(GusRegon), RegonReport.OsPrawna, TestContext.Current.CancellationToken);

        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetReport_invalid_report_name_throws_with_error_code_5()
    {
        var server = new FakeBirServer().Report(GusRegon, "BIR12OsPrawna", "synthetic-report-error-5");
        var client = CreateClient(server);

        var exception = await Assert.ThrowsAsync<PolishOpenDataApiException>(() => client.GetReportAsync(Regon.Parse(GusRegon), RegonReport.OsPrawna, TestContext.Current.CancellationToken));

        Assert.Equal("5", exception.ErrorCode);
        Assert.False(exception.IsTransient);
    }

    [Fact]
    public async Task GetDataDate_parses_and_fills_data_as_of_of_later_results()
    {
        var server = new FakeBirServer()
            .Search("Nip", "5261040828", "search-regon-gus")
            .StanDanych(Fixture.Read("regon/synthetic-getvalue-standanych.txt").Trim());
        var clock = Clock();
        var session = new BirSession(clock, TimeSpan.FromMinutes(30));
        var limiter = new RegonRateLimiter(clock, RegonRateLimit.Off, TimeSpan.Zero);
        var client = new RegonClient(new HttpClient(server.Handler), TestOptions(), clock, session, limiter);
        var other = new RegonClient(new HttpClient(server.Handler), TestOptions(), clock, session, limiter);
        var nip = Nip.Parse("5261040828");

        var before = await client.FindByNipAsync(nip, TestContext.Current.CancellationToken);
        var date = await client.GetDataDateAsync(TestContext.Current.CancellationToken);
        var after = await client.FindByNipAsync(nip, TestContext.Current.CancellationToken);
        var fromOther = await other.FindByNipAsync(nip, TestContext.Current.CancellationToken);

        Assert.Null(before.DataAsOf);
        Assert.Equal(new DateOnly(2018, 10, 19), date.Value);
        Assert.Equal(new DateOnly(2018, 10, 19), date.DataAsOf);
        Assert.Equal(RegonEnvironment.Test, date.Environment);
        Assert.Equal(new DateOnly(2018, 10, 19), after.DataAsOf);
        Assert.Equal(new DateOnly(2018, 10, 19), fromOther.DataAsOf);   // the date is cached with the shared session
        var getValue = Assert.Single(server.Calls, c => c.Operation == "GetValue");
        Assert.Equal("StanDanych", getValue.Arg("pNazwaParametru"));
    }

    [Fact]
    public async Task GetDataDate_unparseable_returns_null()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus").StanDanych("not a date");
        var client = CreateClient(server);

        var date = await client.GetDataDateAsync(TestContext.Current.CancellationToken);
        var later = await client.FindByNipAsync(Nip.Parse("5261040828"), TestContext.Current.CancellationToken);

        Assert.Null(date.Value);
        Assert.Null(date.DataAsOf);
        Assert.Null(later.DataAsOf);
    }

    [Fact]
    public async Task Logout_sends_wyloguj_and_the_next_call_logs_in_again()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus");
        var client = CreateClient(server);
        var nip = Nip.Parse("5261040828");

        await client.FindByNipAsync(nip, TestContext.Current.CancellationToken);
        await client.LogoutAsync(TestContext.Current.CancellationToken);
        await client.FindByNipAsync(nip, TestContext.Current.CancellationToken);

        Assert.Equal(["Zaloguj", "DaneSzukajPodmioty", "Wyloguj", "Zaloguj", "DaneSzukajPodmioty"], server.Calls.Select(c => c.Operation));
        Assert.Equal(FakeBirServer.FirstSid, server.Calls[2].Arg("pIdentyfikatorSesji"));
        Assert.Equal(FakeBirServer.FirstSid, server.Calls[1].Sid);
        Assert.Equal("sidsidsidsid00000002", server.Calls[4].Sid);

        await client.LogoutAsync(TestContext.Current.CancellationToken);
        await client.LogoutAsync(TestContext.Current.CancellationToken);   // nothing to end the second time
        Assert.Equal(2, server.Count("Wyloguj"));
    }
}
