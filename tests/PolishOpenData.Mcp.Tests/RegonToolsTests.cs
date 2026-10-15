using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using PolishOpenData.Bir.Tests;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Mcp.Tests;

/// <summary>lookup_regon through a real MCP session; GUS BIR answers from the recorded fixtures (FakeBirServer).</summary>
public sealed class RegonToolsTests : IAsyncDisposable
{
    private const string Key = "dummy-key-not-a-secret";
    private const string GusRegon = "000331501";

    private McpTestSession? _session;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    private async Task<CallToolResult> LookupAsync(FakeBirServer server, string key, params (string Name, object? Value)[] arguments)
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _session = await McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => server.Handler),
            null,   // the real clock: the library's 3-requests-per-second limiter waits on it and would never wake under a fake one
            TestContext.Current.CancellationToken,
            regonKey: key);
        return await _session.Client.CallToolAsync("lookup_regon", arguments.ToDictionary(a => a.Name, a => a.Value), cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static JsonElement Data(CallToolResult result)
    {
        Assert.True(result.IsError != true, Text(result));
        return result.StructuredContent!.Value;
    }

    private static FakeBirServer LegalPersonServer() => new FakeBirServer()
        .Search("Regon", GusRegon, "search-regon-gus")
        .Report(GusRegon, "BIR12OsPrawna", "synthetic-report-osprawna-gus")
        .Report(GusRegon, "BIR12OsPrawnaPkd", "synthetic-report-osprawna-pkd");

    [Fact]
    public async Task Lookup_by_regon_returns_the_search_row()
    {
        var data = Data(await LookupAsync(LegalPersonServer(), Key, ("regon", GusRegon)));

        Assert.True(data.GetProperty("found").GetBoolean());
        Assert.Equal("production", data.GetProperty("environment").GetString());
        var entity = Assert.Single(data.GetProperty("entities").EnumerateArray());
        Assert.Equal("legal_person", entity.GetProperty("type").GetString());
        Assert.Equal(GusRegon, entity.GetProperty("regon").GetString());
        Assert.Equal("Rejestr REGON, GUS (usługa BIR1.2)", Assert.Single(data.GetProperty("sources").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Legal_person_gets_details_and_main_pkd()
    {
        var data = Data(await LookupAsync(LegalPersonServer(), Key, ("regon", GusRegon)));

        var entity = Assert.Single(data.GetProperty("entities").EnumerateArray());
        Assert.Equal("PAŃSTWOWA JEDNOSTKA BUDŻETOWA", entity.GetProperty("legalForm").GetString());
        Assert.Equal("BRAK REJESTRU", entity.GetProperty("registerType").GetString());
        Assert.Equal("0000135115", entity.GetProperty("registerNumber").GetString());
        Assert.Equal("1975-12-15", entity.GetProperty("activityStartDate").GetString());
        Assert.Equal("active", entity.GetProperty("status").GetString());
        Assert.Equal(1, entity.GetProperty("localUnitCount").GetInt32());
        var pkd = entity.GetProperty("mainPkd");
        Assert.Equal("8411Z", pkd.GetProperty("code").GetString());
        Assert.Equal("Działalność testowa A", pkd.GetProperty("name").GetString());
        Assert.Equal("2007", pkd.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Natural_person_gets_the_note_and_no_personal_fields()
    {
        var server = new FakeBirServer()
            .Search("Regon", "999999990", "synthetic-search-natural-person")
            .Report("999999990", "BIR12OsFizycznaDaneOgolne", "synthetic-report-fiz-daneogolne")
            .Report("999999990", "BIR12OsFizycznaDzialalnoscCeidg", "synthetic-report-fiz-ceidg")
            .Report("999999990", "BIR12OsFizycznaDzialalnoscRolnicza", "synthetic-report-fiz-rolnicza")
            .Report("999999990", "BIR12OsFizycznaPkd", "synthetic-report-fiz-pkd");

        var result = await LookupAsync(server, Key, ("regon", "999999990"));

        var data = Data(result);
        var entities = data.GetProperty("entities").EnumerateArray().ToArray();
        Assert.Equal(2, entities.Length);
        Assert.All(entities, e =>
        {
            Assert.Equal("natural_person", e.GetProperty("type").GetString());
            Assert.Contains("owner's name", e.GetProperty("note").GetString(), StringComparison.Ordinal);
            Assert.False(e.TryGetProperty("legalForm", out var legalForm) && legalForm.ValueKind == JsonValueKind.String);
        });
        Assert.Equal("active", entities[0].GetProperty("status").GetString());   // suspended 2012-05, resumed 2012-09
        Assert.Equal("6201Z", entities[0].GetProperty("mainPkd").GetProperty("code").GetString());
        Assert.Equal("ended", entities[1].GetProperty("status").GetString());    // the search row carries an end date
        var json = data.GetRawText().ToLowerInvariant();
        foreach (var forbidden in new[] { "imie", "nazwisko", "telefon", "phone", "email", "e-mail", "www", "faks", "fax", "jan.kowalski@example.test", "partner", "localunits" })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Several_matches_are_all_listed_and_details_are_capped()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "synthetic-search-seven-rows");
        var first = new[] { "000331501", "142396858", "010418973", "999999990", "651536363" };
        foreach (var regon in first)
        {
            server.Report(regon, "BIR12OsPrawna", "synthetic-report-osprawna-gus").Report(regon, "BIR12OsPrawnaPkd", "synthetic-report-osprawna-pkd");
        }

        var data = Data(await LookupAsync(server, Key, ("nip", "5261040828")));

        Assert.Equal(7, data.GetProperty("entities").GetArrayLength());
        Assert.Equal(first, server.Calls.Where(c => c.Operation == "DanePobierzPelnyRaport" && c.Arg("pNazwaRaportu") == "BIR12OsPrawna").Select(c => c.Arg("pRegon")).ToArray());
        Assert.Equal("2 more matches are listed without details; ask for one of them by its REGON.", Assert.Single(data.GetProperty("warnings").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Names_are_sanitised()
    {
        var server = new FakeBirServer()
            .Search("Regon", "142396858", "synthetic-search-control-char")
            .Report("142396858", "BIR12OsPrawna", "synthetic-report-osprawna-gus")
            .Report("142396858", "BIR12OsPrawnaPkd", "synthetic-report-osprawna-pkd");

        var data = Data(await LookupAsync(server, Key, ("regon", "142396858")));

        var name = Assert.Single(data.GetProperty("entities").EnumerateArray()).GetProperty("name").GetString()!;
        Assert.DoesNotContain(name, char.IsControl);
        Assert.Equal("SPÓŁKATESTOWA SP. Z O.O.", name);
    }

    [Fact]
    public async Task No_details_keeps_the_row_and_warns()
    {
        var server = new FakeBirServer()
            .Search("Regon", GusRegon, "search-regon-gus")
            .Report(GusRegon, "BIR12OsPrawna", "synthetic-report-error-4");

        var data = Data(await LookupAsync(server, Key, ("regon", GusRegon)));

        var entity = Assert.Single(data.GetProperty("entities").EnumerateArray());
        Assert.Equal(GusRegon, entity.GetProperty("regon").GetString());
        Assert.False(entity.TryGetProperty("legalForm", out var legalForm) && legalForm.ValueKind == JsonValueKind.String);
        Assert.Equal("GUS returned no details for REGON " + GusRegon + ".", Assert.Single(data.GetProperty("warnings").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Not_found_is_not_an_error()
    {
        var server = new FakeBirServer().SearchNotFound("Regon", GusRegon);

        var data = Data(await LookupAsync(server, Key, ("regon", GusRegon)));

        Assert.False(data.GetProperty("found").GetBoolean());
        Assert.Empty(data.GetProperty("entities").EnumerateArray());
    }

    [Theory]
    [InlineData("nip", "123", "'123' is not a valid NIP (10 digits with a correct check digit).")]
    [InlineData("regon", "123", "'123' is not a valid REGON (9 or 14 digits with correct check digits).")]
    [InlineData("krs", "abc", "'abc' is not a KRS number (1 to 10 digits).")]
    public async Task Invalid_identifiers_use_the_lookup_company_texts(string name, string value, string expected)
    {
        var server = new FakeBirServer();

        var result = await LookupAsync(server, Key, (name, value));

        Assert.True(result.IsError);
        Assert.Equal(expected, Text(result));
        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task None_or_two_identifiers_are_rejected_before_any_call()
    {
        var server = new FakeBirServer();

        var none = await LookupAsync(server, Key);
        var two = await LookupAsync(server, Key, ("regon", GusRegon), ("nip", "5261040828"));

        Assert.True(none.IsError);
        Assert.Equal("Provide exactly one of nip, regon or krs.", Text(none));
        Assert.True(two.IsError);
        Assert.Equal("Provide exactly one of nip, regon or krs.", Text(two));
        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task Login_sends_the_configured_key_trimmed()
    {
        var server = LegalPersonServer();

        await LookupAsync(server, Key + "\n", ("regon", GusRegon));

        var login = Assert.Single(server.Calls, c => c.Operation == "Zaloguj");
        Assert.Equal(Key, login.Arg("pKluczUzytkownika"));
    }

    [Fact]
    public async Task Rejected_key_error_never_echoes_the_key()
    {
        var server = new FakeBirServer().RejectKey();

        var result = await LookupAsync(server, Key, ("regon", GusRegon));

        Assert.True(result.IsError);
        Assert.StartsWith("GUS did not accept the key in POLISHOPENDATA_REGON_KEY.", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, Text(result), StringComparison.Ordinal);
    }
}
