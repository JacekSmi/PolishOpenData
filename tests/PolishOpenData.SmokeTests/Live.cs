using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Xunit.Sdk;
using Xunit.v3;

// One request at a time: keeps the per-IP request pattern gentle and makes the per-class shared results
// (one ORLEN search, one MCP session) deterministic.
[assembly: Parallelization(Mode = ParallelMode.None)]

namespace PolishOpenData.SmokeTests;

/// <summary>
/// Gate and shared settings for the live tests, which call the real KRS, Biała Lista and GUS BIR (REGON) APIs. They run only with
/// <c>POLISHOPENDATA_SMOKE=1</c>: the weekly <c>nightly-smoke</c> workflow sets it, and so can you, locally.
/// <para>
/// Cost of one full run (all live tests), per IP address:
/// <list type="table">
/// <listheader><term>Budget</term><description>Library / MCP in-process / published package = total</description></listheader>
/// <item><term>Biała Lista searches (each NIP or REGON counts, also inside a batch)</term><description>4 (NIP, REGON, 2-NIP batch) / 1 / 1 = 6, sent as 5 requests, plus 2 (one 2-NIP batch) from the offline test = 8</description></item>
/// <item><term>Biała Lista checks</term><description>1 / 1 / 0 = 2</description></item>
/// <item><term>KRS requests</term><description>5 to 9 (current, full, not-found in P and S, change feed of 1 to 5 weekdays) / 1 / 1 = 7 to 11</description></item>
/// <item><term>Offline flat file (<c>BialaListaOfflineLiveTests</c>: one download of about 250 MB from MF, no quota; skips after 15 min of downloading, up to 20 min in all)</term><description>0 / 0 / 0, but one 2-NIP Biała Lista search (the oracle, counted in the searches row) and one flat-file download</description></item>
/// <item><term>GUGiK ULDK requests (<c>GeoLiveTests</c>, no key)</term><description>5 / 0 / 0 = 5 (by ID and by extent centre; by ID and by its WGS-84 centre; by the UUG point)</description></item>
/// <item><term>GUGiK UUG requests (<c>GeoLiveTests</c>, <c>RcnLiveTests</c>, no key)</term><description>3 / 1 / 0 = 4 (geocode and reverse; geocode in the ULDK/UUG agreement test; geocode of the address in the <c>property_prices</c> test)</description></item>
/// <item><term>GUGiK RCN requests (<c>RcnLiveTests</c>, no key)</term><description>4 / 1 / 0 = 5 (library: premises in a bbox, residential premises for the median, parcels, buildings; MCP: <c>property_prices</c> by address; each a single bounded query, at most 1,000 rows)</description></item>
/// <item><term>GUS BIR test environment requests (published test key, no secret)</term><description>5 / 0 / 0 = 5</description></item>
/// <item><term>GUS BIR production requests (needs <c>POLISHOPENDATA_REGON_KEY</c>, owner-run, skipped otherwise)</term><description>4 / 4 / 0 = 8 (the library test: login, search, report, logout; the lookup_regon test: login, search, core report, PKD report)</description></item>
/// </list>
/// Runtime: the 21 other network tests (8 of them GUGiK) take at most 2 min each and the published-package test 5 min; the offline test adds
/// a few minutes when MF is fast and 15 min (then it skips) when MF is slow (it served 35 to 40 KB/s on 2026-10-13, 8 MB/s at night).
/// The change feed asks for an earlier weekday only while the feeds it got were empty (a public holiday, or a feed not
/// yet published). The MCP tests share one server session, so its cache answers repeated lookups without new upstream
/// calls. Failed calls are not cached, so the next test that needs a failed search sends it again: the library NIP
/// search when it was cancelled, and any MCP search (for example after the MCP server's 10 s attempt timeout, which
/// Biała Lista still counts). A run with failures therefore spends at most 11 searches. Biała Lista is never retried;
/// the MCP server's KRS client (in process and published) retries transient failures (5xx, 408, 429, connection
/// errors and 10 s attempt timeouts), which can add KRS requests but never Biała Lista quota.
/// </para>
/// <para>
/// Biała Lista allows 100 searches and 5,000 checks per IP address per day; exceeding either blocks the IP until
/// midnight Warsaw time, including searches on podatki.gov.pl. A run spends 8 searches (at most 11), so running it
/// locally a few times a day is fine; do not run it in a loop. A test that hits a limit is skipped, not failed.
/// </para>
/// <para>
/// GUS BIR (REGON): <c>RegonTestEnvironmentLiveTests</c> uses GUS's public test environment and its published test key, so it needs
/// no secret and runs in the weekly workflow. <c>RegonLiveTests</c> calls the production service and runs only when
/// <see cref="RegonKeyVariable"/> holds the owner's own key; CI never sets it (the production key must not go into GitHub secrets).
/// GUS logs the IP address, time and content of every request and forbids enumerating identifiers and holding several accounts
/// for one entity: run the production test sparingly, never in a loop.
/// </para>
/// <para>
/// bash: <c>POLISHOPENDATA_SMOKE=1 dotnet test --project tests/PolishOpenData.SmokeTests -c Release</c><br/>
/// PowerShell: <c>$env:POLISHOPENDATA_SMOKE='1'; try { dotnet test --project tests/PolishOpenData.SmokeTests -c Release } finally { Remove-Item Env:POLISHOPENDATA_SMOKE }</c><br/>
/// The published-package test runs <c>dotnet dnx PolishOpenData.Mcp --yes</c> (latest on nuget.org); set
/// <c>POLISHOPENDATA_SMOKE_MCP_VERSION</c> to test another version. An exact version (e.g. <c>1.0.0</c>) is also
/// compared with the version the server reports; a floating version or range (e.g. <c>1.*</c>) is only passed to dnx.
/// </para>
/// </summary>
internal static class Live
{
    /// <summary>The environment variable that enables the live tests (value <c>1</c>).</summary>
    public const string GateVariable = "POLISHOPENDATA_SMOKE";

