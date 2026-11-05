using System;
using PolishOpenData.Demo.Formatting;

namespace PolishOpenData.Demo.Tests;

public class PolishFormatTests
{
    // U+00A0 is the group separator and the space before the unit; zl = "z" + U+0142; m2 = "m" + U+00B2.
    private const string Zl = "zł";
    private const string M2 = "m²";

    public static TheoryData<decimal, string> MoneyCases => new()
    {
        { 1_180_000m, "1 180 000 " + Zl },
        { 21_444m, "21 444 " + Zl },
        { 999m, "999 " + Zl },
        { 1_000m, "1 000 " + Zl },
        { 0m, "0 " + Zl },
        { -0m, "0 " + Zl },
        { -0.4m, "0 " + Zl },
        { 2.5m, "3 " + Zl },
        { -2.5m, "-3 " + Zl },
        { 21_444.5m, "21 445 " + Zl },
        { -1_180_000m, "-1 180 000 " + Zl },
        { 1_234_567_890_123m, "1 234 567 890 123 " + Zl },
        { decimal.MaxValue, "79 228 162 514 264 337 593 543 950 335 " + Zl },
    };

    public static TheoryData<decimal, string> PerSquareMetreCases => new()
    {
        { 21_444m, "21 444 " + Zl + "/" + M2 },
        { 21_443.5m, "21 444 " + Zl + "/" + M2 },
        { 21_444.5m, "21 445 " + Zl + "/" + M2 },
        { 21_444.49m, "21 444 " + Zl + "/" + M2 },
        { 950m, "950 " + Zl + "/" + M2 },
        { 0m, "0 " + Zl + "/" + M2 },
        { -0.4m, "0 " + Zl + "/" + M2 },
    };

    public static TheoryData<decimal, string> AreaCases => new()
    {
        { 54.2m, "54,2 " + M2 },
        { 54m, "54,0 " + M2 },
        { 0m, "0,0 " + M2 },
        { -0m, "0,0 " + M2 },
        { -0.04m, "0,0 " + M2 },
        { 54.25m, "54,3 " + M2 },
        { 54.24m, "54,2 " + M2 },
        { 1_234.56m, "1 234,6 " + M2 },
        { 1_000_000m, "1 000 000,0 " + M2 },
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
        { 1_000, "1 000" },
        { 1_234_567, "1 234 567" },
        { -1_234, "-1 234" },
        { int.MaxValue, "2 147 483 647" },
        { int.MinValue, "-2 147 483 648" },
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
            "1 180 000 " + Zl + "|21 445 " + Zl + "/" + M2 + "|54,2 " + M2 + "|2025-12-30|1 234 567",
            all);
    }
}
