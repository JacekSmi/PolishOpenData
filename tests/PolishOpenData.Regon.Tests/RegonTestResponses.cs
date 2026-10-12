using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

/// <summary>
/// Builds BIR answers framed exactly like the live service (MTOM, one part, CRLF framing, entity-escaped result text),
/// for tests that need an answer no recorded fixture holds (the framing observed from the GUS service). The framing is compared with the captured
/// bytes in <c>RegonTestResponsesTests</c>.
/// </summary>
internal static class RegonTestResponses
{
    public const string Boundary = "uuid:00000000-0000-4000-8000-000000000001+id=1";

    public const string ContentType = "multipart/related; type=\"application/xop+xml\"; start=\"<http://tempuri.org/0>\"; boundary=\"" + Boundary + "\"; start-info=\"application/soap+xml\"";

    private const string PublNamespace = "http://CIS/BIR/PUBL/2014/07";
    private const string BirNamespace = "http://CIS/BIR/2014/07";

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>
    /// The MTOM answer of <paramref name="operation"/> (Zaloguj, Wyloguj, DaneSzukajPodmioty, DanePobierzPelnyRaport or GetValue).
    /// <paramref name="result"/> null or empty gives a self-closed result element, as the service answers a dead session.
    /// </summary>
    public static byte[] Mtom(string operation, string? result)
    {
        switch (operation)
        {
            case "Zaloguj":
            case "Wyloguj":
            case "DaneSzukajPodmioty":
            case "DanePobierzPelnyRaport":
            case "GetValue":
                break;
            default:
                throw new ArgumentException("Unknown BIR operation '" + operation + "'.", nameof(operation));
        }

        var isGetValue = string.Equals(operation, "GetValue", StringComparison.Ordinal);
        var ns = isGetValue ? BirNamespace : PublNamespace;
        var action = ns + (isGetValue ? "/IUslugaBIR/" : "/IUslugaBIRzewnPubl/") + operation + "Response";
        var resultElement = result is null || result.Length == 0
            ? "<" + operation + "Result/>"
            : "<" + operation + "Result>" + Escape(result) + "</" + operation + "Result>";
        return MtomEnvelope(
            "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:a=\"http://www.w3.org/2005/08/addressing\">"
            + "<s:Header><a:Action s:mustUnderstand=\"1\">" + action + "</a:Action></s:Header>"
            + "<s:Body><" + operation + "Response xmlns=\"" + ns + "\">" + resultElement + "</" + operation + "Response></s:Body>"
            + "</s:Envelope>");
    }

    /// <summary>Frames a ready SOAP envelope (a fault, a plain reply) like the service does.</summary>
    public static byte[] MtomEnvelope(string soapEnvelopeXml) =>
        Utf8.GetBytes(
            "\r\n--" + Boundary + "\r\n"
            + "Content-ID: <http://tempuri.org/0>\r\n"
            + "Content-Transfer-Encoding: 8bit\r\n"
            + "Content-Type: application/xop+xml;charset=utf-8;type=\"application/soap+xml\"\r\n"
            + "\r\n"
            + soapEnvelopeXml
            + "\r\n--" + Boundary + "--\r\n");

    public static HttpResponseMessage Response(HttpStatusCode status, byte[] mtomBody) =>
        StubHttpMessageHandler.MtomBytes(status, mtomBody, ContentType);

    /// <summary>The text of <c>tests/Fixtures/regon/&lt;fixtureName&gt;.xml</c>, the inner result a synthetic fixture holds.</summary>
    public static string Inner(string fixtureName) => Fixture.Read("regon/" + fixtureName + ".xml");

    // The service escapes & < > and writes a carriage return as &#xD; (never CDATA); a line feed and a tab stay raw. Any other
    // control character is written as a character reference (&#x1F;), the way WCF's text writer escapes it.
    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '\n':
                case '\t':
                    sb.Append(c);
                    break;
                case < ' ':
                    sb.Append("&#x").Append(((int)c).ToString("X", CultureInfo.InvariantCulture)).Append(';');
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}
