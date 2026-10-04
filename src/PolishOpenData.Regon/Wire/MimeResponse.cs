using System;
using System.Collections.Generic;
using System.Text;

namespace PolishOpenData.Bir.Wire;

/// <summary>The XML root part of a BIR response, plus the other MIME parts by Content-ID.</summary>
internal sealed class SoapPackage
{
    public SoapPackage(ArraySegment<byte> root, string? charset, int partCount, IReadOnlyDictionary<string, ArraySegment<byte>> related)
    {
        Root = root;
        Charset = charset;
        PartCount = partCount;
        Related = related;
    }

    public ArraySegment<byte> Root { get; }

    public string? Charset { get; }

    public int PartCount { get; }

    /// <summary>Other parts keyed by Content-ID without angle brackets (what an xop:Include cid: URI points to).</summary>
    public IReadOnlyDictionary<string, ArraySegment<byte>> Related { get; }
}

/// <summary>Minimal RFC 2046 / RFC 2387 reader for the MTOM (XOP) multipart/related answer; also accepts a plain SOAP body.</summary>
internal static class MimeResponse
{
    public static SoapPackage Parse(byte[] body, string? contentType)
    {
        var (mediaType, parameters) = ParseContentType(contentType);
        if (!string.Equals(mediaType, "multipart/related", StringComparison.OrdinalIgnoreCase))
        {
            // plain application/soap+xml (or text/xml): the whole body is the envelope
            parameters.TryGetValue("charset", out var plainCharset);
            return new SoapPackage(new ArraySegment<byte>(body), plainCharset, 1, new Dictionary<string, ArraySegment<byte>>());
        }

        if (!parameters.TryGetValue("boundary", out var boundary) || boundary.Length == 0)
        {
            throw new FormatException("multipart/related response without a boundary parameter");
        }

        parameters.TryGetValue("start", out var start);
        var parts = SplitParts(body, Encoding.ASCII.GetBytes("--" + boundary));
        if (parts.Count == 0)
        {
            throw new FormatException("multipart/related response without any part");
        }

        var related = new Dictionary<string, ArraySegment<byte>>(StringComparer.Ordinal);
        MimePart? root = null;
        foreach (var part in parts)
        {
            if (part.ContentId is null)
            {
                continue;
            }

            related[part.ContentId] = part.Content;
            if (root is null && start is not null && string.Equals(part.ContentId, Strip(start), StringComparison.Ordinal))
            {
                root = part;
            }
        }

        root ??= parts[0];   // RFC 2387 section 3.2: without a (matching) start parameter the root is the first part
        if (root.TransferEncoding is not null && !IsIdentity(root.TransferEncoding))
        {
            throw new NotSupportedException("The root MIME part uses Content-Transfer-Encoding '" + root.TransferEncoding + "'.");
        }

        if (root.ContentId is not null)
        {
            related.Remove(root.ContentId);
        }

        return new SoapPackage(root.Content, root.Charset, parts.Count, related);
    }

    private static bool IsIdentity(string cte) =>
        string.Equals(cte, "8bit", StringComparison.OrdinalIgnoreCase)
        || string.Equals(cte, "binary", StringComparison.OrdinalIgnoreCase)
        || string.Equals(cte, "7bit", StringComparison.OrdinalIgnoreCase);

    private static string Strip(string contentId) => contentId.Trim().TrimStart('<').TrimEnd('>');

    private sealed class MimePart
    {
        public string? ContentId;
        public string? TransferEncoding;
        public string? Charset;
        public ArraySegment<byte> Content;
    }

    /// <summary>Splits on delimiter lines: a delimiter starts a line (body start or after LF), then "--" (the last one) or blanks and a line end.</summary>
    private static List<MimePart> SplitParts(byte[] body, byte[] delimiter)
    {
        var parts = new List<MimePart>();
        var at = FindDelimiter(body, delimiter, 0, out var closing, out var afterLine);
        if (at < 0)
        {
            throw new FormatException("multipart/related response: the boundary delimiter was not found");
        }

        while (!closing)
        {
            var contentStart = afterLine;
            var next = FindDelimiter(body, delimiter, contentStart, out closing, out afterLine);
            if (next < 0)
            {
                throw new FormatException("multipart/related response without a closing boundary delimiter");
            }

            // the line break in front of the next delimiter belongs to the delimiter, not to the part
            var contentEnd = next;
            if (contentEnd > contentStart && body[contentEnd - 1] == '\n')
            {
                contentEnd--;
                if (contentEnd > contentStart && body[contentEnd - 1] == '\r')
                {
                    contentEnd--;
                }
            }

            parts.Add(ReadPart(body, contentStart, contentEnd));
        }

        return parts;
    }

