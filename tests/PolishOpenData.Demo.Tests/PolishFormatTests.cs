using System;
using System.Linq;
using PolishOpenData.Demo.Formatting;

namespace PolishOpenData.Demo.Tests;

public class PolishFormatTests
{
    // U+00A0 is the group separator and the space before the unit; zl = "z" + U+0142; m2 = "m" + U+00B2.
    private const string Zl = "z\u0142";
    private const string M2 = "m\u00B2";

    public static TheoryData<decimal, string> MoneyCases => new()
    {
        { 1_180_000m, "1\u00A0180\u00A0000\u00A0" + Zl },
        { 21_444m, "21\u00A0444\u00A0" + Zl },
        { 999m, "999\u00A0" + Zl },
        { 1_000m, "1\u00A0000\u00A0" + Zl },
        { 0m, "0\u00A0" + Zl },
        { -0m, "0\u00A0" + Zl },
        { -0.4m, "0\u00A0" + Zl },
        { 2.5m, "3\u00A0" + Zl },
        { -2.5m, "-3\u00A0" + Zl },
        { 21_444.5m, "21\u00A0445\u00A0" + Zl },
        { -1_180_000m, "-1\u00A0180\u00A0000\u00A0" + Zl },
        { 1_234_567_890_123m, "1\u00A0234\u00A0567\u00A0890\u00A0123\u00A0" + Zl },
        { decimal.MaxValue, "79\u00A0228\u00A0162\u00A0514\u00A0264\u00A0337\u00A0593\u00A0543\u00A0950\u00A0335\u00A0" + Zl },
    };

    public static TheoryData<decimal, string> PerSquareMetreCases => new()
    {
        { 21_444m, "21\u00A0444\u00A0" + Zl + "/" + M2 },
        { 21_443.5m, "21\u00A0444\u00A0" + Zl + "/" + M2 },
        { 21_444.5m, "21\u00A0445\u00A0" + Zl + "/" + M2 },
        { 21_444.49m, "21\u00A0444\u00A0" + Zl + "/" + M2 },
        { 950m, "950\u00A0" + Zl + "/" + M2 },
        { 0m, "0\u00A0" + Zl + "/" + M2 },
        { -0.4m, "0\u00A0" + Zl + "/" + M2 },
    };

    public static TheoryData<decimal, string> AreaCases => new()
    {
        { 54.2m, "54,2\u00A0" + M2 },
        { 54m, "54,0\u00A0" + M2 },
        { 0m, "0,0\u00A0" + M2 },
        { -0m, "0,0\u00A0" + M2 },
        { -0.04m, "0,0\u00A0" + M2 },
        { 54.25m, "54,3\u00A0" + M2 },
        { 54.24m, "54,2\u00A0" + M2 },
        { 1_234.56m, "1\u00A0234,6\u00A0" + M2 },
        { 1_000_000m, "1\u00A0000\u00A0000,0\u00A0" + M2 },
    };

    public static TheoryData<DateOnly, string> DateCases => new()
    {
        { new DateOnly(2025, 12, 30), "2025-12-30" },
        { new DateOnly(2025, 1, 5), "2025-01-05" },
        { DateOnly.MinValue, "0001-01-01" },
        { DateOnly.MaxValue, "9999-12-31" },
    };

    public static TheoryData<int, string> CountCases => new()
    {
        { 0, "0" },
        { 7, "7" },
        { 999, "999" },
        { 1_000, "1\u00A0000" },
        { 1_234_567, "1\u00A0234\u00A0567" },
        { -1_234, "-1\u00A0234" },
        { int.MaxValue, "2\u00A0147\u00A0483\u00A0647" },
        { int.MinValue, "-2\u00A0147\u00A0483\u00A0648" },
    };

    [Theory]
    [MemberData(nameof(MoneyCases))]
    public void Money_is_whole_zloty_with_a_non_breaking_space(decimal value, string expected) =>
        Assert.Equal(expected, PolishFormat.Money(value));

    [Theory]
    [MemberData(nameof(PerSquareMetreCases))]
    public void PerSquareMetre_rounds_away_from_zero_to_whole_zloty(decimal value, string expected) =>
        Assert.Equal(expected, PolishFormat.PerSquareMetre(value));

    [Theory]
    [MemberData(nameof(AreaCases))]
    public void Area_has_one_decimal_with_a_comma(decimal value, string expected) =>
        Assert.Equal(expected, PolishFormat.Area(value));

    [Theory]
    [MemberData(nameof(DateCases))]
    public void Date_is_year_month_day(DateOnly value, string expected) =>
        Assert.Equal(expected, PolishFormat.Date(value));

    [Theory]
    [MemberData(nameof(CountCases))]
    public void Count_is_grouped_in_thousands(int value, string expected) =>
        Assert.Equal(expected, PolishFormat.Count(value));

    // A no-break space looks like a space in an editor and in a diff; this pins the character itself, not its look.
    [Fact]
    public void Separators_are_no_break_spaces_never_ASCII_spaces()
    {
        // Expected counts: Money 2 groups + 1 before the unit; per square metre 1 group + 1; area 1 group + 1; count 2 groups.
        var cases = new (string Text, int NoBreakSpaces)[]
        {
            (PolishFormat.Money(1_180_000m), 3),
            (PolishFormat.PerSquareMetre(21_444m), 2),
            (PolishFormat.Area(1_234.5m), 2),
            (PolishFormat.Count(1_234_567), 2),
        };

        foreach (var (text, noBreakSpaces) in cases)
        {
            Assert.DoesNotContain(' ', text);
            Assert.Contains('\u00A0', text);
            Assert.Equal(noBreakSpaces, text.Count(c => c == '\u00A0'));
        }
    }

    // en-US is the culture that would expose a formatter that leans on the current culture (comma groups, dot decimals);
    // pl-PL is close to the target, so it alone proves little; th-TH writes years in the Buddhist era (2568). "" is the invariant culture.
    [Theory]
    [InlineData("pl-PL")]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    [InlineData("")]
    public void Output_does_not_depend_on_the_current_culture(string culture)
    {
        var all = CultureScope.Run(culture, () => string.Join(
            '|',
            PolishFormat.Money(1_180_000m),
            PolishFormat.PerSquareMetre(21_444.5m),
            PolishFormat.Area(54.2m),
            PolishFormat.Date(new DateOnly(2025, 12, 30)),
            PolishFormat.Count(1_234_567)));

        Assert.Equal(
            "1\u00A0180\u00A0000\u00A0" + Zl + "|21\u00A0445\u00A0" + Zl + "/" + M2 + "|54,2\u00A0" + M2 + "|2025-12-30|1\u00A0234\u00A0567",
            all);
    }
}
