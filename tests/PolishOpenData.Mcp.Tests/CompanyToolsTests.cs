using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using PolishOpenData.Mcp;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Mcp.Tests;

public sealed class CompanyToolsTests : IDisposable
{
    private const string OrlenAccount = "06160011271843983820000034";
    private const string WrongAccount = "16160011271234567890123456";

    // Built here (not a committed fixture): a minimal current extract whose free-text fields carry an obviously
    // synthetic 11-digit run, to prove get_krs_extract scrubs it before the summary leaves the server.
    private const string PeselExtractJson = """
        {
          "odpis": {
            "naglowekA": { "rejestr": "RejP", "numerKRS": "0000555001" },
            "dane": {
              "dzial1": {
                "danePodmiotu": { "nazwa": "TEST SPÓŁKA Z O.O." },
                "wspolnicySpzoo": [
                  { "nazwa": "SHAREHOLDER SPÓŁKA Z O.O.", "posiadaneUdzialy": "10 UDZIAŁÓW, PESEL 12345678901" }
                ]
              },
              "dzial2": {
                "reprezentacja": { "nazwaOrganu": "ZARZĄD", "sposobReprezentacji": "JEDNOOSOBOWO, PESEL 12345678901", "sklad": [] },
                "organNadzoru": [
                  { "nazwa": "RADA NADZORCZA", "sposobReprezentacji": "NADZÓR, PESEL 98765432109", "sklad": [] }
                ]
              }
            }
          }
        }
        """;

    private readonly StubHttpMessageHandler _stub = new(Route);
    private readonly ServiceProvider _provider;
    private readonly CompanyTools _tools;

