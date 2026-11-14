using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace PolishOpenData.Sejm;

/// <summary>Finds the Sejm print numbers a title or topic refers to ("druki nr 1344, 2100 i 2100-A").</summary>
public static class SejmPrintRefs
{
    private const string Token = "[0-9]{1,6}(?:-[0-9A-Za-z]{1,4})*";

    private static readonly Regex Phrase = new(
        @"druk(?:u|i|ów)?\s+nr\s+(?<list>" + Token + @"(?:\s*,\s*" + Token + @"|\s+(?:i|oraz)\s+" + Token + @")*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex Tokens = new(Token, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Returns the exact print tokens in order, without duplicates: <c>2100-A</c> is never reported as <c>2100</c>, and <c>13440</c> is never <c>1344</c>.
    /// Compare tokens for equality; do not search for a number inside a longer text.
    /// </summary>
    /// <exception cref="PolishOpenDataApiException">The text took too long to scan.</exception>
    public static IReadOnlyList<string> FromTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var found = new List<string>();
        try
        {
            foreach (Match phrase in Phrase.Matches(text))
            {
                foreach (Match token in Tokens.Matches(phrase.Groups["list"].Value))
                {
                    if (!found.Contains(token.Value))
                    {
                        found.Add(token.Value);
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new PolishOpenDataApiException("Sejm returned a title this library could not scan in time.", HttpStatusCode.OK, null, null, ex);
        }

        return found;
    }
}
