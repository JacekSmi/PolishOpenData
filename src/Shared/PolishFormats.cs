using System;
using System.Globalization;
using System.Text;

namespace PolishOpenData.Internal;

/// <summary>Parsing of date, time and number formats used by Polish registries.</summary>
internal static class PolishFormats
{
    /// <summary>Parses a date with an exact invariant format such as <c>dd.MM.yyyy</c>.</summary>
    public static DateOnly ParseDate(string value, string format) =>
        DateOnly.ParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Parses a Warsaw wall-clock timestamp such as <c>24-09-2026 02:36:01</c>.</summary>
    public static DateTimeOffset ParseWarsawDateTime(string value, string format) =>
        WarsawTime.FromLocal(DateTime.ParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None));

    /// <summary>Parses a decimal with a comma separator and optional spaces, e.g. <c>1451177561,25</c>.</summary>
    public static bool TryParseDecimal(string? value, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var builder = new StringBuilder(value!.Length);
        foreach (var c in value)
        {
            if (c == ',')
            {
                builder.Append('.');
            }
            else if (!char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
        }

        return decimal.TryParse(
            builder.ToString(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out result);
    }
}
