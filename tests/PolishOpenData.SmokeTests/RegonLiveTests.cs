using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Bir;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// The REGON client against GUS's public BIR test environment (gated and budgeted by <see cref="Live"/>). It needs no secret:
/// the client uses GUS's published test key. Five requests: login, search, one report, the data date, logout. This is the test
/// that notices a change of the MTOM framing or of the envelope, so the weekly workflow runs it.
/// </summary>
public sealed class RegonTestEnvironmentLiveTests
{
    // GUS itself: the legal entity that the recorded unit-test fixtures come from.
    internal const string GusRegon = "000331501";
    internal const string GusNip = "5261040828";

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Test_environment_search_report_and_data_date_match_the_model()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new RegonClient(http, new RegonClientOptions { Environment = RegonEnvironment.Test });
        var ct = TestContext.Current.CancellationToken;

        try
        {
            var search = await client.FindByRegonAsync(Regon.Parse(GusRegon), ct);
            AssertGusRow(search, RegonEnvironment.Test);

            // One BIR12OsPrawna report, mapped to the typed model: the same request count as a raw report, but a GUS field that
            // was renamed (and so would silently map to null) fails here, in the weekly smoke.
            var details = await client.GetDetailsAsync(search.Value[0], RegonInclude.None, ct);
            AssertGusDetails(details, RegonEnvironment.Test);

            var date = await client.GetDataDateAsync(ct);
            Assert.Equal(RegonEnvironment.Test, date.Environment);
            if (date.Value is null)
            {
                Assert.Fail(
                    "GUS answered StanDanych in a format that RegonMapper.ParseDataDate does not know (it accepts dd-MM-yyyy, yyyy-MM-dd and dd.MM.yyyy, each with an optional time of day). " +
                    "Add the new format to ParseDataDate, with a test, and capture a redacted fixture.");
            }

            Assert.Equal(date.Value, date.DataAsOf);
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
        }
        finally
        {
            // also after a failed assertion or a quota refusal: the session must not stay open at GUS (best effort, never throws but for cancellation)
            await client.LogoutAsync(ct);
        }
    }

    // The shape that both live tests expect of GUS's own row: one legal person. The test environment is a copy that is not
    // updated, so only the stable fields are asserted.
    internal static void AssertGusRow(RegonResult<IReadOnlyList<RegonEntity>> search, RegonEnvironment environment)
    {
        Assert.Equal(environment, search.Environment);
        var entity = Assert.Single(search.Value);
        Assert.Equal(RegonEntityType.LegalPerson, entity.Type);
        Assert.Equal(RegonSilo.LegalPerson, entity.Silo);
        Assert.Equal(Regon.Parse(GusRegon), entity.Regon);
        Assert.Equal(Nip.Parse(GusNip), entity.Nip);
        Assert.Contains("STATYSTYCZNY", entity.Name, StringComparison.Ordinal);
    }

    // The typed core of a legal person's report: if GUS renames one of these fields, the mapper returns null for it without
    // any other failure, so the live test has to notice. The test environment is a stale copy of the register, so only fields
    // that it fills for GUS are asserted (it has praw_dataPowstania but leaves praw_dataWpisuDoRegon empty).
    internal static void AssertGusDetails(RegonResult<IReadOnlyList<RegonDetails>> details, RegonEnvironment environment)
    {
        Assert.Equal(environment, details.Environment);
        var d = Assert.Single(details.Value);
        Assert.True(d.Fields.TryGetValue("praw_regon9", out var regon9), "GUS no longer sends praw_regon9");
        Assert.Equal(GusRegon, regon9);
        Assert.NotNull(d.LegalFormCode);
        Assert.NotNull(d.Address.City);
        Assert.NotNull(d.Address.PostalCode);
        Assert.NotNull(d.FormationDate);
    }

    internal static void AssertGusReport(RegonResult<IReadOnlyList<RegonRecord>> report, RegonEnvironment environment)
    {
        Assert.Equal(environment, report.Environment);
        var record = Assert.Single(report.Value);
        Assert.True(record.Fields.ContainsKey("praw_regon9"), "GUS no longer sends praw_regon9");
        Assert.Equal(GusRegon, record["praw_regon9"]);
        Assert.True(record.Fields.ContainsKey("praw_nazwa"), "GUS no longer sends praw_nazwa");
        Assert.False(string.IsNullOrEmpty(record["praw_nazwa"]));
    }
}

