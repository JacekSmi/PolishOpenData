using System;
using System.Text.RegularExpressions;

namespace PolishOpenData.Sejm;

/// <summary>
/// Whitelists for identifiers a caller supplies, checked before any request. Identifiers that come from API data are never checked here
/// (the API itself produces numbers with spaces or line breaks): they are trimmed and escaped by the client instead.
/// </summary>
internal static class SejmIds
{
    private static readonly Regex PrintPattern = new("^[0-9]{1,6}(-[0-9A-Za-z]{1,4})*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ClubPattern = new(@"^[\p{L}0-9._-]{1,40}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex CommitteePattern = new("^[A-Z0-9]{2,10}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>A print number such as <c>1344</c>, <c>128-BA</c> or <c>19-001</c>; trimmed.</summary>
    public static string Print(string? value) => Check(value, PrintPattern, "print number", "digits with optional -suffixes such as 128-BA");

    /// <summary>A club id such as <c>KO</c> or <c>niez.</c>.</summary>
    public static string Club(string? value) => Check(value, ClubPattern, "club id", "letters, digits, '.', '_' or '-'");

    /// <summary>A committee code such as <c>ASW</c>.</summary>
    public static string Committee(string? value) => Check(value, CommitteePattern, "committee code", "2 to 10 capital letters or digits");

    private static string Check(string? value, Regex pattern, string what, string shape)
    {
        var trimmed = value?.Trim();
        // a pattern that cannot finish in time is a bad value too (the patterns are linear, so this is a guard, not a path)
        if (trimmed is null || trimmed.Length == 0 || !Matches(pattern, trimmed))
        {
            throw new ArgumentException("Not a valid " + what + " (expected " + shape + ").", nameof(value));
        }

        return trimmed;
    }

    private static bool Matches(Regex pattern, string text)
    {
        try
        {
            return pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
