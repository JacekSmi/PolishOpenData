using System.Text.RegularExpressions;

namespace PolishOpenData.Demo.Search;

/// <summary>
/// Whether a text has the shape of a land-registry (KW) number, which the demo never accepts, shows or writes into a link.
/// The rule of <c>IdentifierTools.LooksLikeKw</c> in <c>src/PolishOpenData.Mcp/Tools/IdentifierTools.cs</c>: trimmed, upper-cased, spaces removed,
/// then a court code, 8 digits and a check digit ('/' or '-' optional between them); the check digit is not verified. A change there is checked here.
/// </summary>
internal static partial class KwShape
{
    public static bool Matches(string text) =>
        Pattern().IsMatch(text.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal));

    [GeneratedRegex("^([A-Z0-9]{4})[/-]?([0-9]{8})[/-]?([0-9])$")]
    private static partial Regex Pattern();
}
