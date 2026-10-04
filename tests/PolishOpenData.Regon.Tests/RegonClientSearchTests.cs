using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

public class RegonClientSearchTests
{
    private const string GusNip = "5261040828";
    private const string GusRegon = "000331501";
    private const string TestAddress = "https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc";
    private const string SearchAction = "http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/DaneSzukajPodmioty";

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero));

    private static RegonClientOptions TestOptions(RegonRateLimit limit = RegonRateLimit.Off) =>
        new() { Environment = RegonEnvironment.Test, RateLimit = limit };

    private static RegonClient CreateClient(FakeBirServer server, FakeTimeProvider? clock = null, RegonClientOptions? options = null) =>
        new(new HttpClient(server.Handler), options ?? TestOptions(), clock ?? Clock());

    // Valid NIPs for the chunking tests: 526, a six-digit counter, then the mod-11 check digit (a check value of 10 is skipped).
    private static List<Nip> ValidNips(int count)
    {
        int[] weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        var nips = new List<Nip>();
        for (var n = 100000; nips.Count < count; n++)
        {
            var digits = "526" + n.ToString(CultureInfo.InvariantCulture);
            var sum = 0;
            for (var i = 0; i < weights.Length; i++)
            {
                sum += (digits[i] - '0') * weights[i];
            }

            var check = sum % 11;
            if (check != 10)
            {
                nips.Add(Nip.Parse(digits + check.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return nips;
    }

    private static string Joined(IEnumerable<Nip> nips) => string.Join(",", nips.Select(n => n.ToString()));

    [Fact]
    public async Task Finds_by_nip_and_labels_the_environment()
    {
        var server = new FakeBirServer().Search("Nip", GusNip, "search-regon-gus");
        var client = CreateClient(server);

        var result = await client.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);

        var entity = Assert.Single(result.Value);
        Assert.Equal(RegonEnvironment.Test, result.Environment);
        Assert.Equal(Regon.Parse(GusRegon), entity.Regon);
        Assert.Equal(Nip.Parse(GusNip), entity.Nip);
        Assert.Null(entity.StatusNip);
        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", entity.Name);
        Assert.Equal(RegonEntityType.LegalPerson, entity.Type);
        Assert.Equal(RegonSilo.LegalPerson, entity.Silo);
        Assert.Equal("Warszawa", entity.Address.City);
        Assert.Equal("00-925", entity.Address.PostalCode);
        Assert.Equal("ul. Test-Krucza", entity.Address.Street);
        Assert.Equal("208", entity.Address.BuildingNumber);
        Assert.Null(entity.EndDate);
        Assert.Equal(["Zaloguj", "DaneSzukajPodmioty"], server.Calls.Select(c => c.Operation));
        Assert.Equal(GusNip, server.Calls[1].Arg("Nip"));
    }

    [Fact]
    public async Task Finds_by_regon_with_9_and_14_digits()
    {
        var server = new FakeBirServer()
            .Search("Regon", GusRegon, "search-regon-gus")
            .Search("Regon", "14726216300027", "synthetic-search-local-unit-lp");
        var client = CreateClient(server);

        var entity = Assert.Single((await client.FindByRegonAsync(Regon.Parse(GusRegon), TestContext.Current.CancellationToken)).Value);
        var unit = Assert.Single((await client.FindByRegonAsync(Regon.Parse("14726216300027"), TestContext.Current.CancellationToken)).Value);

        Assert.Equal(RegonEntityType.LegalPerson, entity.Type);
        Assert.Equal(RegonEntityType.LegalPersonLocalUnit, unit.Type);
        Assert.True(unit.Regon!.Value.IsLocalUnit);
        Assert.Equal("ODDZIAŁ TESTOWY W WARSZAWIE", unit.Name);
        Assert.Equal([GusRegon, "14726216300027"], server.Calls.Where(c => c.Operation == "DaneSzukajPodmioty").Select(c => c.Arg("Regon")));
        Assert.Equal(1, server.Count("Zaloguj"));
    }

    [Fact]
    public async Task Finds_by_krs()
    {
        // A search row carries no KRS field (research-gus-bir/api-facts.md 5.3), so the fixture is the GUS row whatever number is asked for.
        var server = new FakeBirServer().Search("Krs", "0000028860", "search-regon-gus");
        var client = CreateClient(server);

        var result = await client.FindByKrsAsync(KrsNumber.Parse("0000028860"), TestContext.Current.CancellationToken);

        Assert.Single(result.Value);
        Assert.Equal("0000028860", server.Calls[1].Arg("Krs"));
    }

    [Fact]
    public async Task A_control_character_in_a_field_does_not_fail_the_answer()
    {
        // WCF writes a control character of the inner result as a character reference in the envelope: &#x1F; on the wire.
        var wire = Encoding.UTF8.GetString(RegonTestResponses.Mtom("DaneSzukajPodmioty", RegonTestResponses.Inner("synthetic-search-control-char")));
        Assert.Contains("SPÓŁKA&#x1F;TESTOWA", wire, StringComparison.Ordinal);
        var server = new FakeBirServer().Search("Regon", "142396858", "synthetic-search-control-char");
        var client = CreateClient(server);

        var result = await client.FindByRegonAsync(Regon.Parse("142396858"), TestContext.Current.CancellationToken);

        var entity = Assert.Single(result.Value);
        Assert.Equal("SPÓŁKA\u001FTESTOWA SP. Z O.O.", entity.Name);
        Assert.Equal(Regon.Parse("142396858"), entity.Regon);
        Assert.Equal(RegonEntityType.LegalPerson, entity.Type);
        Assert.Equal("Warszawa", entity.Address.City);
        Assert.Equal("ul. Test-Domaniewska", entity.Address.Street);
        Assert.Equal("39", entity.Address.BuildingNumber);
    }

    [Fact]
    public async Task Test_environment_sends_the_published_test_key_and_never_the_configured_one()
    {
        const string ownKey = "dummy-key-not-a-secret";
        var server = new FakeBirServer().Search("Nip", GusNip, "search-regon-gus");
        var client = CreateClient(server, options: new RegonClientOptions { Environment = RegonEnvironment.Test, UserKey = ownKey, RateLimit = RegonRateLimit.Off });

        await client.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);

        var login = Assert.Single(server.Calls, c => c.Operation == "Zaloguj");
        Assert.Equal(RegonClientOptions.TestUserKey, login.Arg("pKluczUzytkownika"));
        Assert.All(server.Handler.Requests, request =>
        {
            Assert.DoesNotContain(ownKey, request.BodyText, StringComparison.Ordinal);
            Assert.DoesNotContain(ownKey, request.Uri.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(ownKey, request.ContentType ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(ownKey, request.UserAgent ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(ownKey, request.Sid ?? string.Empty, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Not_found_returns_an_empty_list()
    {
        var server = new FakeBirServer().SearchNotFound("Nip", "0000000000");
        var client = CreateClient(server);

        var result = await client.FindByNipAsync(Nip.Parse("0000000000"), TestContext.Current.CancellationToken);

        Assert.Empty(result.Value);
        Assert.Equal(RegonEnvironment.Test, result.Environment);
    }

    [Fact]
    public async Task One_nip_can_return_several_rows()
    {
        // api-facts.md 5.3: a natural person has up to three rows for one REGON, one per silo, each with its own address; the element Ulica is left out when empty.
        var server = new FakeBirServer().Search("Nip", "9999999999", "synthetic-search-natural-person");
        var client = CreateClient(server);

        var result = await client.FindByNipAsync(Nip.Parse("9999999999"), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, e => Assert.Equal(RegonEntityType.NaturalPerson, e.Type));
        Assert.All(result.Value, e => Assert.Equal("JAN KOWALSKI", e.Name));
        Assert.Equal([RegonSilo.Ceidg, RegonSilo.Agricultural], result.Value.Select(e => e.Silo));
        Assert.Equal("ul. Test-Prosta", result.Value[0].Address.Street);
        Assert.Equal("2", result.Value[0].Address.ApartmentNumber);
        Assert.Null(result.Value[1].Address.Street);
        Assert.Equal(new DateOnly(2020, 12, 31), result.Value[1].EndDate);
    }

    [Theory]
    [InlineData("nip")]
    [InlineData("regon")]
    [InlineData("krs")]
    public async Task Rejects_empty_identifiers(string kind)
    {
        var server = new FakeBirServer();
        var client = CreateClient(server);
        var ct = TestContext.Current.CancellationToken;

        var exception = kind switch
        {
            "nip" => await Assert.ThrowsAsync<ArgumentException>(() => client.FindByNipAsync(default, ct)),
            "regon" => await Assert.ThrowsAsync<ArgumentException>(() => client.FindByRegonAsync(default, ct)),
            _ => await Assert.ThrowsAsync<ArgumentException>(() => client.FindByKrsAsync(default, ct)),
        };

        Assert.Equal(kind, exception.ParamName);
        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task Batch_nips_returns_one_entry_per_requested_identifier()
    {
        // api-facts.md 5.3 [OBS call 7]: GUS answers a batch with the found entities only; an unknown NIP gets no row and no ErrorCode.
        var server = new FakeBirServer().Search("Nipy", GusNip + ",1234563218,0000000000", "synthetic-search-batch-nips");
        var client = CreateClient(server);

        var result = await client.FindByNipsAsync([Nip.Parse(GusNip), Nip.Parse("1234563218"), Nip.Parse("0000000000")], TestContext.Current.CancellationToken);

        Assert.Equal([GusNip, "1234563218", "0000000000"], result.Value.Select(e => e.Identifier));
        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", Assert.Single(result.Value[0].Entities).Name);
        Assert.Equal("PRZYKŁADOWA SPÓŁKA TESTOWA", Assert.Single(result.Value[1].Entities).Name);
        Assert.Empty(result.Value[2].Entities);
        Assert.Equal(["Zaloguj", "DaneSzukajPodmioty"], server.Calls.Select(c => c.Operation));
        Assert.Equal(GusNip + ",1234563218,0000000000", server.Calls[1].Arg("Nipy"));
    }

    [Fact]
    public async Task Batch_drops_duplicates_and_keeps_request_order()
    {
        // GUS answers in its own order (the GUS row first); the entries follow the request.
        var server = new FakeBirServer().Search("Nipy", "1234563218," + GusNip, "synthetic-search-batch-nips");
        var client = CreateClient(server);

        var result = await client.FindByNipsAsync(
            [Nip.Parse("1234563218"), Nip.Parse(GusNip), Nip.Parse("1234563218")],
            TestContext.Current.CancellationToken);

        Assert.Equal(["1234563218", GusNip], result.Value.Select(e => e.Identifier));
        Assert.Equal(Nip.Parse("1234563218"), Assert.Single(result.Value[0].Entities).Nip);
        Assert.Equal(Nip.Parse(GusNip), Assert.Single(result.Value[1].Entities).Nip);
        Assert.Equal(1, server.Count("DaneSzukajPodmioty"));
    }

    [Fact]
    public async Task Batch_chunks_by_20()
    {
        var nips = ValidNips(25);
        var server = new FakeBirServer()
            .Search("Nipy", Joined(nips.Take(20)), "synthetic-search-batch-nips")
            .Search("Nipy", Joined(nips.Skip(20)), "synthetic-search-batch-nips");
        var clock = Clock();
        var session = new BirSession(clock, TimeSpan.FromMinutes(30));
        var limiter = new RegonRateLimiter(clock, RegonRateLimit.Strictest, TimeSpan.FromSeconds(5));
        var client = new RegonClient(new HttpClient(server.Handler), TestOptions(RegonRateLimit.Strictest), clock, session, limiter);

        var result = await client.FindByNipsAsync(nips, TestContext.Current.CancellationToken);

        Assert.Equal(25, result.Value.Count);
        var searches = server.Calls.Where(c => c.Operation == "DaneSzukajPodmioty").ToList();
        Assert.Equal([20, 5], searches.Select(c => c.Arg("Nipy")!.Split(',').Length));
        Assert.Equal(1 + 20 + 5, limiter.WeightInLastMinute);
        Assert.Equal(3, limiter.CallsInLastSecond);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_with_only_unknown_ids_returns_empty_entries(bool answeredWithAnEmptyResult)
    {
        var nips = new[] { Nip.Parse("0000000000"), Nip.Parse("1234563218") };
        var server = new FakeBirServer();
        if (answeredWithAnEmptyResult)
        {
            // Unobserved at GUS (dotnet-approach.md 3.6): an empty result even with a valid session. The client logs in once more, asks once more and gives up.
            server.Respond(call => call.Operation == "DaneSzukajPodmioty"
                ? RegonTestResponses.Response(HttpStatusCode.OK, RegonTestResponses.Mtom("DaneSzukajPodmioty", null))
                : null);
        }
        else
        {
            server.SearchNotFound("Nipy", Joined(nips));
        }

        var result = await CreateClient(server).FindByNipsAsync(nips, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, entry => Assert.Empty(entry.Entities));
        Assert.Equal(answeredWithAnEmptyResult ? 2 : 1, server.Count("Zaloguj"));
    }

    [Fact]
    public async Task Batch_regons_split_by_length_and_keep_request_order()
    {
        // api-facts.md 5.3: Regony9zn and Regony14zn are separate search fields; GUS leaves the second local unit out of its answer here.
        var server = new FakeBirServer()
            .Search("Regony9zn", GusRegon + ",123456785", "synthetic-search-batch-nips")
            .Search("Regony14zn", "14726216300027,99999999000026", "synthetic-search-local-unit-lp");
        var client = CreateClient(server);

        var result = await client.FindByRegonsAsync(
            [Regon.Parse("14726216300027"), Regon.Parse(GusRegon), Regon.Parse("123456785"), Regon.Parse("99999999000026")],
            TestContext.Current.CancellationToken);

        Assert.Equal(["14726216300027", GusRegon, "123456785", "99999999000026"], result.Value.Select(e => e.Identifier));
        Assert.Equal(RegonEntityType.LegalPersonLocalUnit, Assert.Single(result.Value[0].Entities).Type);
        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", Assert.Single(result.Value[1].Entities).Name);
        Assert.Equal("PRZYKŁADOWA SPÓŁKA TESTOWA", Assert.Single(result.Value[2].Entities).Name);
        Assert.Empty(result.Value[3].Entities);
        var searches = server.Calls.Where(c => c.Operation == "DaneSzukajPodmioty").ToList();
        Assert.Equal(2, searches.Count);
        Assert.Equal(GusRegon + ",123456785", searches[0].Arg("Regony9zn"));
        Assert.Equal("14726216300027,99999999000026", searches[1].Arg("Regony14zn"));
    }

    [Fact]
    public async Task Batch_krs_numbers_are_searched_one_by_one()
    {
        var server = new FakeBirServer()
            .Search("Krs", "0000028860", "search-regon-gus")
            .SearchNotFound("Krs", "0001268296");
        var client = CreateClient(server);

        var result = await client.FindByKrsNumbersAsync([KrsNumber.Parse("0000028860"), KrsNumber.Parse("0001268296")], TestContext.Current.CancellationToken);

        Assert.Equal(["0000028860", "0001268296"], result.Value.Select(e => e.Identifier));
        Assert.Single(result.Value[0].Entities);
        Assert.Empty(result.Value[1].Entities);
        var searches = server.Calls.Where(c => c.Operation == "DaneSzukajPodmioty").ToList();
        Assert.Equal(["0000028860", "0001268296"], searches.Select(c => c.Arg("Krs")));
        Assert.All(searches, c => Assert.Null(c.Arg("Krsy")));
    }

    [Theory]
    [InlineData("nips")]
    [InlineData("regons")]
    [InlineData("krsNumbers")]
    public async Task Batch_rejects_an_empty_collection_and_empty_ids(string parameter)
    {
        var server = new FakeBirServer();
        var client = CreateClient(server);
        var ct = TestContext.Current.CancellationToken;

        Func<Task> nullCollection;
        Func<Task> noIds;
        Func<Task> emptyId;
        switch (parameter)
        {
            case "nips":
                nullCollection = () => client.FindByNipsAsync(null!, ct);
                noIds = () => client.FindByNipsAsync([], ct);
                emptyId = () => client.FindByNipsAsync([Nip.Parse(GusNip), default], ct);
                break;
            case "regons":
                nullCollection = () => client.FindByRegonsAsync(null!, ct);
                noIds = () => client.FindByRegonsAsync([], ct);
                emptyId = () => client.FindByRegonsAsync([Regon.Parse(GusRegon), default], ct);
                break;
            default:
                nullCollection = () => client.FindByKrsNumbersAsync(null!, ct);
                noIds = () => client.FindByKrsNumbersAsync([], ct);
                emptyId = () => client.FindByKrsNumbersAsync([KrsNumber.Parse("0000028860"), default], ct);
                break;
        }

        Assert.Equal(parameter, (await Assert.ThrowsAsync<ArgumentNullException>(nullCollection)).ParamName);
        Assert.Equal(parameter, (await Assert.ThrowsAsync<ArgumentException>(noIds)).ParamName);
        Assert.Equal(parameter, (await Assert.ThrowsAsync<ArgumentException>(emptyId)).ParamName);
        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task Batch_counts_each_id_against_the_limiter()
    {
        var nips = ValidNips(7);
        var server = new FakeBirServer().Search("Nipy", Joined(nips), "synthetic-search-batch-nips");
        var clock = Clock();
        var session = new BirSession(clock, TimeSpan.FromMinutes(30));
        var limiter = new RegonRateLimiter(clock, RegonRateLimit.Strictest, TimeSpan.FromSeconds(5));
        var client = new RegonClient(new HttpClient(server.Handler), TestOptions(RegonRateLimit.Strictest), clock, session, limiter);

        await client.FindByNipsAsync(nips, TestContext.Current.CancellationToken);

        Assert.Equal(1 + 7, limiter.WeightInLastMinute);
        Assert.Equal(2, limiter.CallsInLastSecond);
    }

    [Fact(Timeout = 10_000)]
    public async Task Batch_of_20_passes_under_the_default_limiter()
    {
        var nips = ValidNips(20);
        var server = new FakeBirServer().Search("Nipy", Joined(nips), "synthetic-search-batch-nips");
        var clock = Clock();
        var started = clock.GetUtcNow();
        var client = CreateClient(server, clock, new RegonClientOptions { Environment = RegonEnvironment.Test });

        var result = await client.FindByNipsAsync(nips, TestContext.Current.CancellationToken);

        Assert.Equal(20, result.Value.Count);
        Assert.Equal(started, clock.GetUtcNow());   // nothing waited: the fake clock was never advanced
    }

    // A client on the default Strictest limiter (3 per second, 120 per minute) whose searches all answer with the batch fixture.
    private static (RegonClient Client, FakeBirServer Server, RegonRateLimiter Limiter, FakeTimeProvider Clock) StrictestClient()
    {
        var server = new FakeBirServer().Respond(static call => call.Operation == "DaneSzukajPodmioty"
            ? RegonTestResponses.Response(HttpStatusCode.OK, RegonTestResponses.Mtom("DaneSzukajPodmioty", RegonTestResponses.Inner("synthetic-search-batch-nips")))
            : null);
        var clock = Clock();
        var session = new BirSession(clock, TimeSpan.FromMinutes(30));
        var limiter = new RegonRateLimiter(clock, RegonRateLimit.Strictest, TimeSpan.FromSeconds(5));
        var client = new RegonClient(new HttpClient(server.Handler), TestOptions(RegonRateLimit.Strictest), clock, session, limiter);
        return (client, server, limiter, clock);
    }

    // Moves the fake clock on while the call waits for the per-second window.
    private static async Task<T> WithClockAsync<T>(Task<T> task, FakeTimeProvider clock)
    {
        for (var step = 0; step < 1000 && !task.IsCompleted; step++)
        {
            await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken));
            if (!task.IsCompleted)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
            }
        }

        return await task;
    }

    // Valid REGONs of the given length: a counter followed by the one check digit that the parser accepts.
    private static List<Regon> ValidRegons(int count, int length)
    {
        var regons = new List<Regon>();
        for (var n = 1; regons.Count < count; n++)
        {
            // a 14-digit REGON extends a valid 9-digit one (GUS's own) by a four-digit unit number and a check digit
            var prefix = length == 9 ? "10" + n.ToString("D6", CultureInfo.InvariantCulture) : GusRegon + n.ToString("D4", CultureInfo.InvariantCulture);
            for (var check = 0; check < 10; check++)
            {
                if (Regon.TryParse(prefix + check.ToString(CultureInfo.InvariantCulture), out var regon))
                {
                    regons.Add(regon);
                    break;
                }
            }
        }

        return regons;
    }

    private static List<KrsNumber> KrsNumbers(int count) =>
        Enumerable.Range(1, count).Select(static i => KrsNumber.Parse(i.ToString("D10", CultureInfo.InvariantCulture))).ToList();

    [Fact(Timeout = 30_000)]
    public async Task Batch_that_cannot_fit_the_minute_window_is_refused_before_any_request()
    {
        // 120 NIPs plus the login make 121, over the 120 per minute of Strictest: nothing is sent, so no quota is spent.
        var (client, server, limiter, _) = StrictestClient();

        var exception = await Assert.ThrowsAsync<QuotaExceededException>(() => client.FindByNipsAsync(ValidNips(120), TestContext.Current.CancellationToken));

        Assert.Empty(server.Calls);
        Assert.Equal(0, limiter.WeightInLastMinute);
        Assert.Contains("119", exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30_000)]
    public async Task Batch_that_fits_the_minute_window_passes()
    {
        var (client, server, limiter, clock) = StrictestClient();

        var result = await WithClockAsync(client.FindByNipsAsync(ValidNips(119), TestContext.Current.CancellationToken), clock);

        Assert.Equal(119, result.Value.Count);
        Assert.Equal(1, server.Count("Zaloguj"));
        Assert.Equal(6, server.Count("DaneSzukajPodmioty"));
        Assert.Equal(120, limiter.WeightInLastMinute);
    }

    [Fact(Timeout = 30_000)]
    public async Task Batch_with_a_live_session_is_checked_against_what_is_left_of_the_minute()
    {
        var (client, server, limiter, clock) = StrictestClient();
        await client.FindByNipsAsync(ValidNips(20), TestContext.Current.CancellationToken);   // login and one chunk: 21 used
        var sent = server.Calls.Count;

        // 100 more need no login and would make 121
        var exception = await Assert.ThrowsAsync<QuotaExceededException>(() => client.FindByNipsAsync(ValidNips(100), TestContext.Current.CancellationToken));

        Assert.Equal(sent, server.Calls.Count);
        Assert.Contains("per-minute", exception.Message, StringComparison.Ordinal);
        Assert.Equal(clock.GetUtcNow().AddMinutes(1), exception.ResetsAt);
        Assert.Equal(TimeSpan.FromMinutes(1), exception.RetryAfter);

        // 99 fit
        var result = await WithClockAsync(client.FindByNipsAsync(ValidNips(99), TestContext.Current.CancellationToken), clock);
        Assert.Equal(99, result.Value.Count);
        Assert.Equal(120, limiter.WeightInLastMinute);
    }

    [Fact(Timeout = 30_000)]
    public async Task Regon_batch_is_checked_as_a_whole_before_any_request()
    {
        var (client, server, _, _) = StrictestClient();
        // 100 nine-digit and 20 fourteen-digit REGONs go out in separate loops; the check covers both together: 1 + 120 > 120
        var regons = ValidRegons(100, 9);
        regons.AddRange(ValidRegons(20, 14));

        await Assert.ThrowsAsync<QuotaExceededException>(() => client.FindByRegonsAsync(regons, TestContext.Current.CancellationToken));

        Assert.Empty(server.Calls);
    }

    [Fact(Timeout = 30_000)]
    public async Task Krs_batch_that_cannot_fit_the_minute_window_is_refused_before_any_request()
    {
        var (client, server, _, _) = StrictestClient();

        var exception = await Assert.ThrowsAsync<QuotaExceededException>(() => client.FindByKrsNumbersAsync(KrsNumbers(120), TestContext.Current.CancellationToken));

        Assert.Empty(server.Calls);
        Assert.Contains("119", exception.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30_000)]
    public async Task Krs_batch_that_fits_the_minute_window_passes()
    {
        var (client, server, limiter, clock) = StrictestClient();

        var result = await WithClockAsync(client.FindByKrsNumbersAsync(KrsNumbers(119), TestContext.Current.CancellationToken), clock);

        Assert.Equal(119, result.Value.Count);
        Assert.Equal(119, server.Count("DaneSzukajPodmioty"));
        Assert.Equal(120, limiter.WeightInLastMinute);
    }

    [Fact]
    public async Task Every_request_has_sid_action_and_wsa_to()
    {
        var server = new FakeBirServer().Search("Nip", GusNip, "search-regon-gus");
        var client = CreateClient(server);

        await client.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);

        Assert.Equal(2, server.Calls.Count);
        Assert.Null(server.Calls[0].Sid);
        Assert.Equal("http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/Zaloguj", server.Calls[0].Action);
        Assert.Equal(FakeBirServer.FirstSid, server.Calls[1].Sid);
        Assert.Equal(SearchAction, server.Calls[1].Action);
        Assert.All(server.Calls, call =>
        {
            Assert.Equal(TestAddress, call.WsaTo);
            Assert.StartsWith("application/soap+xml; charset=utf-8; action=\"" + call.Action + "\"", call.ContentType, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Wsa_to_follows_the_base_address_override()
    {
        var server = new FakeBirServer().Search("Nip", GusNip, "search-regon-gus");
        var options = TestOptions();
        options.BaseAddress = new Uri("https://regon.example.test/wsBIR/Fake.svc");
        var client = CreateClient(server, options: options);

        await client.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);

        Assert.All(server.Calls, call => Assert.Equal("https://regon.example.test/wsBIR/Fake.svc", call.WsaTo));
        Assert.All(server.Handler.Requests, request => Assert.Equal("https://regon.example.test/wsBIR/Fake.svc", request.Uri.ToString()));
    }

    [Fact]
    public async Task Client_without_di_gets_private_state()
    {
        var server = new FakeBirServer().Search("Nip", GusNip, "search-regon-gus");
        var first = CreateClient(server);
        var second = CreateClient(server);

        await first.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);
        await second.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);

        Assert.Equal(2, server.Count("Zaloguj"));   // each client has its own session

        // The same two clients over one shared session and limiter, as AddRegonClient builds them, log in once.
        var shared = new FakeBirServer().Search("Nip", GusNip, "search-regon-gus");
        var clock = Clock();
        var session = new BirSession(clock, TimeSpan.FromMinutes(30));
        var limiter = new RegonRateLimiter(clock, RegonRateLimit.Off, TimeSpan.FromSeconds(5));
        var a = new RegonClient(new HttpClient(shared.Handler), TestOptions(), clock, session, limiter);
        var b = new RegonClient(new HttpClient(shared.Handler), TestOptions(), clock, session, limiter);

        await a.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);
        await b.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);

        Assert.Equal(1, shared.Count("Zaloguj"));
    }

    [Fact]
    public void Missing_key_in_production_throws_at_construction()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new RegonClient(new HttpClient(), new RegonClientOptions()));
        Assert.Contains("RegonClientOptions.UserKey", exception.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => new RegonClient(new HttpClient()));
        Assert.Throws<ArgumentNullException>(() => new RegonClient(null!, TestOptions()));

        // With a key the production client is built (nothing is sent until the first call).
        Assert.NotNull(new RegonClient(new HttpClient(), new RegonClientOptions { UserKey = "dummy-key-not-a-secret" }));
    }

    [Fact]
    public void A_host_that_contradicts_the_environment_throws_at_construction()
    {
        var testHostAsProduction = new RegonClientOptions { UserKey = "dummy-key-not-a-secret", BaseAddress = new Uri(TestAddress) };
        var productionHostAsTest = new RegonClientOptions { Environment = RegonEnvironment.Test, BaseAddress = RegonClientOptions.ProductionAddress };

        var exception = Assert.Throws<InvalidOperationException>(() => new RegonClient(new HttpClient(), testHostAsProduction));
        Assert.Contains("RegonClientOptions.BaseAddress", exception.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => new RegonClient(new HttpClient(), productionHostAsTest));

        // The internal constructor that AddRegonClient uses checks it too.
        var clock = Clock();
        Assert.Throws<InvalidOperationException>(() => new RegonClient(
            new HttpClient(),
            testHostAsProduction,
            clock,
            new BirSession(clock, TimeSpan.FromMinutes(30)),
            new RegonRateLimiter(clock, RegonRateLimit.Off, TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task Retrieved_at_comes_from_the_clock()
    {
        var server = new FakeBirServer().Search("Nip", GusNip, "search-regon-gus");
        var clock = Clock();
        var client = CreateClient(server, clock);
        var start = clock.GetUtcNow();

        var first = await client.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(5));
        var second = await client.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);

        Assert.Equal(start, first.RetrievedAt);
        Assert.Equal(start.AddMinutes(5), second.RetrievedAt);
        Assert.Equal(1, server.Count("Zaloguj"));   // 5 minutes is inside the 30-minute session lifetime
    }

    [Fact]
    public async Task Data_as_of_is_null_until_known()
    {
        var server = new FakeBirServer().Search("Nip", GusNip, "search-regon-gus");
        var clock = Clock();
        var session = new BirSession(clock, TimeSpan.FromMinutes(30));
        var limiter = new RegonRateLimiter(clock, RegonRateLimit.Off, TimeSpan.FromSeconds(5));
        var client = new RegonClient(new HttpClient(server.Handler), TestOptions(), clock, session, limiter);

        var before = await client.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);
        session.DataAsOf = new DateOnly(2018, 10, 19);
        var after = await client.FindByNipAsync(Nip.Parse(GusNip), TestContext.Current.CancellationToken);

        Assert.Null(before.DataAsOf);
        Assert.Equal(new DateOnly(2018, 10, 19), after.DataAsOf);
        Assert.Equal(0, server.Count("GetValue"));   // never fetched implicitly
    }
}
