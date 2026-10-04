using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PolishOpenData.Bir.Tests;

public class RegonModelTests
{
    // The 17 report names and the REGON length each report needs, written out by hand so that a typo in the library is caught
    // (research-gus-bir/api-facts.md section 5.4).
    private static readonly (RegonReport Report, string WireName, int Length)[] ExpectedReports =
    [
        (RegonReport.OsPrawna, "BIR12OsPrawna", 9),
        (RegonReport.OsPrawnaPkd, "BIR12OsPrawnaPkd", 9),
        (RegonReport.OsPrawnaListaJednLokalnych, "BIR12OsPrawnaListaJednLokalnych", 9),
        (RegonReport.OsPrawnaSpCywilnaWspolnicy, "BIR12OsPrawnaSpCywilnaWspolnicy", 9),
        (RegonReport.OsFizycznaDaneOgolne, "BIR12OsFizycznaDaneOgolne", 9),
        (RegonReport.OsFizycznaDzialalnoscCeidg, "BIR12OsFizycznaDzialalnoscCeidg", 9),
        (RegonReport.OsFizycznaDzialalnoscRolnicza, "BIR12OsFizycznaDzialalnoscRolnicza", 9),
        (RegonReport.OsFizycznaDzialalnoscPozostala, "BIR12OsFizycznaDzialalnoscPozostala", 9),
        (RegonReport.OsFizycznaDzialalnoscSkreslonaDo20141108, "BIR12OsFizycznaDzialalnoscSkreslonaDo20141108", 9),
        (RegonReport.OsFizycznaPkd, "BIR12OsFizycznaPkd", 9),
        (RegonReport.OsFizycznaListaJednLokalnych, "BIR12OsFizycznaListaJednLokalnych", 9),
        (RegonReport.JednLokalnaOsPrawnej, "BIR12JednLokalnaOsPrawnej", 14),
        (RegonReport.JednLokalnaOsPrawnej121, "BIR121JednLokalnaOsPrawnej", 14),
        (RegonReport.JednLokalnaOsPrawnejPkd, "BIR12JednLokalnaOsPrawnejPkd", 14),
        (RegonReport.JednLokalnaOsFizycznej, "BIR12JednLokalnaOsFizycznej", 14),
        (RegonReport.JednLokalnaOsFizycznejPkd, "BIR12JednLokalnaOsFizycznejPkd", 14),
        (RegonReport.TypPodmiotu, "BIR12TypPodmiotu", 0),
    ];

    [Fact]
    public void Result_exposes_environment_time_and_data_date()
    {
        var retrievedAt = new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

        var known = new RegonResult<int>(5, RegonEnvironment.Test, retrievedAt, new DateOnly(2018, 10, 19));
        var unknown = new RegonResult<string>("value", RegonEnvironment.Production, retrievedAt, null);

        Assert.Equal(5, known.Value);
        Assert.Equal(RegonEnvironment.Test, known.Environment);
        Assert.Equal(retrievedAt, known.RetrievedAt);
        Assert.Equal(new DateOnly(2018, 10, 19), known.DataAsOf);
        Assert.Equal("value", unknown.Value);
        Assert.Equal(RegonEnvironment.Production, unknown.Environment);
        Assert.Null(unknown.DataAsOf);
    }

