using System.Globalization;

namespace PolishOpenData.Demo.Formatting;

/// <summary>
/// Numbers, prices and dates the way the page shows them: decimal comma, a non-breaking space between thousands and before the unit.
/// A hand-built <see cref="NumberFormatInfo"/> keeps the output the same whatever the browser language is,
/// and works with <c>InvariantGlobalization</c>, where <c>new CultureInfo("pl-PL")</c> throws.
/// </summary>
internal static class PolishFormat
{
    // U+00A0 (no-break space) is the group separator and the space before a unit, so a price never wraps inside; the unit "zloty" is z + U+0142 (l with stroke).
    private const string Nbsp = "\u00A0";
    private const string ZlotySuffix = Nbsp + "z\u0142";

    private static readonly NumberFormatInfo Number = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = Nbsp,
        NumberGroupSizes = [3],
        NegativeSign = "-",
    };

    /// <summary>A price in whole zloty: groups and unit are separated by U+00A0, e.g. "1 180 000 zl" with the l written as U+0142.</summary>
    public static string Money(decimal zloty) => Whole(zloty) + ZlotySuffix;

    /// <summary>A price per square metre in whole zloty: "21 444 zl/m2" with U+00A0 as separator, l as U+0142 and 2 as U+00B2.</summary>
    public static string PerSquareMetre(decimal zlotyPerSquareMetre) => Whole(zlotyPerSquareMetre) + ZlotySuffix + "/m\u00B2";

    /// <summary>An area with one decimal: "54,2 m2" with U+00A0 before the unit and 2 as U+00B2 (a trailing zero stays: "54,0 m2").</summary>
    public static string Area(decimal squareMetres) =>
        Math.Round(squareMetres, 1, MidpointRounding.AwayFromZero).ToString("N1", Number) + Nbsp + "m\u00B2";

    /// <summary>A date as <c>2025-12-30</c>.</summary>
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A count with thousands grouped: <c>1 234 567</c>.</summary>
    public static string Count(int count) => count.ToString("N0", Number);

    /// <summary>A number in whole units with thousands grouped by U+00A0: <c>21 444</c>. Rounded away from zero so that a half goes up, whichever way the formatter would round a tie.</summary>
    public static string Whole(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", Number);
}
