using System;
using System.Net;
using System.Text;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

public class RegonTestResponsesTests
{
    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    // A recorded answer with its boundary (it changes with every answer) replaced by the helper's fixed one.
    private static string RecordedWithFixedBoundary(string name)
    {
        var headers = Fixture.Read("regon/" + name + ".headers");
        var start = headers.IndexOf("boundary=\"", StringComparison.Ordinal) + "boundary=\"".Length;
        var recordedBoundary = headers.Substring(start, headers.IndexOf('"', start) - start);
        return Text(Fixture.ReadBytes("regon/" + name + ".mtom")).Replace(recordedBoundary, RegonTestResponses.Boundary);
    }

    [Fact]
    public void Mtom_frames_like_the_recorded_service()
    {
        Assert.Equal(RecordedWithFixedBoundary("zaloguj-ok"), Text(RegonTestResponses.Mtom("Zaloguj", "0123456789abcdefghij")));
        Assert.Equal(RecordedWithFixedBoundary("wyloguj-true"), Text(RegonTestResponses.Mtom("Wyloguj", "true")));
        Assert.Equal(RecordedWithFixedBoundary("search-empty"), Text(RegonTestResponses.Mtom("DaneSzukajPodmioty", null)));
        Assert.Equal(RecordedWithFixedBoundary("getvalue-empty"), Text(RegonTestResponses.Mtom("GetValue", null)));

        // The recorded search answer: the service writes CRLF inside the result, which travels as &#xD; followed by a raw LF.
        var inner = string.Join(
            "\r\n",
            "<root>",
            "  <dane>",
            "    <Regon>000331501</Regon>",
            "    <Nip>5261040828</Nip>",
            "    <StatusNip />",
            "    <Nazwa>GŁÓWNY URZĄD STATYSTYCZNY</Nazwa>",
            "    <Wojewodztwo>MAZOWIECKIE</Wojewodztwo>",
            "    <Powiat>m. st. Warszawa</Powiat>",
            "    <Gmina>Śródmieście</Gmina>",
            "    <Miejscowosc>Warszawa</Miejscowosc>",
            "    <KodPocztowy>00-925</KodPocztowy>",
            "    <Ulica>ul. Test-Krucza</Ulica>",
            "    <NrNieruchomosci>208</NrNieruchomosci>",
            "    <NrLokalu />",
            "    <Typ>P</Typ>",
            "    <SilosID>6</SilosID>",
            "    <DataZakonczeniaDzialalnosci />",
            "    <MiejscowoscPoczty>Warszawa</MiejscowoscPoczty>",
            "  </dane>",
            "</root>");
        Assert.Equal(RecordedWithFixedBoundary("search-regon-gus"), Text(RegonTestResponses.Mtom("DaneSzukajPodmioty", inner)));
    }

    [Fact]
    public void MtomEnvelope_frames_a_ready_envelope()
    {
        var expected = "\r\n--" + RegonTestResponses.Boundary + "\r\n"
            + "Content-ID: <http://tempuri.org/0>\r\n"
            + "Content-Transfer-Encoding: 8bit\r\n"
            + "Content-Type: application/xop+xml;charset=utf-8;type=\"application/soap+xml\"\r\n"
            + "\r\n"
            + "<s:Envelope/>"
            + "\r\n--" + RegonTestResponses.Boundary + "--\r\n";
        Assert.Equal(expected, Text(RegonTestResponses.MtomEnvelope("<s:Envelope/>")));
    }

    [Fact]
    public void Mtom_escapes_markup_and_carriage_returns()
    {
        var text = Text(RegonTestResponses.Mtom("DaneSzukajPodmioty", "<a b=\"1\">x & y > z</a>\r\nnext"));
        Assert.Contains("<DaneSzukajPodmiotyResult>&lt;a b=\"1\"&gt;x &amp; y &gt; z&lt;/a&gt;&#xD;\nnext</DaneSzukajPodmiotyResult>", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_result_is_a_self_closed_element(string? result)
    {
        var text = Text(RegonTestResponses.Mtom("DaneSzukajPodmioty", result));
        Assert.Contains("<DaneSzukajPodmiotyResult/>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("</DaneSzukajPodmiotyResult>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetValue_uses_the_bir_namespace()
    {
        var getValue = Text(RegonTestResponses.Mtom("GetValue", "19-10-2018"));
        Assert.Contains("<GetValueResponse xmlns=\"http://CIS/BIR/2014/07\"><GetValueResult>19-10-2018</GetValueResult></GetValueResponse>", getValue, StringComparison.Ordinal);
        Assert.Contains(">http://CIS/BIR/2014/07/IUslugaBIR/GetValueResponse</a:Action>", getValue, StringComparison.Ordinal);

        var report = Text(RegonTestResponses.Mtom("DanePobierzPelnyRaport", "x"));
        Assert.Contains("<DanePobierzPelnyRaportResponse xmlns=\"http://CIS/BIR/PUBL/2014/07\">", report, StringComparison.Ordinal);
        Assert.Contains(">http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/DanePobierzPelnyRaportResponse</a:Action>", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_operation_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => RegonTestResponses.Mtom("DanePobierzRaportZbiorczy", null));
        Assert.Equal("operation", exception.ParamName);
    }

    [Fact]
    public void Response_carries_the_status_and_the_raw_content_type()
    {
        using var response = RegonTestResponses.Response(HttpStatusCode.OK, RegonTestResponses.Mtom("Zaloguj", "x"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Content.Headers.TryGetValues("Content-Type", out var values));
        Assert.Equal(RegonTestResponses.ContentType.Replace("; ", ";"), string.Join(", ", values!).Replace("; ", ";"));
        Assert.Contains("boundary=\"" + RegonTestResponses.Boundary + "\"", RegonTestResponses.ContentType, StringComparison.Ordinal);
    }

    [Fact]
    public void Inner_reads_a_synthetic_fixture()
    {
        var inner = RegonTestResponses.Inner("synthetic-search-notfound");
        Assert.StartsWith("<root>\n", inner, StringComparison.Ordinal);
        Assert.Contains("<ErrorCode>4</ErrorCode>", inner, StringComparison.Ordinal);
    }
}
