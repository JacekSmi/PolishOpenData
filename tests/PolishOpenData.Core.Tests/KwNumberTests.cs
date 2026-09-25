using System;
using System.Linq;
using System.Text.Json;
using PolishOpenData;

namespace PolishOpenData.Core.Tests;

public class KwNumberTests
{
    // KW numbers of municipally owned properties from municipal sale/lease listings (art. 35 u.g.n.): pultusk.pl, gozdowo.eu, wieliczka.eu, gizycko.info, bip.miedzychod.pl, stare-miasto.pl; plus the algorytm.org example WL1A/00272852/9.
    [Theory]
    [InlineData("WL1A/00272852/9")]
    [InlineData("OS1U/00016474/4")]
    [InlineData("PL1E/00015351/7")]
    [InlineData("KR1I/00010902/4")]
    [InlineData("KR1I/00019031/0")]
    [InlineData("KR1I/00012800/3")]
    [InlineData("KR1I/00020313/1")]
    [InlineData("OL1G/00013813/1")]
    [InlineData("OL1G/00057552/3")]
    [InlineData("PO2A/00020066/2")]
    [InlineData("KN1N/00055699/0")]
    public void Real_published_numbers_are_valid(string input)
    {
        Assert.True(KwNumber.TryParse(input, out var kw));
        Assert.Equal(input, kw.ToString());
        Assert.NotNull(kw.Court);
        Assert.True(kw.Court!.IsCurrent);
    }

    [Theory]
    [InlineData("wl1a/00272852/9")]
    [InlineData(" WL1A / 00272852 / 9 ")]
    [InlineData("WL1A-00272852-9")]
    [InlineData("WL1A002728529")]
    public void Accepts_lenient_formatting(string input)
    {
        Assert.Equal("WL1A/00272852/9", KwNumber.Parse(input).ToString());
    }

    [Theory]
    [InlineData("WL1A/00272852-9")]
    [InlineData("wl1a-00272852/9")]
    public void Mixed_separators_are_accepted_and_normalised(string input)
    {
        // Lenient on purpose: each of the two separator positions takes '/' or '-' on its own, so a mix is valid.
        // Only "both separators or none" is enforced. The canonical form always uses '/'.
        Assert.True(KwNumber.TryParse(input, out var kw));
        Assert.Equal("WL1A/00272852/9", kw.ToString());
    }

    [Fact]
    public void Exposes_parts_and_court()
    {
        var kw = KwNumber.Parse("WL1A/00272852/9");
        Assert.Equal("WL1A", kw.CourtCode);
        Assert.Equal("00272852", kw.Number);
        Assert.Equal(9, kw.CheckDigit);
        Assert.Contains("Aleksandrowie Kujawskim", kw.Court!.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Worked_example_from_spec()
    {
        // 31·1 + 22·3 + 1·7 + 11·1 + 0 + 0 + 2·1 + 7·3 + 2·7 + 8·1 + 5·3 + 2·7 = 189 -> 9
        Assert.Equal(9, KwNumber.ComputeCheckDigit("WL1A", "00272852"));
    }

    [Theory]
    [InlineData("KR2I", "00012345", 2)]   // historical code (Niepołomice, until 2026-06-30)
    [InlineData("GD2W", "00001234", 0)]
    [InlineData("WA1M", "00012345", 1)]
    [InlineData("GL1X", "00000100", 1)]   // X = 10
    [InlineData("AA1A", "00000001", 9)]   // well-formed but not in the registry
    public void Computes_check_digits(string code, string number, int expected)
    {
        Assert.Equal(expected, KwNumber.ComputeCheckDigit(code, number));
        Assert.True(KwNumber.TryComputeCheckDigit(code, number, out var digit));
        Assert.Equal(expected, digit);
    }

    [Fact]
    public void Historical_code_parses_and_reports_it()
    {
        var kw = KwNumber.Parse("KR2I/00012345/2");
        Assert.NotNull(kw.Court);
        Assert.False(kw.Court!.IsCurrent);
        Assert.Equal(new DateOnly(2026, 6, 30), kw.Court.LastValidOn);
    }

    [Fact]
    public void Well_formed_unknown_code_parses_without_court()
    {
        var kw = KwNumber.Parse("AA1A/00000001/9");
        Assert.Null(kw.Court);
    }

    [Theory]
    [InlineData("0S1U/00016474/4")]   // zero instead of letter O (printed on pultusk.pl, 2026-08-20)
    [InlineData("KR11/00020313/1")]   // digit 1 instead of letter I
    [InlineData("KR1I/00012800/2")]   // wrong check digit
    [InlineData("WL1A/00272852/8")]
    [InlineData("WQ1A/00272852/9")]   // Q is not used
    [InlineData("WL0A/00272852/9")]   // third character must be 1-9
    [InlineData("WL1A/0027285/9")]    // 7-digit number
    [InlineData("WL1A/002728521/9")]
    [InlineData("WL1A//00272852/9")]
    [InlineData("")]
    public void Rejects_invalid_numbers(string input)
    {
        Assert.False(KwNumber.TryParse(input, out var kw));
        Assert.True(kw.IsEmpty);
        Assert.Throws<FormatException>(() => KwNumber.Parse(input));
    }

    [Fact]
    public void Suggests_letter_O_for_zero_in_code()
    {
        var suggestions = KwNumber.SuggestCorrections("0S1U/00016474/4");
        Assert.Equal(new[] { "OS1U/00016474/4" }, suggestions.Select(s => s.ToString()));
    }

    [Fact]
    public void Suggests_letter_I_for_one_in_code()
    {
        var suggestions = KwNumber.SuggestCorrections("KR11/00020313/1");
        Assert.Equal(new[] { "KR1I/00020313/1" }, suggestions.Select(s => s.ToString()));
    }

    [Fact]
    public void Converts_confusable_letters_in_number_part()
    {
        var suggestions = KwNumber.SuggestCorrections("WL1A/OO272852/9");
        Assert.Equal(new[] { "WL1A/00272852/9" }, suggestions.Select(s => s.ToString()));
    }

    [Theory]
    [InlineData("KR1I/00012800/2")]   // only the check digit is wrong: no guessing
    [InlineData("WL1A/00272852/9")]   // already valid: nothing to correct
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData(null)]
    public void No_suggestions(string? input)
    {
        Assert.Empty(KwNumber.SuggestCorrections(input));
    }

    [Fact]
    public void Compute_rejects_bad_arguments()
    {
        Assert.Throws<ArgumentException>(() => KwNumber.ComputeCheckDigit("WL1", "00272852"));
        Assert.Throws<ArgumentException>(() => KwNumber.ComputeCheckDigit("WL1A", "0027285X"));
        Assert.False(KwNumber.TryComputeCheckDigit(null, "00272852", out _));
    }

    [Fact]
    public void Serializes_as_canonical_string()
    {
        Assert.Equal("\"WL1A/00272852/9\"", JsonSerializer.Serialize(KwNumber.Parse("wl1a002728529")));
        Assert.Equal(KwNumber.Parse("WL1A/00272852/9"), JsonSerializer.Deserialize<KwNumber>("\"WL1A/00272852/9\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<KwNumber>("\"WL1A/00272852/8\""));
    }
}