    private static int FindDelimiter(byte[] body, byte[] delimiter, int from, out bool closing, out int afterLine)
    {
        closing = false;
        afterLine = -1;
        for (var i = from; i <= body.Length - delimiter.Length; i++)
        {
            if ((i != 0 && body[i - 1] != '\n') || !Matches(body, i, delimiter))
            {
                continue;
            }

            var j = i + delimiter.Length;
            if (j + 1 < body.Length && body[j] == '-' && body[j + 1] == '-')
            {
                closing = true;
                afterLine = body.Length;
                return i;
            }

            while (j < body.Length && (body[j] == ' ' || body[j] == '\t'))
            {
                j++;   // transport padding
            }

            if (j < body.Length && body[j] == '\r')
            {
                j++;
            }

            if (j < body.Length && body[j] == '\n')
            {
                afterLine = j + 1;
                return i;
            }
        }

        return -1;
    }

    private static bool Matches(byte[] body, int at, byte[] needle)
    {
        for (var k = 0; k < needle.Length; k++)
        {
            if (body[at + k] != needle[k])
            {
                return false;
            }
        }

        return true;
    }

    private static MimePart ReadPart(byte[] body, int start, int end)
    {
        // The headers end at the first empty line; a part without headers starts with that empty line.
        var lineStart = start;
        var headerEnd = start;
        var contentStart = -1;
        while (lineStart <= end)
        {
            var eol = lineStart < end ? Array.IndexOf(body, (byte)'\n', lineStart, end - lineStart) : -1;
            var lineEnd = eol < 0 ? end : eol;
            var length = lineEnd - lineStart;
            if (length > 0 && body[lineEnd - 1] == '\r')
            {
                length--;
            }

            if (length == 0)
            {
                headerEnd = lineStart;
                contentStart = eol < 0 ? end : eol + 1;
                break;
            }

            if (eol < 0)
            {
                break;
            }

            lineStart = eol + 1;
        }

        if (contentStart < 0)
        {
            throw new FormatException("A MIME part's headers are not terminated by an empty line.");
        }

        var part = new MimePart();
        var headers = Encoding.ASCII.GetString(body, start, headerEnd - start).Replace("\r\n", "\n").Replace("\n ", " ").Replace("\n\t", " ");
        foreach (var line in headers.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            if (string.Equals(name, "Content-ID", StringComparison.OrdinalIgnoreCase))
            {
                part.ContentId = Strip(value);
            }
            else if (string.Equals(name, "Content-Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                part.TransferEncoding = value;
            }
            else if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)
                && ParseContentType(value).Parameters.TryGetValue("charset", out var charset))
            {
                part.Charset = charset;
            }
        }

        part.Content = new ArraySegment<byte>(body, contentStart, end - contentStart);
        return part;
    }

    /// <summary>Splits "type/subtype; a=b; c=\"d;e\"" into the media type and its (case-insensitive) parameters; handles quoted values.</summary>
    public static (string MediaType, Dictionary<string, string> Parameters) ParseContentType(string? header)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (header is null || header.Trim().Length == 0)
        {
            return (string.Empty, parameters);
        }

        var i = 0;
        var type = ReadUntil(header, ref i, ';', '\0').Trim();
        while (i < header.Length)
        {
            i++;   // skip ';'
            var name = ReadUntil(header, ref i, '=', ';').Trim();
            if (i >= header.Length || header[i] == ';')
            {
                continue;   // a parameter without a value
            }

            i++;   // skip '='
            while (i < header.Length && header[i] == ' ')
            {
                i++;
            }

            string value;
            if (i < header.Length && header[i] == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < header.Length && header[i] != '"')
                {
                    if (header[i] == '\\' && i + 1 < header.Length)
                    {
                        i++;
                    }

                    sb.Append(header[i++]);
                }

                i++;   // closing quote
                value = sb.ToString();
                ReadUntil(header, ref i, ';', '\0');   // ignore anything up to the next parameter
            }
            else
            {
                value = ReadUntil(header, ref i, ';', '\0').Trim();
            }

            if (name.Length > 0)
            {
                parameters[name] = value;
            }
        }

        return (type, parameters);
    }

    private static string ReadUntil(string s, ref int i, char a, char b)
    {
        var from = i;
        while (i < s.Length && s[i] != a && (b == '\0' || s[i] != b))
        {
            i++;
        }

        return s.Substring(from, i - from);
    }
}
