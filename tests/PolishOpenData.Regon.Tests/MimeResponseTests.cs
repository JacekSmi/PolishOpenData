using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using PolishOpenData.Bir.Wire;
using PolishOpenData.Internal;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

// Hand-made MIME cases (boundaries, the start parameter, transfer encodings, line breaks), without the XML reader.
public class MimeResponseTests
{
    private const string Envelope = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><XResponse xmlns=\"http://CIS/BIR/PUBL/2014/07\"><XResult>&lt;root&gt;&lt;dane&gt;&lt;A&gt;1&lt;/A&gt;&lt;/dane&gt;&lt;/root&gt;</XResult></XResponse></s:Body></s:Envelope>";

    private static byte[] Bytes(string text) => new UTF8Encoding(false).GetBytes(text);

    // net472 has no ArraySegment<byte>.ToArray(): decode through the segment's array.
    private static string Text(ArraySegment<byte> segment) => Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count);

    private static string RawContentType(HttpResponseMessage response) =>
        response.Content.Headers.TryGetValues("Content-Type", out var values) ? string.Join(", ", values) : string.Empty;

    [Fact]
    public void Root_is_selected_by_start_not_by_position()
    {
        // the first part is an attachment; the root is the SECOND part and is named by start=
        var body = Bytes(
            "preamble text\r\n--b1\r\nContent-ID: <att@x>\r\nContent-Type: application/octet-stream\r\n\r\nBINARY\r\n"
            + "--b1\r\nContent-ID: <root@x>\r\nContent-Type: application/xop+xml;charset=utf-8\r\n\r\n" + Envelope + "\r\n--b1--\r\ntrailing epilogue");

        var package = MimeResponse.Parse(body, "multipart/related; type=\"application/xop+xml\"; start=\"<root@x>\"; boundary=\"b1\"");

        Assert.Equal(2, package.PartCount);
        Assert.Equal(Envelope, Text(package.Root));
        Assert.Equal("utf-8", package.Charset);
        Assert.Equal("BINARY", Text(package.Related["att@x"]));
        Assert.False(package.Related.ContainsKey("root@x"));
    }

    [Fact]
    public void First_part_is_the_root_without_start()
    {
        // RFC 2387 section 3.2: no start parameter, so the first part is the root
        var package = MimeResponse.Parse(Bytes("--b2\r\nContent-ID: <r>\r\n\r\n" + Envelope + "\r\n--b2--\r\n"), "multipart/related; boundary=b2");

        Assert.Equal(1, package.PartCount);
        Assert.Equal(Envelope, Text(package.Root));
        Assert.Empty(package.Related);
    }

    [Fact]
    public void Accepts_lf_only_line_breaks_padding_and_a_quoted_boundary()
    {
        // a boundary with ':' '+' '=' needs quotes; the delimiter line has transport padding; every line ends with a bare LF
        var body = Bytes("--uuid:x+id=1 \t\nContent-Type: application/xop+xml\n\n" + Envelope + "\n--uuid:x+id=1--");

        var package = MimeResponse.Parse(body, "multipart/related; type=\"application/xop+xml\";boundary=\"uuid:x+id=1\"");

        Assert.Equal(Envelope, Text(package.Root));
    }

    [Fact]
    public void A_boundary_that_prefixes_another_line_does_not_match()
    {
        var body = Bytes("--b\r\nContent-ID: <r>\r\n\r\n" + Envelope + "\r\n--b-not-a-delimiter\r\n--b--\r\n");

        var package = MimeResponse.Parse(body, "multipart/related; boundary=b");

        Assert.Equal(1, package.PartCount);
        Assert.Equal(Envelope + "\r\n--b-not-a-delimiter", Text(package.Root));
    }

    [Fact]
    public void Plain_soap_body_is_accepted()
    {
        var package = MimeResponse.Parse(Bytes(Envelope), "application/soap+xml; charset=utf-8");

        Assert.Equal(1, package.PartCount);
        Assert.Equal(Envelope, Text(package.Root));
        Assert.Equal("utf-8", package.Charset);
        Assert.Empty(package.Related);

        var withoutType = MimeResponse.Parse(Bytes(Envelope), null);
        Assert.Equal(Envelope, Text(withoutType.Root));
        Assert.Null(withoutType.Charset);
    }

    [Fact]
    public void Base64_root_is_refused()
    {
        var body = Bytes("--b\r\nContent-Transfer-Encoding: base64\r\n\r\nQUJD\r\n--b--");

        Assert.Throws<NotSupportedException>(() => MimeResponse.Parse(body, "multipart/related; boundary=b"));
    }

    [Fact]
    public void Quoted_printable_root_is_refused()
    {
        var body = Bytes("--b\r\nContent-Transfer-Encoding: quoted-printable\r\n\r\nABC=3D\r\n--b--");

        Assert.Throws<NotSupportedException>(() => MimeResponse.Parse(body, "multipart/related; boundary=b"));
    }

    [Theory]
    [InlineData("8bit")]
    [InlineData("7bit")]
    [InlineData("binary")]
    [InlineData("8BIT")]
    public void Identity_transfer_encodings_are_accepted(string encoding)
    {
        var body = Bytes("--b\r\nContent-Transfer-Encoding: " + encoding + "\r\n\r\n" + Envelope + "\r\n--b--");

        Assert.Equal(Envelope, Text(MimeResponse.Parse(body, "multipart/related; boundary=b").Root));
    }

    [Fact]
    public void Missing_closing_delimiter_throws()
    {
        Assert.Throws<FormatException>(() => MimeResponse.Parse(Bytes("--b\r\n\r\n" + Envelope), "multipart/related; boundary=b"));
    }

    [Fact]
    public void Missing_boundary_throws()
    {
        Assert.Throws<FormatException>(() => MimeResponse.Parse(Bytes(Envelope), "multipart/related"));
        Assert.Throws<FormatException>(() => MimeResponse.Parse(Bytes(Envelope), "multipart/related; boundary=\"\""));
    }

    [Fact]
    public void Unterminated_quoted_parameter_is_a_format_error()
    {
        // in the outer Content-Type
        var outer = Assert.Throws<FormatException>(() => MimeResponse.Parse(Bytes("--abc\r\n\r\n" + Envelope + "\r\n--abc--\r\n"), "multipart/related; boundary=\"abc"));
        Assert.Contains("quoted", outer.Message, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => MimeResponse.ParseContentType("a/b; c=\""));

        // in the Content-Type of a part
        var body = Bytes("--b\r\nContent-ID: <r>\r\nContent-Type: application/xop+xml; charset=\"utf-8\r\n\r\n" + Envelope + "\r\n--b--\r\n");
        Assert.Throws<FormatException>(() => MimeResponse.Parse(body, "multipart/related; boundary=b"));
    }

    [Fact]
    public void No_parts_throws()
    {
        // only the closing delimiter
        Assert.Throws<FormatException>(() => MimeResponse.Parse(Bytes("--b--\r\n"), "multipart/related; boundary=b"));
        // the boundary does not occur at all
        Assert.Throws<FormatException>(() => MimeResponse.Parse(Bytes("no delimiter here"), "multipart/related; boundary=b"));
    }

    [Fact]
    public void Related_parts_are_keyed_by_content_id()
    {
        var body = Bytes(
            "--b\r\nContent-ID: <root@x>\r\n\r\n" + Envelope + "\r\n"
            + "--b\r\nContent-ID: <one@x>\r\n\r\nFIRST\r\n"
            + "--b\r\nContent-ID: <two@x>\r\n\r\nSECOND\r\n"
            + "--b\r\nContent-Type: text/plain\r\n\r\nNO ID\r\n--b--\r\n");

        var package = MimeResponse.Parse(body, "multipart/related; boundary=b; start=\"<root@x>\"");

        Assert.Equal(4, package.PartCount);
        Assert.Equal(new[] { "one@x", "two@x" }, package.Related.Keys.OrderBy(static k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal("FIRST", Text(package.Related["one@x"]));
        Assert.Equal("SECOND", Text(package.Related["two@x"]));
    }

    [Fact]
    public void Charset_of_the_root_part_is_reported()
    {
        var head = Bytes("--b\r\nContent-Type: application/xop+xml;charset=utf-16\r\n\r\n");
        var utf16 = Encoding.Unicode.GetBytes(Envelope);
        var tail = Bytes("\r\n--b--\r\n");

        var package = MimeResponse.Parse(head.Concat(utf16).Concat(tail).ToArray(), "multipart/related; boundary=b");

        Assert.Equal("utf-16", package.Charset);
        Assert.Equal(utf16, package.Root.Array!.Skip(package.Root.Offset).Take(package.Root.Count).ToArray());
    }

    [Fact]
    public void Content_type_parser_handles_quoted_parameters()
    {
        var (mediaType, parameters) = MimeResponse.ParseContentType(
            "multipart/related; type=\"application/xop+xml\"; start=\"<http://tempuri.org/0>\"; boundary=\"uuid:fe842bcf-8f38-44fb-9430-4dda5ed38c80+id=188163\"; start-info=\"application/soap+xml\"");
        Assert.Equal("multipart/related", mediaType);
        Assert.Equal("application/xop+xml", parameters["type"]);
        Assert.Equal("<http://tempuri.org/0>", parameters["start"]);
        Assert.Equal("uuid:fe842bcf-8f38-44fb-9430-4dda5ed38c80+id=188163", parameters["boundary"]);
        Assert.Equal("application/soap+xml", parameters["start-info"]);

        // the curl-style header has no space after ';'; names are case-insensitive
        var (_, tight) = MimeResponse.ParseContentType("multipart/related; type=\"application/xop+xml\";start=\"<a>\";BOUNDARY=\"b:c\"");
        Assert.Equal("<a>", tight["start"]);
        Assert.Equal("b:c", tight["boundary"]);

        // a ';' inside quotes, an escaped quote, a parameter without a value, an unquoted value
        var (_, odd) = MimeResponse.ParseContentType("a/b; c=\"d;e\"; f=\"g\\\"h\"; flag; x=1");
        Assert.Equal("d;e", odd["c"]);
        Assert.Equal("g\"h", odd["f"]);
        Assert.False(odd.ContainsKey("flag"));
        Assert.Equal("1", odd["x"]);

        Assert.Equal(string.Empty, MimeResponse.ParseContentType(null).MediaType);
        Assert.Equal(string.Empty, MimeResponse.ParseContentType("   ").MediaType);
        Assert.Empty(MimeResponse.ParseContentType(string.Empty).Parameters);
    }

    [Theory]
    [InlineData("zaloguj-ok")]
    [InlineData("search-regon-gus")]
    [InlineData("wyloguj-true")]
    [InlineData("search-empty")]
    [InlineData("getvalue-empty")]
    [InlineData("fault-sender-500")]
    [InlineData("report-osprawna-gus")]
    [InlineData("getvalue-standanych")]
    public void Parses_every_recorded_fixture(string name)
    {
        // every recorded answer, with the Content-Type read the way BirTransport reads it: the raw header of a real HttpClient response
        // (the fixtures hold both spellings: with spaces after ';' as .NET re-serialises it, and the curl-style one without)
        using var response = StubHttpMessageHandler.Mtom(name);
        var contentType = RawContentType(response);
        var bytes = Fixture.ReadBytes("regon/" + name + ".mtom");

        var package = MimeResponse.Parse(bytes, contentType);

        Assert.Equal(1, package.PartCount);
        Assert.Equal("utf-8", package.Charset);
        Assert.Empty(package.Related);   // the one part is the root, and the root is not "related"
        var root = Text(package.Root);
        Assert.StartsWith("<s:Envelope ", root, StringComparison.Ordinal);
        Assert.EndsWith("</s:Envelope>", root, StringComparison.Ordinal);
        Assert.DoesNotContain("--uuid:", root, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("report-osprawna-gus", "DanePobierzPelnyRaportResponse")]
    [InlineData("getvalue-standanych", "GetValueResponse")]
    public void Parses_the_recorded_content_type_without_spaces_after_the_semicolons(string name, string responseElement)
    {
        // the live service sends multipart/related; type="...";start="...";boundary="...";start-info="..." (no space after
        // the later semicolons); the line is fed exactly as the .headers file recorded it, without HttpClient in between
        var contentType = Fixture.Read("regon/" + name + ".headers").Split('\n')[1].Substring("Content-Type: ".Length);
        Assert.Contains("\";start=\"<http://tempuri.org/0>\";boundary=\"uuid:", contentType, StringComparison.Ordinal);
        Assert.DoesNotContain("\"; ", contentType, StringComparison.Ordinal);

        var package = MimeResponse.Parse(Fixture.ReadBytes("regon/" + name + ".mtom"), contentType);

        Assert.Equal(1, package.PartCount);
        Assert.Equal("utf-8", package.Charset);
        var root = Text(package.Root);
        Assert.StartsWith("<s:Envelope ", root, StringComparison.Ordinal);
        Assert.Contains("<" + responseElement + " xmlns=", root, StringComparison.Ordinal);
        Assert.EndsWith("</s:Envelope>", root, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parses_the_recorded_search_answer_to_the_gus_row()
    {
        using var response = StubHttpMessageHandler.Mtom("search-regon-gus");
        var contentType = RawContentType(response);
        var bytes = await response.Content.ReadBytesAsync(TestContext.Current.CancellationToken);

        var root = Text(MimeResponse.Parse(bytes, contentType).Root);

        Assert.Contains("<DaneSzukajPodmiotyResponse xmlns=\"http://CIS/BIR/PUBL/2014/07\">", root, StringComparison.Ordinal);
        Assert.Contains("&lt;Regon&gt;000331501&lt;/Regon&gt;", root, StringComparison.Ordinal);
    }
}
