using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Unicode;
using PolishOpenData.Demo.Search;
using PolishOpenData.Geo;

namespace PolishOpenData.Demo.State;

/// <summary>
/// What a shareable link to the page carries: the picked point, the radius, the period and an optional label (the address text).
/// The link is the fragment <c>#lat=52.22850&amp;lon=21.01720&amp;r=500&amp;m=24&amp;q=Warszawa%2C%20Krucza%2050</c>;
/// everything in it is read back as untrusted text, so <see cref="TryParse"/> checks every part and never throws.
/// </summary>
public sealed record PlaceUrlState(Wgs84Point Point, int RadiusMeters, int PeriodMonths, string? Label)
{
    /// <summary>The radius used when a link does not say (metres).</summary>
    public const int DefaultRadius = 500;

    /// <summary>The period used when a link does not say (months).</summary>
    public const int DefaultMonths = 24;

    private const int MaxLabelLength = 100;

    /// <summary>The radii the page offers, in metres.</summary>
    public static IReadOnlyList<int> Radii { get; } = [250, 500, 1000, 2000];

    /// <summary>The periods the page offers, in months.</summary>
    public static IReadOnlyList<int> Months { get; } = [12, 24, 60];

    /// <summary>
    /// The fragment for this state, with the leading <c>#</c>: coordinates with 5 decimals (about a metre), invariant culture,
    /// and <c>q</c> last, left out when the label is empty. The label goes through the same clean-up as <see cref="TryParse"/>,
    /// so a link never carries more than the capped label. Reading the fragment back gives an equal state only up to that clean-up
    /// and the rounding of the point to 5 decimals (a lone surrogate in the label also becomes U+FFFD): a caller that wants to know
    /// whether the link changed must compare fragments (or the rounded point), not a parsed state with an unrounded one.
    /// </summary>
    public string ToFragment()
    {
        var fragment = string.Create(CultureInfo.InvariantCulture, $"#lat={Point.Latitude:F5}&lon={Point.Longitude:F5}&r={RadiusMeters}&m={PeriodMonths}");
        return CleanLabel(Label) is { } label ? fragment + "&q=" + Uri.EscapeDataString(label) : fragment;
    }

    /// <summary>
    /// Reads a fragment, with or without the leading <c>#</c>. Parameters may come in any order and unknown ones are ignored.
    /// It fails when <c>lat</c> or <c>lon</c> is missing, not a plain decimal number (digits and a point, no sign, exponent or comma),
    /// or not a point in Poland; when <c>r</c> or <c>m</c> is present but not an allowed value (missing means the default);
    /// and when a known parameter appears twice, since there is no telling which value was meant.
    /// The label <c>q</c> is percent-decoded as UTF-8; a malformed sequence makes it <c>null</c> without failing the rest.
    /// Control characters are removed, the text is trimmed and cut to 100 UTF-16 chars (never inside a surrogate pair).
    /// </summary>
    public static bool TryParse(string? fragment, [NotNullWhen(true)] out PlaceUrlState? state)
    {
        state = null;
        if (string.IsNullOrEmpty(fragment))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in (fragment[0] == '#' ? fragment[1..] : fragment).Split('&'))
        {
            var equals = part.IndexOf('=');
            var key = equals > 0 ? part[..equals] : string.Empty;
            if (key is "lat" or "lon" or "r" or "m" or "q" && !values.TryAdd(key, part[(equals + 1)..]))
            {
                return false;
            }
        }

        if (!values.TryGetValue("lat", out var lat) || !values.TryGetValue("lon", out var lon)
            || !double.TryParse(lat, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(lon, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var longitude)
            || !Wgs84Point.IsInPoland(latitude, longitude)
            || !TryAllowed(values, "r", DefaultRadius, Radii, out var radius)
            || !TryAllowed(values, "m", DefaultMonths, Months, out var months))
        {
            return false;
        }

        var label = values.TryGetValue("q", out var q) ? CleanLabel(Unescape(q)) : null;
        state = new PlaceUrlState(new Wgs84Point(latitude, longitude), radius, months, label);
        return true;
    }

    // A missing parameter gives the fallback; a present one must be plain digits and one of the allowed values.
    private static bool TryAllowed(Dictionary<string, string> values, string key, int fallback, IReadOnlyList<int> allowed, out int result)
    {
        result = fallback;
        return !values.TryGetValue(key, out var text)
            || (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out result) && allowed.Contains(result));
    }

    // Percent-decoding as strict UTF-8. Uri.UnescapeDataString would pass a bad sequence ("%zz", "%4", "%FF") through as text; here it is an error.
    // A plus sign stays a plus sign (the escaper writes a space as %20).
    private static string? Unescape(string text)
    {
        var bytes = new List<byte>(text.Length);
        for (var i = 0; i < text.Length;)
        {
            if (text[i] != '%')
            {
                var end = text.IndexOf('%', i);
                end = end < 0 ? text.Length : end;
                bytes.AddRange(Encoding.UTF8.GetBytes(text.Substring(i, end - i)));
                i = end;
            }
            else if (i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]))
            {
                bytes.Add((byte)((Uri.FromHex(text[i + 1]) << 4) | Uri.FromHex(text[i + 2])));
                i += 3;
            }
            else
            {
                return null;
            }
        }

        var decoded = bytes.ToArray();
        return Utf8.IsValid(decoded) ? Encoding.UTF8.GetString(decoded) : null;
    }

    // Control characters and invisible formatting (bidi overrides, line and paragraph separators) are dropped: the label is attacker-controlled text from a link.
    // A label with the shape of a KW number is dropped whole.
    private static string? CleanLabel(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsControl(c) && char.GetUnicodeCategory(c) is not (UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator))
            {
                builder.Append(c);
            }
        }

        var label = builder.ToString().Trim();
        if (label.Length > MaxLabelLength)
        {
            // A high surrogate at the last kept position means its pair would be split by the cut.
            label = label[..(char.IsHighSurrogate(label[MaxLabelLength - 1]) ? MaxLabelLength - 1 : MaxLabelLength)].TrimEnd();
        }

        // A land-registry (KW) number is never shown or written back into a link, whoever put it there.
        return label.Length == 0 || KwShape.Matches(label) ? null : label;
    }
}
