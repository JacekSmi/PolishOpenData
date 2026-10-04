using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

public class FixtureFramingTests
{
    private static string FixtureFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "regon");

    // The sizes of the captured bodies. A different size means a tool normalised line endings or re-saved the file.
    [Theory]
    [InlineData("zaloguj-ok", 650)]
    [InlineData("search-regon-gus", 1549)]
    [InlineData("wyloguj-true", 634)]
    [InlineData("search-empty", 659)]
    [InlineData("getvalue-empty", 601)]
    [InlineData("fault-sender-500", 896)]
    public void Recorded_fixtures_have_the_captured_sizes(string name, int size)
    {
        Assert.Equal(size, Fixture.ReadBytes("regon/" + name + ".mtom").Length);
    }

    [Fact]
    public void Mtom_fixtures_keep_crlf_framing()
    {
        var files = Directory.GetFiles(FixtureFolder, "*.mtom");
        Assert.True(files.Length >= 6, "expected the six recorded fixtures, found " + files.Length);
        foreach (var file in files)
        {
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            var name = Path.GetFileName(file);
            Assert.True(text.StartsWith("\r\n--uuid:", StringComparison.Ordinal), name + " must start with CRLF and the boundary");
            Assert.True(text.IndexOf("\r\n--uuid:", 2, StringComparison.Ordinal) > 0, name + " must hold the closing delimiter after a CRLF");
            Assert.True(text.EndsWith("--\r\n", StringComparison.Ordinal), name + " must end with the closing delimiter and CRLF");
            Assert.True(text.IndexOf("\r\nContent-ID: <http://tempuri.org/0>\r\n", StringComparison.Ordinal) > 0, name + " must carry CRLF part headers");
        }
    }

    [Fact]
    public void Every_mtom_fixture_has_a_two_line_headers_file()
    {
        foreach (var file in Directory.GetFiles(FixtureFolder, "*.mtom"))
        {
            var headersFile = Path.ChangeExtension(file, ".headers");
            Assert.True(File.Exists(headersFile), "missing " + headersFile);
            var text = File.ReadAllText(headersFile);
            Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
            Assert.EndsWith("\n", text, StringComparison.Ordinal);
            var lines = text.TrimEnd('\n').Split('\n');
            Assert.Equal(2, lines.Length);
            Assert.Matches("^HTTP [1-5][0-9][0-9]$", lines[0]);
            Assert.StartsWith("Content-Type: multipart/related; type=\"application/xop+xml\"", lines[1], StringComparison.Ordinal);
        }
    }

    // The synthetic files hold Polish letters. A tool that writes them in a legacy code page (cp1250 in a Windows editor or
    // in a shell that re-encodes the heredocs) leaves bytes that are not UTF-8; GUS-shaped parsing tests would then fail much later.
    [Fact]
    public void Synthetic_fixtures_are_strict_utf8()
    {
        var strict = new UTF8Encoding(false, true);
        var files = new List<string>(Directory.GetFiles(FixtureFolder, "synthetic-*"));
        files.Add(Path.Combine(FixtureFolder, "bir12-field-names.tsv"));
        Assert.True(files.Count >= 22, "expected the synthetic fixtures and the field list, found " + files.Count);
        foreach (var file in files)
        {
            var text = strict.GetString(File.ReadAllBytes(file));
            Assert.DoesNotContain("�", text, StringComparison.Ordinal);
        }

        Assert.Contains("spółką", Fixture.Read("regon/synthetic-report-error-21.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Field_list_has_427_lines()
    {
        var lines = Fixture.Read("regon/bir12-field-names.tsv").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(427, lines.Length);

        var perReport = new Dictionary<string, int>(StringComparer.Ordinal);
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var cells = line.Split('\t');
            Assert.Equal(2, cells.Length);
            Assert.NotEmpty(cells[0]);
            Assert.NotEmpty(cells[1]);
            Assert.True(pairs.Add(line), "duplicate line " + line);
            perReport[cells[0]] = perReport.TryGetValue(cells[0], out var count) ? count + 1 : 1;
        }

        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["BIR12OsPrawna"] = 55,
            ["BIR12OsPrawnaPkd"] = 4,
            ["BIR12OsPrawnaListaJednLokalnych"] = 26,
            ["BIR12OsPrawnaSpCywilnaWspolnicy"] = 5,
            ["BIR12OsFizycznaDaneOgolne"] = 22,
            ["BIR12OsFizycznaDzialalnoscCeidg"] = 44,
            ["BIR12OsFizycznaDzialalnoscRolnicza"] = 36,
            ["BIR12OsFizycznaDzialalnoscPozostala"] = 42,
            ["BIR12OsFizycznaDzialalnoscSkreslonaDo20141108"] = 35,
            ["BIR12OsFizycznaPkd"] = 7,
            ["BIR12OsFizycznaListaJednLokalnych"] = 28,
            ["BIR12JednLokalnaOsFizycznej"] = 39,
            ["BIR12JednLokalnaOsFizycznejPkd"] = 5,
            ["BIR12JednLokalnaOsPrawnej"] = 36,
            ["BIR121JednLokalnaOsPrawnej"] = 38,
            ["BIR12JednLokalnaOsPrawnejPkd"] = 4,
            ["BIR12TypPodmiotu"] = 1,
        };
        Assert.Equal(expected.Count, perReport.Count);
        foreach (var pair in expected)
        {
            Assert.True(perReport.TryGetValue(pair.Key, out var actual), "report missing: " + pair.Key);
            Assert.Equal(pair.Value, actual);
        }
    }

    [Fact]
    public void Synthetic_fixtures_are_lf_only()
    {
        var files = Directory.GetFiles(FixtureFolder, "synthetic-*");
        Assert.True(files.Length >= 21, "expected the 21 synthetic fixtures, found " + files.Length);
        foreach (var file in files)
        {
            Assert.DoesNotContain("\r", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Data_date_fixture_is_the_bare_text()
    {
        Assert.Equal("19-10-2018", Fixture.Read("regon/synthetic-getvalue-standanych.txt"));
    }
}
