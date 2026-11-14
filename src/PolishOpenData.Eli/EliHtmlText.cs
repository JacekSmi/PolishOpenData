using System;
using System.Net;
using System.Text.RegularExpressions;

namespace PolishOpenData.Eli;

/// <summary>Turns the HTML the ELI API serves for a unit of an act into plain text. The HTML is untrusted: nothing of it is kept but text.</summary>
internal static class EliHtmlText
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline;

    private static readonly Regex Comments = new(@"<!--.*?-->", Options, Timeout);
    private static readonly Regex ScriptsAndStyles = new(@"<(script|style)\b[^>]*>.*?</\1\s*>", Options, Timeout);
    private static readonly Regex Superscript = new(@"<sup\b[^>]*>(.*?)</sup\s*>", Options, Timeout);
    private static readonly Regex BlockEnds = new(@"<br\s*/?>|</(p|div|h[1-6]|li|tr|table|ul|ol|section|blockquote)\s*>", Options, Timeout);
    private static readonly Regex Tags = new(@"<[^>]*>", Options, Timeout);
    private static readonly Regex Spaces = new(@"[ \t\r\f\v]+", Options, Timeout);

    /// <summary>
    /// Plain text with one line per block (heading, paragraph, point), at most <paramref name="maxChars"/> characters;
    /// <paramref name="truncated"/> says whether it was cut. Superscripts are written <c>446^1</c>.
    /// </summary>
    public static string ToPlainText(string html, int maxChars, out bool truncated)
    {
        var s = Comments.Replace(html, string.Empty);
        s = ScriptsAndStyles.Replace(s, string.Empty);
        s = Superscript.Replace(s, "^$1");
        s = BlockEnds.Replace(s, "\n");
        s = Tags.Replace(s, string.Empty);
        s = WebUtility.HtmlDecode(s).Replace(' ', ' ');   // decoded last, so an encoded "<" is text and never a tag

        var lines = new System.Collections.Generic.List<string>();
        foreach (var raw in s.Split('\n'))
        {
            var line = Spaces.Replace(raw, " ").Trim();
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        var text = string.Join("\n", lines);
        truncated = text.Length > maxChars;
        return truncated ? text.Substring(0, maxChars) : text;
    }
}
