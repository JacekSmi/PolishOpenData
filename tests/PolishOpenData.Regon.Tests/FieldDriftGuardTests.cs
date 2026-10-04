using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

// The drift guard: the 427 field occurrences of the 17 BIR12 reports (tests/Fixtures/regon/bir12-field-names.tsv) must each be either
// read by a table of RegonFields or on its list of deliberately raw-only fields, and nothing stale may stay in the tables. When GUS adds,
// renames or drops a field, the TSV is updated and these tests say which table needs a line.
// The TSV was reconstructed from the BIR11 schemas plus the documented BIR12 differences, not copied from the BIR12 XSDs
// (GUS BIR1.2 manual; the field list is tests/Fixtures/regon/bir12-field-names.tsv); see tests/Fixtures/README.md.
public class FieldDriftGuardTests
{
    // fields per report, as in tests/Fixtures/regon/bir12-field-names.tsv (sum 427)
    private static readonly (string Report, int Count)[] ExpectedCounts =
    [
        ("BIR12OsPrawna", 55),
        ("BIR12OsPrawnaPkd", 4),
        ("BIR12OsPrawnaListaJednLokalnych", 26),
        ("BIR12OsPrawnaSpCywilnaWspolnicy", 5),
        ("BIR12OsFizycznaDaneOgolne", 22),
        ("BIR12OsFizycznaDzialalnoscCeidg", 44),
        ("BIR12OsFizycznaDzialalnoscRolnicza", 36),
        ("BIR12OsFizycznaDzialalnoscPozostala", 42),
        ("BIR12OsFizycznaDzialalnoscSkreslonaDo20141108", 35),
        ("BIR12OsFizycznaPkd", 7),
        ("BIR12OsFizycznaListaJednLokalnych", 28),
        ("BIR12JednLokalnaOsFizycznej", 39),
        ("BIR12JednLokalnaOsFizycznejPkd", 5),
        ("BIR12JednLokalnaOsPrawnej", 36),
        ("BIR121JednLokalnaOsPrawnej", 38),
        ("BIR12JednLokalnaOsPrawnejPkd", 4),
        ("BIR12TypPodmiotu", 1),
    ];

    private static List<(string Report, string Field)> ReadTsv()
    {
        var result = new List<(string Report, string Field)>();
        foreach (var raw in Fixture.Read("regon/bir12-field-names.tsv").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            Assert.Equal(2, parts.Length);
            result.Add((parts[0], parts[1]));
        }

        return result;
    }

    private static HashSet<string> AllFieldNames() => new(ReadTsv().Select(entry => entry.Field), StringComparer.Ordinal);

    private static string[] RegonFieldTables() =>
        typeof(RegonFields).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string[]))
            .Select(field => field.Name)
            .ToArray();

    [Fact]
    public void Field_list_has_427_names_in_17_reports()
    {
        var entries = ReadTsv();

        Assert.Equal(427, entries.Count);
        Assert.Equal(17, entries.Select(entry => entry.Report).Distinct(StringComparer.Ordinal).Count());
        foreach (var (report, count) in ExpectedCounts)
        {
            Assert.Equal(count, entries.Count(entry => string.Equals(entry.Report, report, StringComparison.Ordinal)));
        }

        Assert.Equal(427, ExpectedCounts.Sum(expected => expected.Count));
        Assert.Equal(entries.Count, entries.Select(entry => entry.Report + "\t" + entry.Field).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_field_is_mapped_or_raw_only()
    {
        var unknown = AllFieldNames()
            .Where(name => !RegonFields.Mapped.Contains(name) && !RegonFields.RawOnly.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(unknown.Length == 0, "Fields that are neither mapped nor raw-only: " + string.Join(", ", unknown));
    }

    [Fact]
    public void No_field_is_both_mapped_and_raw_only()
    {
        var both = RegonFields.Mapped.Where(name => RegonFields.RawOnly.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.True(both.Length == 0, "Fields in both sets: " + string.Join(", ", both));
    }

    [Fact]
    public void No_table_entry_is_missing_from_the_field_list()
    {
        var known = AllFieldNames();

        var stale = RegonFields.Mapped.Concat(RegonFields.RawOnly)
            .Where(name => !known.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(stale.Length == 0, "Names that no report has (a typo or a field GUS dropped): " + string.Join(", ", stale));
    }

    [Fact]
    public void Report_names_match_the_enum_wire_names()
    {
        var fromTsv = ReadTsv().Select(entry => entry.Report).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var fromEnum = typeof(RegonReport).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => RegonReports.WireName((RegonReport)field.GetValue(null)!))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(17, fromEnum.Length);
        Assert.Equal(fromTsv, fromEnum);
    }

    [Fact]
    public void Counts_are_pinned()
    {
        var distinct = new HashSet<string>(RegonFields.Mapped, StringComparer.Ordinal);
        distinct.UnionWith(RegonFields.RawOnly);

        Assert.Equal(54, RegonFieldTables().Length);
        Assert.Equal(169, RegonFields.Mapped.Count);
        Assert.Equal(62, RegonFields.RawOnly.Count);
        Assert.Equal(231, distinct.Count);
        Assert.Equal(231, AllFieldNames().Count);
    }

    [Fact]
    public void Every_synthetic_report_fixture_field_is_known()
    {
        var known = AllFieldNames();
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "regon");
        var files = Directory.GetFiles(folder, "synthetic-report-*.xml")
            .Select(Path.GetFileName)
            .Where(name => name is not null && !name.StartsWith("synthetic-report-error-", StringComparison.Ordinal))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // the ten synthetic report fixtures; a later fixture is checked too
        foreach (var expected in new[]
        {
            "synthetic-report-osprawna-gus.xml", "synthetic-report-osprawna-pkd.xml", "synthetic-report-osprawna-lista-jednostek.xml",
            "synthetic-report-osprawna-wspolnicy.xml", "synthetic-report-fiz-daneogolne.xml", "synthetic-report-fiz-ceidg.xml",
            "synthetic-report-fiz-rolnicza.xml", "synthetic-report-fiz-pkd.xml", "synthetic-report-jednlokalna-lp-121.xml",
            "synthetic-report-jednlokalna-lf.xml",
        })
        {
            Assert.Contains(expected, files);
        }

        foreach (var file in files)
        {
            var names = XDocument.Parse(File.ReadAllText(Path.Combine(folder, file)))
                .Descendants()
                .Select(element => element.Name.LocalName)
                .Where(name => name != "root" && name != "dane")
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.NotEmpty(names);
            var unknown = names.Where(name => !known.Contains(name)).ToArray();
            Assert.True(unknown.Length == 0, file + " has fields that no BIR12 report has: " + string.Join(", ", unknown));
        }
    }

    [Fact]
    public void The_osprawna_fixture_carries_all_55_fields_of_the_report()
    {
        var expected = ReadTsv()
            .Where(entry => string.Equals(entry.Report, "BIR12OsPrawna", StringComparison.Ordinal))
            .Select(entry => entry.Field)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var actual = XDocument.Parse(RegonTestResponses.Inner("synthetic-report-osprawna-gus"))
            .Descendants("dane")
            .Single()
            .Elements()
            .Select(element => element.Name.LocalName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(55, expected.Length);
        Assert.Equal(expected, actual);
    }
}
