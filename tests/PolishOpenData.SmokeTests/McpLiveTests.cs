using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// The MCP server in process with its production wiring and real HTTP, driven over the MCP protocol (in-memory
/// transport). One session for the class, so its cache answers repeated lookups without new upstream calls.
/// </summary>
public sealed partial class McpLiveTests(McpLiveFixture live) : IClassFixture<McpLiveFixture>
{
    private const string KrsEndpoint = "https://api-krs.ms.gov.pl";
    private const string VatEndpoint = "https://wl-api.mf.gov.pl";

    // Identifier fields hold validated numbers (a 14-digit REGON is legitimate); every other string is checked.
    private static readonly string[] IdentifierProperties = ["nip", "regon", "krs"];

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Lookup_company_by_nip_combines_both_registries()
    {
        Live.SkipUnlessEnabled();
        using var company = await CallAsync("lookup_company", new() { ["nip"] = Live.OrlenNip }, TestContext.Current.CancellationToken);
        var root = company.RootElement;

        Assert.True(root.GetProperty("found").GetBoolean());
        Assert.Contains("ORLEN", root.GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal(Live.OrlenNip, root.GetProperty("nip").GetString());
        Assert.Equal(Live.OrlenKrs, root.GetProperty("krs").GetString());
        Assert.Equal("active", root.GetProperty("vat").GetProperty("status").GetString());
        Assert.NotEmpty(root.GetProperty("vat").GetProperty("bankAccounts").EnumerateArray());
        Assert.Equal("found", root.GetProperty("krsRegistry").GetProperty("status").GetString());
        Assert.Equal("entrepreneurs", root.GetProperty("krsRegistry").GetProperty("register").GetString());

        var sources = root.GetProperty("sources").EnumerateArray().ToList();
        AssertRetrievedRecently(Source(sources, KrsEndpoint));
        var vat = Source(sources, VatEndpoint);
        AssertRetrievedRecently(vat);
        Assert.False(string.IsNullOrEmpty(vat.GetProperty("requestId").GetString()));
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Lookup_company_by_krs_finds_the_same_company()
    {
        Live.SkipUnlessEnabled();
        using var company = await CallAsync("lookup_company", new() { ["krs"] = "28860" }, TestContext.Current.CancellationToken);
        var root = company.RootElement;

        Assert.Equal("KRS " + Live.OrlenKrs, root.GetProperty("query").GetString());
        Assert.True(root.GetProperty("found").GetBoolean());
        Assert.Contains("ORLEN", root.GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal(Live.OrlenNip, root.GetProperty("nip").GetString());
        Assert.Equal(Live.OrlenKrs, root.GetProperty("krs").GetString());
        Assert.Equal("active", root.GetProperty("vat").GetProperty("status").GetString());
        Assert.Equal("found", root.GetProperty("krsRegistry").GetProperty("status").GetString());
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Get_krs_extract_of_orlen_has_no_pesel_like_digit_run()
    {
        Live.SkipUnlessEnabled();
        using var extract = await CallAsync("get_krs_extract", new() { ["krs"] = "28860" }, TestContext.Current.CancellationToken);
        var root = extract.RootElement;

        Assert.Equal("found", root.GetProperty("status").GetString());
        var summary = root.GetProperty("summary");
        Assert.Contains("ORLEN", summary.GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Object, summary.GetProperty("representation").ValueKind);

        // The server scrubs 11-digit runs (PESEL numbers) from the KRS free text before returning it.
        var offenders = new List<string>();
        FindElevenDigitRuns(root, "$", offenders);
        Assert.Empty(offenders);
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Check_vat_bank_account_confirms_an_account_from_the_lookup()
    {
        Live.SkipUnlessEnabled();

        // Served from the session cache when the lookup test ran first; otherwise this is the class's one search.
        using var company = await CallAsync("lookup_company", new() { ["nip"] = Live.OrlenNip }, TestContext.Current.CancellationToken);
        var account = company.RootElement.GetProperty("vat").GetProperty("bankAccounts")[0].GetString();

        using var check = await CallAsync("check_vat_bank_account", new() { ["bankAccount"] = account, ["nip"] = Live.OrlenNip }, TestContext.Current.CancellationToken);
        var root = check.RootElement;
        Assert.Equal(account, root.GetProperty("bankAccount").GetString());
        Assert.True(root.GetProperty("assignedToActiveVatPayer").GetBoolean());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("requestId").GetString()));
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Validate_identifier_accepts_a_nip_and_a_kw_number_offline()
    {
        Live.SkipUnlessEnabled();
        using var nip = await CallAsync("validate_identifier", new() { ["value"] = "774-000-14-54", ["kind"] = "nip" }, TestContext.Current.CancellationToken);
        var nipResult = Assert.Single(nip.RootElement.GetProperty("results").EnumerateArray());
        Assert.True(nipResult.GetProperty("isValid").GetBoolean());
        Assert.Equal(Live.OrlenNip, nipResult.GetProperty("normalized").GetString());

        using var kw = await CallAsync("validate_identifier", new() { ["value"] = "WL1A/00272852/9" }, TestContext.Current.CancellationToken);
        var kwResult = Assert.Single(kw.RootElement.GetProperty("results").EnumerateArray());
        Assert.Equal("kw", kwResult.GetProperty("kind").GetString());
        Assert.True(kwResult.GetProperty("isValid").GetBoolean());
        Assert.Equal("WL1A/00272852/9", kwResult.GetProperty("normalized").GetString());
    }

    private async Task<JsonDocument> CallAsync(string tool, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var result = await live.Client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken);
        Live.SkipIfQuotaError(result);
        Assert.False(result.IsError == true, tool + " returned isError: " + Live.Text(result));
        return JsonDocument.Parse(Live.Text(result));
    }

    private static JsonElement Source(List<JsonElement> sources, string endpoint) =>
        Assert.Single(sources, s => string.Equals(s.GetProperty("endpoint").GetString(), endpoint, StringComparison.Ordinal));

    private static void AssertRetrievedRecently(JsonElement source)
    {
        var retrievedAt = source.GetProperty("retrievedAt").GetDateTimeOffset();
        var now = DateTimeOffset.UtcNow;
        Assert.InRange(retrievedAt, now.AddDays(-2), now.AddHours(1));
    }

    private static void FindElevenDigitRuns(JsonElement element, string path, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (!IdentifierProperties.Contains(property.Name, StringComparer.Ordinal))
                    {
                        FindElevenDigitRuns(property.Value, path + "." + property.Name, found);
                    }
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FindElevenDigitRuns(item, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", found);
                    index++;
                }

                break;
            case JsonValueKind.String when ElevenDigits().IsMatch(element.GetString()!):
                found.Add(path);
                break;
        }
    }

    [GeneratedRegex(@"\d{11}")]
    private static partial Regex ElevenDigits();
}

/// <summary>Starts the in-process MCP session with real HTTP, only when the live tests are enabled.</summary>
public sealed class McpLiveFixture : IAsyncLifetime
{
    private McpTestSession? _session;

    public McpClient Client => (_session ?? throw new InvalidOperationException("The live MCP session starts only with " + Live.GateVariable + "=1.")).Client;

    public async ValueTask InitializeAsync()
    {
        if (Live.Enabled)
        {
            _session = await McpTestSession.StartAsync(
                http => http.ConfigureHttpClient(c => c.Timeout = Live.HttpTimeout),
                clock: null,
                TestContext.Current.CancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }
}
