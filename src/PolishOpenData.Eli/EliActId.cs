using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PolishOpenData.Eli;

/// <summary>
/// The identifier of a legal act in the Sejm ELI API: publisher, year and position, for example <c>DU/2024/1061</c>
/// (Dz.U. 2024 poz. 1061) or <c>MP/2023/1261</c> (M.P. 2023 poz. 1261).
/// </summary>
public readonly struct EliActId : IEquatable<EliActId>
{
    private static readonly Regex Slashed = new(@"^([A-Za-z]{2,3})/(\d{4})/(\d{1,7})$", RegexOptions.CultureInvariant);
    private static readonly Regex Address = new(@"^W([A-Za-z]{2,3})(\d{4})(\d{7})$", RegexOptions.CultureInvariant);
    private static readonly Regex Display = new(@"^(dz\.?\s?u\.?|m\.?\s?p\.?)\s+(\d{4})\s+poz\.?\s*(\d{1,7})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private EliActId(string publisher, int year, int position)
    {
        Publisher = publisher;
        Year = year;
        Position = position;
    }

    /// <summary>Publisher code: <c>DU</c> (Dziennik Ustaw), <c>MP</c> (Monitor Polski) and so on; empty for a default value.</summary>
    public string Publisher { get; } = string.Empty;

    /// <summary>Year of publication.</summary>
    public int Year { get; }

    /// <summary>Position in that year's volume.</summary>
    public int Position { get; }

    /// <summary>True for a default (unset) id.</summary>
    public bool IsEmpty => Position == 0;

    /// <summary>The id as the API writes it in paths: <c>DU/2024/1061</c>.</summary>
    internal string Path => Publisher + "/" + Year.ToString(CultureInfo.InvariantCulture) + "/" + Position.ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses <c>DU/2024/1061</c>, the API address <c>WDU20240001061</c> or "Dz.U. 2024 poz. 1061" (also "M.P.").</summary>
    /// <exception cref="FormatException">The text is none of these.</exception>
    public static EliActId Parse(string text) =>
        TryParse(text, out var id) ? id : throw new FormatException("Not a legal act id (use DU/2024/1061, WDU20240001061 or 'Dz.U. 2024 poz. 1061').");

    /// <summary>Like <see cref="Parse"/> but returns false instead of throwing.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out EliActId id)
    {
        id = default;
        if (text is null || text.Trim().Length == 0)
        {
            return false;
        }

        var trimmed = text.Trim();
        string publisher;
        string year;
        string position;
        Match m;
        if ((m = Slashed.Match(trimmed)).Success || (m = Address.Match(trimmed)).Success)
        {
            publisher = m.Groups[1].Value.ToUpperInvariant();
            year = m.Groups[2].Value;
            position = m.Groups[3].Value;
        }
        else if ((m = Display.Match(trimmed)).Success)
        {
            publisher = m.Groups[1].Value.StartsWith("d", StringComparison.OrdinalIgnoreCase) ? "DU" : "MP";
            year = m.Groups[2].Value;
            position = m.Groups[3].Value;
        }
        else
        {
            return false;
        }

        var y = int.Parse(year, CultureInfo.InvariantCulture);
        var p = int.Parse(position, CultureInfo.InvariantCulture);
        if (y is < 1900 or > 2200 || p < 1)
        {
            return false;
        }

        id = new EliActId(publisher, y, p);
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(EliActId other) => Publisher == other.Publisher && Year == other.Year && Position == other.Position;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EliActId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (((Publisher?.GetHashCode() ?? 0) * 397) ^ Year) * 397 ^ Position;

    /// <summary>The id written like <c>DU/2024/1061</c>; empty for a default value.</summary>
    public override string ToString() => IsEmpty ? string.Empty : Path;

    /// <summary>Value equality.</summary>
    public static bool operator ==(EliActId left, EliActId right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(EliActId left, EliActId right) => !left.Equals(right);
}

/// <summary>Helpers for article numbers as people write them.</summary>
public static class EliArticle
{
    private static readonly Regex Number = new(@"^(\d{1,4}[a-z]{0,3})(?:[\^_](\d{1,2})|([¹²³⁰-⁹]{1,2}))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Turns "415", "art. 415", "446^1", "446¹" or "446_1" into the form the structure uses ("415", "446_1"); a superscript that
    /// is a footnote-style number is written the same way. Returns null when the text is not an article number.
    /// </summary>
    public static string? Normalize(string? article)
    {
        if (article is null || article.Trim().Length == 0)
        {
            return null;
        }

        var text = article.Trim();
        if (text.StartsWith("art", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(3).TrimStart('.', ' ');
        }

        var m = Number.Match(text.Trim());
        if (!m.Success)
        {
            return null;
        }

        var main = m.Groups[1].Value.ToLowerInvariant();
        if (m.Groups[2].Success)
        {
            return main + "_" + m.Groups[2].Value;
        }

        return m.Groups[3].Success ? main + "_" + FromSuperscript(m.Groups[3].Value) : main;
    }

    private static string FromSuperscript(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            _ = sb.Append(c switch
            {
                '¹' => '1',
                '²' => '2',
                '³' => '3',
                _ => (char)('0' + (c - '⁰')),
            });
        }

        return sb.ToString();
    }
}
