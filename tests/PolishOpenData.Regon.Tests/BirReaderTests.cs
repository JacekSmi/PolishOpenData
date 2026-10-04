using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Xml;
using PolishOpenData.Bir.Wire;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

public class BirReaderTests
{
    private static string RawContentType(HttpResponseMessage response) =>
        response.Content.Headers.TryGetValues("Content-Type", out var values) ? string.Join(", ", values) : string.Empty;

    // A recorded answer through the same path BirTransport uses: raw Content-Type header, then MimeResponse.
    private static SoapPackage Recorded(string name)
    {
        using var response = StubHttpMessageHandler.Mtom(name);
        return MimeResponse.Parse(Fixture.ReadBytes("regon/" + name + ".mtom"), RawContentType(response));
    }

    private static SoapPackage FromEnvelope(string soapEnvelopeXml) =>
        MimeResponse.Parse(RegonTestResponses.MtomEnvelope(soapEnvelopeXml), RegonTestResponses.ContentType);

    private static string Envelope(string resultXml) =>
        "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><XResponse xmlns=\"http://CIS/BIR/PUBL/2014/07\">" + resultXml + "</XResponse></s:Body></s:Envelope>";

    [Fact]
    public void Reads_the_zaloguj_result_of_the_recorded_fixture()
    {
        var result = BirReader.ReadResult(Recorded("zaloguj-ok"), BirOperations.ResultElement(BirOperation.Zaloguj), BirOperations.ResultNamespace(BirOperation.Zaloguj));

        Assert.Equal("0123456789abcdefghij", result);
        Assert.Equal("true", BirReader.ReadResult(Recorded("wyloguj-true"), "WylogujResult", BirEnvelope.Publ));
    }

    [Fact]
    public void Null_result_element_name_takes_the_first_result_in_a_bir_namespace()
    {
        Assert.Equal("0123456789abcdefghij", BirReader.ReadResult(Recorded("zaloguj-ok"), null, BirEnvelope.Publ));
        Assert.Equal(string.Empty, BirReader.ReadResult(Recorded("getvalue-empty"), null, BirEnvelope.Publ));
    }

    [Fact]
    public void Reads_the_search_row_of_the_recorded_fixture()
    {
        var result = BirReader.ReadResult(Recorded("search-regon-gus"), "DaneSzukajPodmiotyResult", BirEnvelope.Publ);

        Assert.NotNull(result);
        Assert.StartsWith("<root>", result, StringComparison.Ordinal);
        var row = Assert.Single(BirReader.ReadRows(result!));
        Assert.Equal(16, row.Count);   // the empty elements are fields too
        Assert.Equal("000331501", row["Regon"]);
        Assert.Equal("5261040828", row["Nip"]);
        Assert.Null(row["StatusNip"]);
        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", row["Nazwa"]);
        Assert.Equal("MAZOWIECKIE", row["Wojewodztwo"]);
        Assert.Equal("m. st. Warszawa", row["Powiat"]);
        Assert.Equal("Śródmieście", row["Gmina"]);
        Assert.Equal("Warszawa", row["Miejscowosc"]);
        Assert.Equal("00-925", row["KodPocztowy"]);
        Assert.Equal("ul. Test-Krucza", row["Ulica"]);
        Assert.Equal("208", row["NrNieruchomosci"]);
        Assert.Null(row["NrLokalu"]);
        Assert.Equal("P", row["Typ"]);
        Assert.Equal("6", row["SilosID"]);
        Assert.Null(row["DataZakonczeniaDzialalnosci"]);
        Assert.Equal("Warszawa", row["MiejscowoscPoczty"]);
    }

    [Fact]
    public void Empty_result_element_is_an_empty_string()
    {
        Assert.Equal(string.Empty, BirReader.ReadResult(Recorded("search-empty"), "DaneSzukajPodmiotyResult", BirEnvelope.Publ));
    }

    [Fact]
    public void Missing_result_element_returns_null()
    {
        // the login answer has no DaneSzukajPodmiotyResult
        Assert.Null(BirReader.ReadResult(Recorded("zaloguj-ok"), "DaneSzukajPodmiotyResult", BirEnvelope.Publ));
        Assert.Null(BirReader.ReadResult(FromEnvelope("<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body/></s:Envelope>"), "XResult", BirEnvelope.Publ));
    }

    [Fact]
    public void GetValue_result_is_matched_by_namespace()
    {
        // GetValue answers in the BIR namespace (no PUBL): the first harness looked in the wrong one and saw "no element"
        var package = Recorded("getvalue-empty");

        Assert.Equal(string.Empty, BirReader.ReadResult(package, BirOperations.ResultElement(BirOperation.GetValue), BirOperations.ResultNamespace(BirOperation.GetValue)));
        Assert.Null(BirReader.ReadResult(package, "GetValueResult", BirEnvelope.Publ));
    }

