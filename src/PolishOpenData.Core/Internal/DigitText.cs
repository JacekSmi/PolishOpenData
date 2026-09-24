using System;

namespace PolishOpenData.Internal;

/// <summary>Shared lenient digit extraction for identifier value types.</summary>
internal static class DigitText
{
    /// <summary>
    /// Copies the digits of <paramref name="input"/> into <paramref name="destination"/>, skipping spaces and dashes
    /// and (optionally) a leading "PL" country prefix. Returns the number of digits, or -1 when the input contains any
    /// other character or more digits than <paramref name="destination"/> can hold.
    /// </summary>
    public static int Extract(ReadOnlySpan<char> input, Span<char> destination, bool allowPlPrefix)
    {
        var s = input.Trim();
        if (allowPlPrefix && s.Length >= 2 && (s[0] == 'P' || s[0] == 'p') && (s[1] == 'L' || s[1] == 'l'))
        {
            s = s.Slice(2);
        }

        var count = 0;
        foreach (var c in s)
        {
            if (c == ' ' || c == '-')
            {
                continue;
            }

            if (c < '0' || c > '9' || count == destination.Length)
            {
                return -1;
            }

            destination[count++] = c;
        }

        return count;
    }
}
