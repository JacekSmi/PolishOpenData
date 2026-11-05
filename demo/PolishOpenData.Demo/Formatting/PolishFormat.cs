using System.Globalization;

namespace PolishOpenData.Demo.Formatting;

/// <summary>
/// Numbers, prices and dates the way the page shows them: decimal comma, a non-breaking space between thousands and before the unit.
/// A hand-built <see cref="NumberFormatInfo"/> keeps the output the same whatever the browser language is,
/// and works with <c>InvariantGlobalization</c>, where <c>new CultureInfo("pl-PL")</c> throws.
/// </summary>
internal static class PolishFormat
{
    // U+00A0 NO-BREAK SPACE keeps "1 180 000 zł" on one line; zł is "z" + U+0142.
    private const string Nbsp = " ";
    private const string ZlotySuffix = Nbsp + "zł";

    private static readonly NumberFormatInfo Number = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = Nbsp,
        NumberGroupSizes = [3],
        NegativeSign = "-",
    };

    /// <summary>A price in whole zloty: <c>1 180 000 zł</c>.</summary>
    public static string Money(decimal zloty) => Whole(zloty) + ZlotySuffix;

    /// <summary>A price per square metre in whole zloty: <c>21 444 zł/m²</c>.</summary>
    public static string PerSquareMetre(decimal zlotyPerSquareMetre) => Whole(zlotyPerSquareMetre) + ZlotySuffix + "/m²";

    /// <summary>An area with one decimal: <c>54,2 m²</c> (a trailing zero stays: <c>54,0 m²</c>).</summary>
    public static string Area(decimal squareMetres) =>
        Math.Round(squareMetres, 1, MidpointRounding.AwayFromZero).ToString("N1", Number) + Nbsp + "m²";

    /// <summary>A date as <c>2025-12-30</c>.</summary>
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A count with thousands grouped: <c>1 234 567</c>.</summary>
    public static string Count(int count) => count.ToString("N0", Number);

    // Rounded away from zero so that a half zloty goes up, whichever way the formatter would round a tie.
    private static string Whole(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", Number);
}
