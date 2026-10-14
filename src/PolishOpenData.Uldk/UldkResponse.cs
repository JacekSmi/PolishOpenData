using System;
using System.Collections.Generic;
using System.Globalization;
using PolishOpenData.Internal;

namespace PolishOpenData.Uldk;

/// <summary>Parser of the plain-text ULDK answer.</summary>
internal static class UldkResponse
{
    /// <summary>
    /// Splits an answer into rows of <c>|</c>-separated fields. First line <c>0</c>: every following line is a row.
    /// A number <c>n</c> (several matches): the next <c>n</c> lines. <c>-1 brak wyników</c>: no row. Any other
    /// <c>-1 …</c> is a service error. The "not found" answer carries a stray second line that is ignored.
    /// </summary>
    public static IReadOnlyList<string[]> Parse(string body)
    {
        var lines = body.Split('\n');
        var first = lines[0].Trim();
        if (first.StartsWith("-1", StringComparison.Ordinal))
        {
            var message = first.Substring(2).Trim();
            if (message.Contains("brak wyników", StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            throw new PolishOpenDataApiException(message, null, "uldk_error", HttpHelpers.Truncate(body));
        }

        if (!int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            throw new PolishOpenDataApiException("ULDK returned a response that could not be read.", null, null, HttpHelpers.Truncate(body));
        }

        var rows = new List<string[]>();
        for (var i = 1; i < lines.Length && (count == 0 || rows.Count < count); i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length > 0)
            {
                rows.Add(line.Split('|'));
            }
        }

        return rows;
    }
}