    /// <summary>The environment variable that holds your own GUS production key for <c>RegonLiveTests</c>; the test is skipped without it.</summary>
    public const string RegonKeyVariable = "POLISHOPENDATA_REGON_KEY";

    /// <summary>Upper bound for one live test (ms). HttpClient.Timeout does not cover reading a body after the headers.</summary>
    public const int TestTimeout = 120_000;

    /// <summary>ORLEN S.A., the known subject: a large public company in both registries.</summary>
    public const string OrlenNip = "7740001454";

    /// <summary>ORLEN's KRS number.</summary>
    public const string OrlenKrs = "0000028860";

    /// <summary>Timeout of every live HTTP client, so an unresponsive registry fails the test instead of hanging the run.</summary>
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

    /// <summary>True when <c>POLISHOPENDATA_SMOKE=1</c>.</summary>
    public static bool Enabled => string.Equals(Environment.GetEnvironmentVariable(GateVariable), "1", StringComparison.Ordinal);

    /// <summary>Call first in every live test.</summary>
    public static void SkipUnlessEnabled()
    {
        if (!Enabled)
        {
            Assert.Skip("Live tests run only with " + GateVariable + "=1; they call the real registries and spend Biała Lista quota (see the Live class).");
        }
    }

    /// <summary>An HTTP client for the library clients, with <see cref="HttpTimeout"/>.</summary>
    public static HttpClient CreateHttpClient() => new() { Timeout = HttpTimeout };

    /// <summary>A registry limit is not a regression: skip.</summary>
    public static void SkipOnQuota(QuotaExceededException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Assert.Skip("Registry request limit reached for this IP address: " + exception.Message);
    }

    /// <summary>The MCP server reports a registry limit as an isError result (see ToolErrors), not as an exception: skip.</summary>
    public static void SkipIfQuotaError(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsError == true && Text(result).StartsWith("Request limit reached:", StringComparison.Ordinal))
        {
            Assert.Skip("Registry request limit reached for this IP address: " + Text(result));
        }
    }

    /// <summary>
    /// Asked by NIP or REGON, <c>lookup_company</c> keeps the whitelist part when KRS fails: the result is not an error,
    /// its KRS part is null and the KRS error is in <c>warnings</c>. Skips when that error is a registry limit (as
    /// <see cref="SkipIfQuotaError"/> does for an error result) and fails with the warnings otherwise.
    /// </summary>
    public static void RequirePart(JsonElement result, string property)
    {
        if (result.TryGetProperty(property, out var part) && part.ValueKind == JsonValueKind.Object)
        {
            return;
        }

        var warnings = result.TryGetProperty("warnings", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(w => w.ValueKind == JsonValueKind.String ? w.GetString()! : w.GetRawText()).ToList()
            : [];
        if (warnings.FirstOrDefault(w => w.Contains("Request limit reached:", StringComparison.Ordinal)) is { } quota)
        {
            Assert.Skip("Registry request limit reached for this IP address: " + quota);
        }

        Assert.Fail("The result has no " + property + " part. Warnings: " + (warnings.Count == 0 ? "(none)" : string.Join(" | ", warnings)));
    }

    /// <summary>The first text block of a tool result (the JSON for successful calls, the message for errors).</summary>
    public static string Text(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
    }
}