    public CompanyToolsTests() =>
        (_provider, _tools) = Build(new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero)), _stub);

    public void Dispose() => _provider.Dispose();

    private static (ServiceProvider Provider, CompanyTools Tools) Build(TimeProvider clock, StubHttpMessageHandler stub)
    {
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddPolishOpenDataServices(http => http.ConfigurePrimaryHttpMessageHandler(() => stub));
        var provider = services.BuildServiceProvider();
        return (provider, new CompanyTools(provider.GetRequiredService<CachedRegistries>(), provider.GetRequiredService<TimeProvider>()));
    }

    private static HttpResponseMessage Route(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        if (uri.Host == "api-krs.ms.gov.pl")
        {
            return uri.PathAndQuery switch
            {
                "/api/krs/OdpisAktualny/0000028860?rejestr=P&format=json" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "krs/current-P-0000028860-orlen.json"),
                "/api/krs/OdpisAktualny/0000030897?rejestr=S&format=json" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "krs/current-S-0000030897-wosp.json"),
                "/api/krs/OdpisAktualny/0000106150?rejestr=P&format=json" => StubHttpMessageHandler.Empty(HttpStatusCode.NoContent),
                "/api/krs/OdpisPelny/0000106150?rejestr=P&format=json" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "krs/full-P-0000106150-removed-trimmed.json"),
                "/api/krs/OdpisAktualny/0000555001?rejestr=P&format=json" => StubHttpMessageHandler.Json(HttpStatusCode.OK, PeselExtractJson),
                _ => StubHttpMessageHandler.FromFixture(HttpStatusCode.NotFound, "krs/not-found-404.json"),
            };
        }

        return uri.AbsolutePath switch
        {
            "/api/search/nip/7740001454" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/search-nip-orlen.json"),
            "/api/search/nip/5213003700" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/search-nip-notfound.json"),
            "/api/search/nip/9999999982" => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/synthetic-search-nip-nullarrays-unknownfield.json"),
            "/api/check/nip/7740001454/bank-account/" + OrlenAccount => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/check-nip-tak.json"),
            "/api/check/nip/7740001454/bank-account/" + WrongAccount => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bialalista/check-nip-nie.json"),
            _ => StubHttpMessageHandler.FromFixture(HttpStatusCode.NotFound, "bialalista/error-404-wl190-unknown-route.json"),
        };
    }

    private static JsonElement Json(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        using var document = JsonDocument.Parse(Text(result));
        return document.RootElement.Clone();
    }

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [Fact]
    public async Task Lookup_by_nip_merges_whitelist_and_krs()
    {
        var root = Json(await _tools.LookupCompany(nip: "774-000-14-54", cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(root.GetProperty("found").GetBoolean());
        Assert.Equal("ORLEN SPÓŁKA AKCYJNA", root.GetProperty("name").GetString());
        Assert.Equal("0000028860", root.GetProperty("krs").GetString());
        var vat = root.GetProperty("vat");
        Assert.Equal("active", vat.GetProperty("status").GetString());
        Assert.Equal(236, vat.GetProperty("bankAccountCount").GetInt32());
        Assert.Equal(20, vat.GetProperty("bankAccounts").GetArrayLength());
        var krs = root.GetProperty("krsRegistry");
        Assert.Equal("found", krs.GetProperty("status").GetString());
        Assert.Equal("SPÓŁKA AKCYJNA", krs.GetProperty("legalForm").GetString());
        Assert.Equal(2, root.GetProperty("sources").GetArrayLength());
        Assert.Contains(root.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("20 of 236", StringComparison.Ordinal));
        Assert.Contains("ORLEN SPÓŁKA AKCYJNA", Text(await _tools.LookupCompany(nip: "7740001454", cancellationToken: TestContext.Current.CancellationToken)), StringComparison.Ordinal);   // readable Polish, no \u escapes
    }

    [Fact]
    public async Task Repeated_lookups_are_served_from_cache()
    {
        await _tools.LookupCompany(nip: "7740001454", cancellationToken: TestContext.Current.CancellationToken);
        var requests = _stub.RequestUris.Count;
        await _tools.LookupCompany(nip: "7740001454", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(requests, _stub.RequestUris.Count);
    }

    [Fact]
    public async Task Lookup_by_krs_goes_through_the_nip_in_the_extract()
    {
        var root = Json(await _tools.LookupCompany(krs: "30897", cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(root.GetProperty("found").GetBoolean());
        Assert.Equal("5213003700", root.GetProperty("nip").GetString());
        Assert.Equal("associations", root.GetProperty("krsRegistry").GetProperty("register").GetString());
        Assert.Equal("not_found", root.GetProperty("vat").GetProperty("status").GetString());
        Assert.Contains(_stub.RequestUris, u => u.AbsolutePath == "/api/search/nip/5213003700");
    }

    [Fact]
    public async Task Lookup_of_removed_company_reports_removal_date()
    {
        var root = Json(await _tools.LookupCompany(krs: "106150", cancellationToken: TestContext.Current.CancellationToken));
        var krs = root.GetProperty("krsRegistry");
        Assert.Equal("removed", krs.GetProperty("status").GetString());
        Assert.Equal("2022-08-12", krs.GetProperty("removedOn").GetString());
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("7740001454", null, "28860")]
    public async Task Lookup_requires_exactly_one_identifier(string? nip, string? regon, string? krs)
    {
        var result = await _tools.LookupCompany(nip, regon, krs, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError == true);
        Assert.Contains("exactly one", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Lookup_rejects_bad_input_without_calling_the_registry()
    {
        var badNip = await _tools.LookupCompany(nip: "7740001455", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(badNip.IsError == true);
        Assert.Contains("not a valid NIP", Text(badNip), StringComparison.Ordinal);

        var badDate = await _tools.LookupCompany(nip: "7740001454", date: "24.09.2026", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(badDate.IsError == true);
        Assert.Contains("YYYY-MM-DD", Text(badDate), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Lookup_rejects_a_future_date_without_calling_the_registry()
    {
        // The fake clock is 2026-09-24 in Warsaw; 2026-09-25 is tomorrow.
        var result = await _tools.LookupCompany(nip: "7740001454", date: "2026-09-25", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError == true);
        Assert.Contains("is in the future", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Lookup_accepts_today_as_an_explicit_date()
    {
        // The fake clock is 2026-09-24 in Warsaw: an explicit "today" is not in the future.
        var result = await _tools.LookupCompany(nip: "7740001454", date: "2026-09-24", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(Json(result).GetProperty("found").GetBoolean());
        Assert.Contains(_stub.RequestUris, u => u.AbsolutePath == "/api/search/nip/7740001454" && u.Query == "?date=2026-09-24");
    }

    [Fact]
    public async Task Future_date_check_uses_the_Warsaw_day()
    {
        // 22:30 UTC on 2026-09-24 is 00:30 on 2026-09-25 in Warsaw (CEST): the 25th is today there, the 26th is not.
        using var stub = new StubHttpMessageHandler(Route);
        var (provider, tools) = Build(new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 22, 30, 0, TimeSpan.Zero)), stub);
        using var disposeProvider = provider;

        var today = await tools.LookupCompany(nip: "7740001454", date: "2026-09-25", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(Json(today).GetProperty("found").GetBoolean());
        Assert.Contains(stub.RequestUris, u => u.AbsolutePath == "/api/search/nip/7740001454" && u.Query == "?date=2026-09-25");

        var requests = stub.RequestUris.Count;
        var tomorrow = await tools.LookupCompany(nip: "7740001454", date: "2026-09-26", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(tomorrow.IsError == true);
        Assert.Contains("is in the future", Text(tomorrow), StringComparison.Ordinal);
        Assert.Equal(requests, stub.RequestUris.Count);
    }

    [Fact]
    public async Task Lookup_by_nip_not_on_whitelist_suggests_krs_when_none_is_known()
    {
        var root = Json(await _tools.LookupCompany(nip: "5213003700", cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(root.GetProperty("found").GetBoolean());
        Assert.Contains(root.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("try again with 'krs'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lookup_of_a_taxpayer_without_a_krs_number_does_not_suggest_krs()
    {
        // A sole trader (JDG): found and active on the whitelist, but Biała Lista never reports a KRS number for
        // one. That is not the "dead end" the krs-retry warning is about, and must not say "Not on the VAT whitelist".
        var root = Json(await _tools.LookupCompany(nip: "9999999982", cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(root.GetProperty("found").GetBoolean());
        Assert.Equal("exempt", root.GetProperty("vat").GetProperty("status").GetString());
        Assert.DoesNotContain(root.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("Not on the VAT whitelist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_account_on_whitelist()
    {
        var root = Json(await _tools.CheckVatBankAccount(OrlenAccount, nip: "7740001454", cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(root.GetProperty("assignedToActiveVatPayer").GetBoolean());
        Assert.Equal("NiR01-98jk3mi", root.GetProperty("requestId").GetString());
        Assert.Contains("is on the VAT whitelist", root.GetProperty("meaning").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_account_not_on_whitelist_is_explained_carefully()
    {
        var root = Json(await _tools.CheckVatBankAccount(WrongAccount, nip: "7740001454", cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(root.GetProperty("assignedToActiveVatPayer").GetBoolean());
        var meaning = root.GetProperty("meaning").GetString()!;
        Assert.Contains("not an active VAT payer", meaning, StringComparison.Ordinal);
        Assert.Contains("does not tell who owns", meaning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_rejects_invalid_account()
    {
        var result = await _tools.CheckVatBankAccount("06160011271843983820000035", nip: "7740001454", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError == true);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Check_account_rejects_a_future_date_without_calling_the_registry()
    {
        // The fake clock is 2026-09-24 in Warsaw; 2026-09-25 is tomorrow.
        var result = await _tools.CheckVatBankAccount(OrlenAccount, nip: "7740001454", date: "2026-09-25", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError == true);
        Assert.Contains("is in the future", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Krs_extract_is_a_summary_without_free_text()
    {
        var result = await _tools.GetKrsExtract("28860", TestContext.Current.CancellationToken);
        var root = Json(result);
        Assert.Equal("found", root.GetProperty("status").GetString());
        var summary = root.GetProperty("summary");
        Assert.Equal("ORLEN SPÓŁKA AKCYJNA", summary.GetProperty("name").GetString());
        Assert.Equal("7740001454", summary.GetProperty("nip").GetString());
        Assert.Equal(9, summary.GetProperty("representation").GetProperty("members").GetArrayLength());
        Assert.DoesNotContain("REDACTED", Text(result), StringComparison.Ordinal);   // rodzajProkury free text never leaves the server
    }

    [Fact]
    public async Task Krs_extract_of_removed_and_unknown_entities()
    {
        var removed = Json(await _tools.GetKrsExtract("106150", TestContext.Current.CancellationToken));
        Assert.Equal("removed", removed.GetProperty("status").GetString());
        Assert.Equal("2022-08-12", removed.GetProperty("removedOn").GetString());

        var unknown = Json(await _tools.GetKrsExtract("1", TestContext.Current.CancellationToken));
        Assert.Equal("not_found", unknown.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Krs_extract_scrubs_pesel_like_numbers_from_free_text_fields()
    {
        var result = await _tools.GetKrsExtract("555001", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("12345678901", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain("98765432109", Text(result), StringComparison.Ordinal);

        var summary = Json(result).GetProperty("summary");
        Assert.Contains("[PESEL removed]", summary.GetProperty("representation").GetProperty("representationMethod").GetString(), StringComparison.Ordinal);
        Assert.Contains("[PESEL removed]", summary.GetProperty("supervisoryBodies").EnumerateArray().Single().GetProperty("representationMethod").GetString(), StringComparison.Ordinal);
        Assert.Contains("[PESEL removed]", summary.GetProperty("shareholders").EnumerateArray().Single().GetProperty("shares").GetString(), StringComparison.Ordinal);
    }

    // Synthetic numbers only. A PESEL is 11 digits, sometimes written as birth date + serial (6 + 5) with one space or
    // hyphen; a longer digit run is some other number (a 14-digit REGON, a 26-digit NRB) and stays intact.
    [Theory]
    [InlineData("PESEL 12345678901", "PESEL [PESEL removed]")]
    [InlineData("12345678901", "[PESEL removed]")]
    [InlineData("PESEL: 12345678901, udziały 10", "PESEL: [PESEL removed], udziały 10")]
    [InlineData("PESEL 850101 12346", "PESEL [PESEL removed]")]
    [InlineData("PESEL 850101-12346.", "PESEL [PESEL removed].")]
    [InlineData("12345678901 i 98765432109", "[PESEL removed] i [PESEL removed]")]
    [InlineData("REGON 61018820170000, ZARZĄD", "REGON 61018820170000, ZARZĄD")]
    [InlineData("RACHUNEK 61109010140000071219812874 W BANKU", "RACHUNEK 61109010140000071219812874 W BANKU")]
    [InlineData("NUMER 123456789012", "NUMER 123456789012")]
    [InlineData("NUMER 1234567-12345", "NUMER 1234567-12345")]
    [InlineData("NUMER 850101-123466", "NUMER 850101-123466")]
    [InlineData("KRS 0000028860, NIP 7740001454", "KRS 0000028860, NIP 7740001454")]
    public void Pesel_scrub_removes_only_standalone_pesel_like_numbers(string text, string expected)
    {
        Assert.Equal(expected, CompanyTools.ScrubPeselText(text));
    }
}
