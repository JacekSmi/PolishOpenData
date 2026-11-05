using System.Text.RegularExpressions;

namespace PolishOpenData.Demo.Search;

/// <summary>
/// Whether a text has the shape of a land-registry (KW) number, which the demo never accepts, shows or writes into a link.
/// The rule of <c>IdentifierTools.LooksLikeKw</c> in <c>src/PolishOpenData.Mcp/Tools/IdentifierTools.cs</c>: trimmed, upper-cased, spaces removed,
/// then a court code, 8 digits and a check digit ('/' or '-' optional between them); the check digit is not verified. A change there is checked here.
/// <see cref="Matches"/> is that rule for typed input (the whole text is a KW); <see cref="Contains"/> is the same shape anywhere in a text, for the label of a link.
/// </summary>
internal static partial class KwShape
{
    public static bool Matches(string text) => Whole().IsMatch(Normalize(text));

    // For a link label, where dropping a harmless label is the safe error: any 13 letters or digits of which the last 9 are digits match, so a long number
    // in an address ("ul. 1234567890123") loses its label too. Accepted; a typed text keeps the anchored rule above, where a false match would refuse a search.
    public static bool Contains(string text) => Anywhere().IsMatch(Normalize(text));

    private static string Normalize(string text) => text.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);

    [GeneratedRegex("^([A-Z0-9]{4})[/-]?([0-9]{8})[/-]?([0-9])$")]
    private static partial Regex Whole();

    [GeneratedRegex("([A-Z0-9]{4})[/-]?([0-9]{8})[/-]?([0-9])")]
    private static partial Regex Anywhere();
}