    [Fact]
    public void Record_indexer_returns_null_for_a_missing_field()
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["praw_nazwa"] = "GŁÓWNY URZĄD STATYSTYCZNY" };

        var record = new RegonRecord(fields);

        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", record["praw_nazwa"]);
        Assert.Null(record["praw_nip"]);
        Assert.Null(record["PRAW_NAZWA"]);
        Assert.Same(fields, record.Fields);
        Assert.Single(record.Fields);
    }

    [Fact]
    public void Batch_entry_exposes_identifier_and_entities()
    {
        var entities = new[] { new RegonEntity { Name = "A" }, new RegonEntity { Name = "B" } };

        var entry = new RegonBatchEntry("5261040828", entities);
        var empty = new RegonBatchEntry("0000000000", []);

        Assert.Equal("5261040828", entry.Identifier);
        Assert.Equal(2, entry.Entities.Count);
        Assert.Equal("B", entry.Entities[1].Name);
        Assert.Equal("0000000000", empty.Identifier);
        Assert.Empty(empty.Entities);
    }

    [Fact]
    public void Reports_have_distinct_wire_names_for_all_17()
    {
        // every declared member is covered by the table above, and there are exactly 17 of them
        var declared = typeof(RegonReport).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => f.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expected = ExpectedReports.Select(e => e.Report.ToString()).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(17, declared.Length);
        Assert.Equal(declared, expected);
        foreach (var (report, wireName, _) in ExpectedReports)
        {
            Assert.Equal(wireName, RegonReports.WireName(report));
        }

        Assert.Equal(17, ExpectedReports.Select(e => RegonReports.WireName(e.Report)).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("BIR121JednLokalnaOsPrawnej", RegonReports.WireName(RegonReport.JednLokalnaOsPrawnej121));
    }

    [Theory]
    [InlineData(RegonReport.OsPrawna, 9)]
    [InlineData(RegonReport.OsPrawnaPkd, 9)]
    [InlineData(RegonReport.OsPrawnaListaJednLokalnych, 9)]
    [InlineData(RegonReport.OsPrawnaSpCywilnaWspolnicy, 9)]
    [InlineData(RegonReport.OsFizycznaDaneOgolne, 9)]
    [InlineData(RegonReport.OsFizycznaDzialalnoscCeidg, 9)]
    [InlineData(RegonReport.OsFizycznaDzialalnoscRolnicza, 9)]
    [InlineData(RegonReport.OsFizycznaDzialalnoscPozostala, 9)]
    [InlineData(RegonReport.OsFizycznaDzialalnoscSkreslonaDo20141108, 9)]
    [InlineData(RegonReport.OsFizycznaPkd, 9)]
    [InlineData(RegonReport.OsFizycznaListaJednLokalnych, 9)]
    [InlineData(RegonReport.JednLokalnaOsPrawnej, 14)]
    [InlineData(RegonReport.JednLokalnaOsPrawnej121, 14)]
    [InlineData(RegonReport.JednLokalnaOsPrawnejPkd, 14)]
    [InlineData(RegonReport.JednLokalnaOsFizycznej, 14)]
    [InlineData(RegonReport.JednLokalnaOsFizycznejPkd, 14)]
    [InlineData(RegonReport.TypPodmiotu, 0)]
    public void Reports_know_the_regon_length_they_need(RegonReport report, int length)
    {
        Assert.Equal(length, RegonReports.RegonLength(report));
        Assert.Equal(length, ExpectedReports.Single(e => e.Report == report).Length);
    }

    [Fact]
    public void An_unknown_report_value_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RegonReports.WireName((RegonReport)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegonReports.RegonLength((RegonReport)99));
    }

    [Fact]
    public void Include_all_is_the_union_of_the_flags()
    {
        Assert.Equal(RegonInclude.Pkd | RegonInclude.LocalUnits | RegonInclude.Partners, RegonInclude.All);
        Assert.Equal(7, (int)RegonInclude.All);
        Assert.Equal(0, (int)RegonInclude.None);
        Assert.True(RegonInclude.All.HasFlag(RegonInclude.Partners));
        Assert.False(RegonInclude.Pkd.HasFlag(RegonInclude.LocalUnits));
    }

    [Fact]
    public void Silo_and_type_values_follow_the_gus_codes()
    {
        // SilosID 1, 2, 3, 4 and 6 (research-gus-bir/api-facts.md section 5.3); the mapper parses the numbers into these members.
        Assert.Equal(1, (int)RegonSilo.Ceidg);
        Assert.Equal(2, (int)RegonSilo.Agricultural);
        Assert.Equal(3, (int)RegonSilo.Other);
        Assert.Equal(4, (int)RegonSilo.RemovedBefore2014);
        Assert.Equal(6, (int)RegonSilo.LegalPerson);
        Assert.Equal(0, (int)RegonSilo.Unknown);
        Assert.Equal(0, (int)RegonEntityType.Unknown);
        Assert.Equal(RegonEnvironment.Production, default(RegonEnvironment));
        Assert.Equal(RegonRateLimit.Strictest, default(RegonRateLimit));
    }

    [Fact]
    public void Authentication_exception_is_an_api_exception_with_the_standard_constructors()
    {
        var inner = new InvalidOperationException("inner");

        var plain = new RegonAuthenticationException();
        var withMessage = new RegonAuthenticationException("GUS did not accept the configured key.");
        var withInner = new RegonAuthenticationException("rejected", inner);

        Assert.IsAssignableFrom<PolishOpenDataApiException>(plain);
        Assert.IsAssignableFrom<PolishOpenDataException>(plain);
        Assert.Equal("GUS did not accept the configured key.", withMessage.Message);
        Assert.Null(withMessage.StatusCode);
        Assert.False(withMessage.IsTransient);
        Assert.Equal("rejected", withInner.Message);
        Assert.Same(inner, withInner.InnerException);
    }

    [Fact]
    public void Model_defaults_are_empty_not_null()
    {
        var details = new RegonDetails();
        var entity = new RegonEntity();
        var unit = new RegonLocalUnit();
        var pkd = new RegonPkd();
        var address = new RegonAddress();

        Assert.Equal(string.Empty, details.Name);
        Assert.NotNull(details.Address);
        Assert.Equal(RegonEntityType.Unknown, details.Type);
        Assert.Equal(RegonSilo.Unknown, details.Silo);
        Assert.Null(details.Regon);
        Assert.Null(details.Nip);
        Assert.Null(details.Pkd);
        Assert.Null(details.LocalUnits);
        Assert.Null(details.Partners);
        Assert.Null(details.HasCeidgActivity);
        Assert.NotNull(details.Fields);
        Assert.Empty(details.Fields);
        Assert.Equal(string.Empty, entity.Name);
        Assert.NotNull(entity.Address);
        Assert.Null(entity.Regon);
        Assert.Null(entity.EndDate);
        Assert.Equal(string.Empty, unit.Name);
        Assert.NotNull(unit.Address);
        Assert.Equal(RegonSilo.Unknown, unit.Silo);
        Assert.Equal(string.Empty, pkd.Code);
        Assert.False(pkd.IsPrimary);
        Assert.Null(pkd.Silo);
        Assert.Null(address.PostalCode);
        Assert.Null(address.Street);
        Assert.Null(address.CountryCode);
    }
}