    [Fact]
    public void Fault_is_read_with_code_subcode_and_reason()
    {
        var package = Recorded("fault-sender-500");

        var fault = Assert.Throws<SoapFaultException>(() => BirReader.ReadResult(package, "DaneSzukajPodmiotyResult", BirEnvelope.Publ));

        Assert.Equal("s:Sender", fault.Code);
        Assert.Equal("a:DestinationUnreachable", fault.Subcode);
        Assert.StartsWith("The message with To 'urn:wrong-address' cannot be processed at the receiver", fault.Reason, StringComparison.Ordinal);
        Assert.True(fault.IsSender);
        Assert.Contains("a:DestinationUnreachable", fault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Receiver_fault_is_not_a_sender_fault()
    {
        var package = FromEnvelope(
            "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><s:Fault><s:Code><s:Value>s:Receiver</s:Value></s:Code>"
            + "<s:Reason><s:Text xml:lang=\"en-US\">Server busy</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>");

        var fault = Assert.Throws<SoapFaultException>(() => BirReader.ReadResult(package, "XResult", BirEnvelope.Publ));

        Assert.False(fault.IsSender);
        Assert.Null(fault.Subcode);
        Assert.Equal("Server busy", fault.Reason);
    }

    [Fact]
    public void Result_text_is_unescaped_including_carriage_returns()
    {
        var original = "<root>\r\n  <dane>\r\n    <Nazwa>A &amp; B &lt;C&gt;</Nazwa>\r\n  </dane>\r\n</root>";
        var package = MimeResponse.Parse(RegonTestResponses.Mtom("DaneSzukajPodmioty", original), RegonTestResponses.ContentType);

        var result = BirReader.ReadResult(package, "DaneSzukajPodmiotyResult", BirEnvelope.Publ);

        Assert.Equal(original, result);   // &lt; &amp; and &#xD; all come back as the characters they stand for
    }

    [Fact]
    public void Xop_include_inside_a_result_throws()
    {
        var package = FromEnvelope(Envelope("<XResult><xop:Include xmlns:xop=\"http://www.w3.org/2004/08/xop/include\" href=\"cid:att@x\"/></XResult>"));

        Assert.Throws<NotSupportedException>(() => BirReader.ReadResult(package, "XResult", BirEnvelope.Publ));
    }

    [Fact]
    public void Unexpected_child_element_throws()
    {
        var package = FromEnvelope(Envelope("<XResult>text<b>more</b></XResult>"));

        Assert.Throws<FormatException>(() => BirReader.ReadResult(package, "XResult", BirEnvelope.Publ));
    }

    [Fact]
    public void Rows_treat_missing_and_empty_elements_alike()
    {
        var rows = BirReader.ReadRows("<root>\r\n  <dane>\r\n    <ErrorCode>4</ErrorCode>\r\n    <Nip />\r\n    <NrLokalu></NrLokalu>\r\n    <X>a &amp; b</X>\r\n  </dane>\r\n  <dane/>\r\n</root>");

        Assert.Equal(2, rows.Count);
        Assert.Equal("4", rows[0]["ErrorCode"]);
        Assert.Null(rows[0]["Nip"]);
        Assert.Null(rows[0]["NrLokalu"]);
        Assert.Null(rows[0]["Ulica"]);   // never sent
        Assert.Equal("a & b", rows[0]["X"]);
        Assert.Equal(4, rows[0].Count);
        Assert.Equal(new[] { "ErrorCode", "Nip", "NrLokalu", "X" }, rows[0].Fields.Select(static f => f.Key).OrderBy(static k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(0, rows[1].Count);   // an empty <dane/> is a row without fields
        Assert.Empty(BirReader.ReadRows("<root />"));
    }

    [Fact]
    public void Rows_accept_cdata()
    {
        var rows = BirReader.ReadRows("<root><dane><Nazwa><![CDATA[A <b>&</b>]]></Nazwa></dane></root>");

        Assert.Equal("A <b>&</b>", Assert.Single(rows)["Nazwa"]);
    }

    [Fact]
    public void Rows_survive_a_control_character()
    {
        // GUS may copy a stray control character into a name; it must not fail the whole answer
        var rows = BirReader.ReadRows("<root><dane><Nazwa>A&#x1F;B</Nazwa></dane></root>");

        var name = Assert.Single(rows)["Nazwa"];
        Assert.Equal(3, name!.Length);
        Assert.Equal('\u001F', name[1]);
    }

    [Fact]
    public void Dtd_is_prohibited()
    {
        var withDtd = "<!DOCTYPE root [<!ENTITY e \"x\">]><root><dane><A>&e;</A></dane></root>";
        Assert.Throws<XmlException>(() => BirReader.ReadRows(withDtd));

        var package = MimeResponse.Parse(
            Encoding.UTF8.GetBytes("<!DOCTYPE s:Envelope [<!ENTITY e \"x\">]><s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body/></s:Envelope>"),
            "application/soap+xml; charset=utf-8");
        Assert.Throws<XmlException>(() => BirReader.ReadResult(package, "XResult", BirEnvelope.Publ));
    }

    [Fact]
    public void Utf16_root_is_read_with_its_charset()
    {
        var head = Encoding.ASCII.GetBytes("--b\r\nContent-Type: application/xop+xml;charset=utf-16\r\n\r\n");
        var root = Encoding.Unicode.GetBytes(Envelope("<XResult>zażółć</XResult>"));
        var tail = Encoding.ASCII.GetBytes("\r\n--b--\r\n");
        var package = MimeResponse.Parse(head.Concat(root).Concat(tail).ToArray(), "multipart/related; boundary=b");

        Assert.Equal("zażółć", BirReader.ReadResult(package, "XResult", BirEnvelope.Publ));
    }

    [Fact]
    public void Unknown_charset_is_an_argument_exception()
    {
        var package = MimeResponse.Parse(Encoding.ASCII.GetBytes("--b\r\nContent-Type: application/xop+xml;charset=x-no-such-charset\r\n\r\n<a/>\r\n--b--\r\n"), "multipart/related; boundary=b");

        Assert.Throws<ArgumentException>(() => BirReader.ReadResult(package, "XResult", BirEnvelope.Publ));
    }
}