/// <summary>
/// The REGON client against the production BIR service. It runs only when <see cref="Live.RegonKeyVariable"/> holds your own GUS
/// key and is for the owner to run by hand: CI never sets the variable, because the production key does not go into GitHub
/// secrets and GUS forbids several accounts for one entity. Four requests: login, search, one report, logout; a legal entity
/// only (GUS itself).
/// </summary>
public sealed class RegonLiveTests
{
    [Fact(Timeout = Live.TestTimeout)]
    public async Task Production_search_and_report_of_gus_match_the_model()
    {
        Live.SkipUnlessEnabled();
        var key = Environment.GetEnvironmentVariable(Live.RegonKeyVariable);
        if (string.IsNullOrWhiteSpace(key))
        {
            Assert.Skip("The production REGON test runs only when " + Live.RegonKeyVariable + " holds your own GUS key; CI never sets it.");
        }

        using var http = Live.CreateHttpClient();
        var client = new RegonClient(http, new RegonClientOptions { Environment = RegonEnvironment.Production, UserKey = key });
        var ct = TestContext.Current.CancellationToken;

        try
        {
            var search = await client.FindByRegonAsync(Regon.Parse(RegonTestEnvironmentLiveTests.GusRegon), ct);
            RegonTestEnvironmentLiveTests.AssertGusRow(search, RegonEnvironment.Production);

            var report = await client.GetReportAsync(Regon.Parse(RegonTestEnvironmentLiveTests.GusRegon), RegonReport.OsPrawna, ct);
            RegonTestEnvironmentLiveTests.AssertGusReport(report, RegonEnvironment.Production);
        }
        catch (QuotaExceededException ex)
        {
            Live.SkipOnQuota(ex);
        }
        finally
        {
            // also after a failed assertion or a quota refusal: the session on the owner's production key must not stay open
            await client.LogoutAsync(ct);
        }
    }
}

/// <summary>
/// The opt-in <c>lookup_regon</c> MCP tool against production GUS. Like the library's production test it runs only with your own
/// <c>POLISHOPENDATA_REGON_KEY</c> and is for the owner to run by hand: CI never sets it. Four requests: login, search by NIP, the core
/// report and the PKD report (the in-process server does not log out).
/// </summary>
public sealed class RegonMcpLiveTests
{
    [Fact(Timeout = Live.TestTimeout)]
    public async Task Lookup_regon_by_nip_finds_gus_with_details()
    {
        Live.SkipUnlessEnabled();
        var key = Environment.GetEnvironmentVariable(Live.RegonKeyVariable);
        if (string.IsNullOrWhiteSpace(key))
        {
            Assert.Skip("The production lookup_regon test runs only when " + Live.RegonKeyVariable + " holds your own GUS key; CI never sets it.");
        }

        var ct = TestContext.Current.CancellationToken;
        await using var session = await McpTestSession.StartAsync(http => http.ConfigureHttpClient(c => c.Timeout = Live.HttpTimeout), clock: null, ct, regonKey: key);

        var result = await session.Client.CallToolAsync("lookup_regon", new Dictionary<string, object?> { ["nip"] = RegonTestEnvironmentLiveTests.GusNip }, cancellationToken: ct);

        Assert.True(result.IsError != true, ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text);
        var data = result.StructuredContent!.Value;
        Assert.True(data.GetProperty("found").GetBoolean());
        Assert.Equal("production", data.GetProperty("environment").GetString());
        var gus = Assert.Single(data.GetProperty("entities").EnumerateArray(), e => e.GetProperty("regon").GetString() == RegonTestEnvironmentLiveTests.GusRegon);
        Assert.Equal("legal_person", gus.GetProperty("type").GetString());
        Assert.Equal("active", gus.GetProperty("status").GetString());
        Assert.True(gus.TryGetProperty("mainPkd", out var pkd) && pkd.ValueKind == System.Text.Json.JsonValueKind.Object, "GUS should have a main PKD activity");
    }
}
