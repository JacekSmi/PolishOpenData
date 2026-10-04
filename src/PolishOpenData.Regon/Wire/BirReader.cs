using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace PolishOpenData.Bir.Wire;

/// <summary>A SOAP fault carried in the (HTTP 500) answer.</summary>
internal sealed class SoapFaultException : Exception
{
    public SoapFaultException(string code, string? subcode, string reason)
        : base("SOAP fault " + code + (subcode is null ? string.Empty : "/" + subcode) + ": " + reason)
    {
        Code = code;
        Subcode = subcode;
        Reason = reason;
    }

    /// <summary>Value of the top-level Code (s:Sender or s:Receiver, prefix as sent).</summary>
    public string Code { get; }

    public string? Subcode { get; }

    public string Reason { get; }

    public bool IsSender => Code.EndsWith("Sender", StringComparison.Ordinal);
}

/// <summary>One flat record of the inner result: the children of a &lt;dane&gt; element (name -> text).</summary>
internal sealed class BirRow
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public int Count => _values.Count;

    /// <summary>Text of the field, or null when the element is missing or empty (GUS leaves empty elements out or writes them self-closed).</summary>
    public string? this[string name] => _values.TryGetValue(name, out var v) && v.Length > 0 ? v : null;

    public IEnumerable<KeyValuePair<string, string>> Fields => _values;

    internal void Set(string name, string value) => _values[name] = value;
}

/// <summary>XmlReader based parsing of the outer SOAP envelope and of the unescaped inner result. No reflection, no XmlSerializer.</summary>
internal static class BirReader
{
    public const string XopNamespace = "http://www.w3.org/2004/08/xop/include";

    private static XmlReaderSettings Settings(bool lenientCharacters) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CheckCharacters = !lenientCharacters,
        MaxCharactersFromEntities = 0,
    };

    private static XmlReader Open(ArraySegment<byte> bytes, string? charset, bool lenient)
    {
        var stream = new MemoryStream(bytes.Array!, bytes.Offset, bytes.Count, writable: false);
        if (charset is null || string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase))
        {
            return XmlReader.Create(stream, Settings(lenient));   // UTF-8 or a BOM
        }

        return XmlReader.Create(new StreamReader(stream, Encoding.GetEncoding(charset), false), Settings(lenient));
    }

    /// <summary>
    /// Returns the text of the *Result element (null when the Body has none). Throws <see cref="SoapFaultException"/> for a fault
    /// and <see cref="NotSupportedException"/> for an xop:Include inside the result (xs:string results are never externalised by WCF's MTOM encoder).
    /// </summary>
    public static string? ReadResult(SoapPackage package, string? resultElement, string resultNamespace)
    {
        using var reader = Open(package.Root, package.Charset, lenient: false);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.NamespaceURI == BirEnvelope.Soap12 && reader.LocalName == "Fault")
            {
                throw ReadFault(reader);
            }

            // resultElement == null: test helper, the first element named *Result in the BIR namespaces
            var matches = resultElement is null
                ? reader.LocalName.EndsWith("Result", StringComparison.Ordinal) && (reader.NamespaceURI == BirEnvelope.Publ || reader.NamespaceURI == BirEnvelope.Bir)
                : reader.LocalName == resultElement && reader.NamespaceURI == resultNamespace;
            if (matches)
            {
                return ReadTextOnly(reader);
            }
        }

        return null;
    }

    private static string ReadTextOnly(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        var depth = reader.Depth;
        var sb = new StringBuilder();
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    sb.Append(reader.Value);   // entity references (&lt; &#xD;) are already resolved by the reader
                    break;
                case XmlNodeType.Element when reader.NamespaceURI == XopNamespace && reader.LocalName == "Include":
                    throw new NotSupportedException("The result contains an xop:Include; BIR is not expected to return binary content.");
                case XmlNodeType.Element:
                    throw new FormatException("Unexpected element <" + reader.LocalName + "> inside a string result.");
                case XmlNodeType.EndElement when reader.Depth == depth:
                    return sb.ToString();
            }
        }

        throw new FormatException("The result element is not closed.");
    }

    private static SoapFaultException ReadFault(XmlReader reader)
    {
        string? code = null, subcode = null, reason = null;
        var depth = reader.Depth;
        var inSubcode = false;
        reader.Read();
        while (!reader.EOF && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType == XmlNodeType.Element && reader.NamespaceURI == BirEnvelope.Soap12)
            {
                // ReadElementContentAsString moves past the end tag, so those branches must not call Read() again
                switch (reader.LocalName)
                {
                    case "Subcode":
                        inSubcode = true;
                        break;
                    case "Value":
                        var v = reader.ReadElementContentAsString();
                        if (inSubcode) { subcode ??= v; } else { code ??= v; }
                        continue;
                    case "Text":
                        reason ??= reader.ReadElementContentAsString();
                        continue;
                }
            }

            reader.Read();
        }

        return new SoapFaultException(code ?? "?", subcode, reason ?? string.Empty);
    }

    /// <summary>
    /// Parses the unescaped inner result <c>&lt;root&gt;&lt;dane&gt;...&lt;/dane&gt;&lt;/root&gt;</c> into one flat row per dane.
    /// CheckCharacters is off, so a stray control character in a name does not fail the whole answer.
    /// </summary>
    public static List<BirRow> ReadRows(string inner)
    {
        var rows = new List<BirRow>();
        using var reader = XmlReader.Create(new StringReader(inner), Settings(lenientCharacters: true));
        BirRow? row = null;
        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && reader.LocalName == "dane")
            {
                row = new BirRow();
                rows.Add(row);
                if (reader.IsEmptyElement)
                {
                    row = null;
                }
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.Depth == 2 && row is not null)
            {
                var name = reader.LocalName;
                row.Set(name, reader.ReadElementContentAsString());   // reads past the end tag, so no Read() below
                continue;
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == 1)
            {
                row = null;
            }

            reader.Read();
        }

        return rows;
    }
}
