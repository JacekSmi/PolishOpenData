using System;
using System.Globalization;

namespace PolishOpenData.Mcp;

internal static class ToolInput
{
    public static bool TryParseDate(string? value, out DateOnly? date, out string? error)
    {
        date = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
            return true;
        }

        error = "'" + value + "' is not a date in YYYY-MM-DD format.";
        return false;
    }
}
