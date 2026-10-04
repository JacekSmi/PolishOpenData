using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

public class FixtureFramingTests
{
    private static string FixtureFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "regon");

    // The sizes and SHA-256 hashes of the captured bodies and their .headers files. A different size means a tool normalised
    // line endings or re-saved the file; a different hash means the recorded bytes were edited, which they never are.
    [Theory]
    [InlineData("zaloguj-ok", 650, "7ec2a614020b9c07beb0a1832b75f39dfc8765e1b92d9f406a3d958607eb734b", "5f074a194da92130da2a0eb5da81eabea486377b496263e68aed60e11b60bd6c")]
    [InlineData("search-regon-gus", 1549, "e1b6b12440af87084f09eeb3b9b38c32f463938a490e9639c3bb0e8976c948c9", "c13c99a8f93b0e640abf9070c73cb2b79dbe2e78d46381522508f86a206b1728")]
    [InlineData("wyloguj-true", 634, "c508d3f29191b116553d9654a0dbb2a2447b530a3b7f5e704b3ef46980100edb", "599ce9112aa54fc380853b389773bd7e718787c18858b0deefd0c3a530cb09b7")]
    [InlineData("search-empty", 659, "57efb2f2f5e8773fc63a049d6d12bf168efc4c9049505316b74bc8e74d5207d2", "3fbb7e1d09b8cc61b21613b19b3b70b96828601cd8e6c08f16fff171e4839a6a")]
    [InlineData("getvalue-empty", 601, "6a057dc63a84cda9da79c68afb430cfecc622ac0bb1244d4fc31dfa87ff36258", "c01e2c1d490e2077f0f740c93b2a58fa8510995299b1e6ea247a6f35afba4ef4")]
    [InlineData("fault-sender-500", 896, "fda9e9946bb84eab50860ba6b7a0d752c170031fb8c8f013304ecc0e9539207a", "b1e3b13fd8cdd9966a8e38b0ec991b7eef3353fe6af93a08ae32f063b0721d0e")]
    [InlineData("report-osprawna-gus", 5155, "75e025da6bd06fb2add92f3b39207d270c3d36fd1f8b525ddc809637e9199819", "732727f5116a7027a8fdfc8d6d4db475d6bc97c4c7339a718718952ddc077cab")]
    [InlineData("getvalue-standanych", 627, "5037a6816df235bdf32e9850703eadbe6bf74c47b235049c9d518c0bb387b1a8", "77666628811409eab724040f1cf7556f2bd79691ccdc7177f3d3844d26c9080c")]
    public void Recorded_fixtures_have_the_captured_sizes_and_hashes(string name, int size, string mtomSha256, string headersSha256)
    {
        var mtom = Fixture.ReadBytes("regon/" + name + ".mtom");
        Assert.Equal(size, mtom.Length);
        Assert.Equal(mtomSha256, Sha256Hex(mtom));
        Assert.Equal(headersSha256, Sha256Hex(Fixture.ReadBytes("regon/" + name + ".headers")));
    }

    private static string Sha256Hex(byte[] bytes)
    {
#if NET
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
#else
        // net472 has neither SHA256.HashData nor Convert.ToHexStringLower
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
#endif
    }

    [Fact]
    public void Mtom_fixtures_keep_crlf_framing()
    {
        // the eight recorded answers: six from the first capture, plus the full report and the data date recorded on 2026-10-04
        var files = Directory.GetFiles(FixtureFolder, "*.mtom");
        Assert.True(files.Length == 8, "expected the eight recorded fixtures, found " + files.Length);
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
        files.Add(Path.Combine(FixtureFolder, "observed-field-names-test-env.tsv"));
        Assert.True(files.Count >= 24, "expected the synthetic fixtures and the field list, found " + files.Count);
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
        Assert.True(files.Length >= 22, "expected the 22 synthetic fixtures, found " + files.Length);
        foreach (var file in files)
        {
            Assert.DoesNotContain("\r", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    // The field names seen live on the test environment (no values): report<TAB>name, LF only, 97 lines in 5 reports.
    [Fact]
    public void Observed_field_name_list_has_97_lf_lines()
    {
        var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(Path.Combine(FixtureFolder, "observed-field-names-test-env.tsv")));
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        var lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal(97, lines.Length);

        var reports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var cells = line.Split('\t');
            Assert.Equal(2, cells.Length);
            Assert.NotEmpty(cells[0]);
            Assert.NotEmpty(cells[1]);
            reports.Add(cells[0]);
        }

        Assert.Equal(5, reports.Count);
    }

    [Fact]
    public void Data_date_fixture_is_the_bare_text()
    {
        Assert.Equal("19-10-2018", Fixture.Read("regon/synthetic-getvalue-standanych.txt"));
    }
}
