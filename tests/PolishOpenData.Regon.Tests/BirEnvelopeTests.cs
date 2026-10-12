using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using PolishOpenData.Bir.Wire;

namespace PolishOpenData.Bir.Tests;

public class BirEnvelopeTests
{
    private static readonly Uri TestEndpoint = new("https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc");

    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace Wsa = "http://www.w3.org/2005/08/addressing";
    private static readonly XNamespace Publ = "http://CIS/BIR/PUBL/2014/07";
    private static readonly XNamespace Bir = "http://CIS/BIR/2014/07";
    private static readonly XNamespace Data = "http://CIS/BIR/PUBL/2014/07/DataContract";

    // Canonical XML for the golden comparisons: the writer repeats namespace declarations on inner elements (the live service
    // accepted that), so declarations are dropped and only element names (namespace + local name), attributes and text are compared.
    private static XDocument Canonical(byte[] bytes)
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        foreach (var element in document.Descendants())
        {
            element.Attributes().Where(static a => a.IsNamespaceDeclaration).Remove();
        }

        return document;
    }

    private static void AssertGolden(string expectedXml, byte[] actual)
    {
        var expected = Canonical(Encoding.UTF8.GetBytes(expectedXml));
        Assert.True(XNode.DeepEquals(expected, Canonical(actual)), "Expected:\n" + expected + "\nActual:\n" + Canonical(actual));
    }

    private static XElement Body(byte[] envelope) => Canonical(envelope).Root!.Element(Soap + "Body")!;

    [Fact]
    public void Zaloguj_envelope_matches_the_golden_xml()
    {
        var bytes = BirEnvelope.Zaloguj(TestEndpoint, "dummy-key-not-a-secret", out var action);

        Assert.Equal("http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/Zaloguj", action);
        AssertGolden(
            """
            <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope" xmlns:ns="http://CIS/BIR/PUBL/2014/07" xmlns:wsa="http://www.w3.org/2005/08/addressing">
              <soap:Header>
                <wsa:To>https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc</wsa:To>
                <wsa:Action>http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/Zaloguj</wsa:Action>
              </soap:Header>
              <soap:Body>
                <ns:Zaloguj>
                  <ns:pKluczUzytkownika>dummy-key-not-a-secret</ns:pKluczUzytkownika>
                </ns:Zaloguj>
              </soap:Body>
            </soap:Envelope>
            """,
            bytes);
    }

    [Fact]
    public void Wyloguj_envelope_names_the_sid()
    {
        var bytes = BirEnvelope.Wyloguj(TestEndpoint, "0123456789abcdefghij", out var action);

        Assert.Equal("http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/Wyloguj", action);
        AssertGolden(
            """
            <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope">
              <soap:Header>
                <wsa:To xmlns:wsa="http://www.w3.org/2005/08/addressing">https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc</wsa:To>
                <wsa:Action xmlns:wsa="http://www.w3.org/2005/08/addressing">http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/Wyloguj</wsa:Action>
              </soap:Header>
              <soap:Body>
                <ns:Wyloguj xmlns:ns="http://CIS/BIR/PUBL/2014/07">
                  <ns:pIdentyfikatorSesji>0123456789abcdefghij</ns:pIdentyfikatorSesji>
                </ns:Wyloguj>
              </soap:Body>
            </soap:Envelope>
            """,
            bytes);
    }

    [Fact]
    public void GetValue_uses_the_bir_namespace_and_action()
    {
        var bytes = BirEnvelope.GetValue(TestEndpoint, "StanDanych", out var action);

        // GetValue lives in the namespace and interface without PUBL (GUS BIR1.2 manual)
        Assert.Equal("http://CIS/BIR/2014/07/IUslugaBIR/GetValue", action);
        AssertGolden(
            """
            <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope">
              <soap:Header>
                <wsa:To xmlns:wsa="http://www.w3.org/2005/08/addressing">https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc</wsa:To>
                <wsa:Action xmlns:wsa="http://www.w3.org/2005/08/addressing">http://CIS/BIR/2014/07/IUslugaBIR/GetValue</wsa:Action>
              </soap:Header>
              <soap:Body>
                <ns:GetValue xmlns:ns="http://CIS/BIR/2014/07">
                  <ns:pNazwaParametru>StanDanych</ns:pNazwaParametru>
                </ns:GetValue>
              </soap:Body>
            </soap:Envelope>
            """,
            bytes);
    }

    [Theory]
    [InlineData("Regon", "000331501")]
    [InlineData("Nip", "5261040828")]
    [InlineData("Krs", "0000028860")]
    [InlineData("Regony9zn", "000331501,123456785")]
    [InlineData("Regony14zn", "14239685800010,99999999000026")]
    [InlineData("Nipy", "5261040828,1234563218")]
    [InlineData("Krsy", "0000028860,0001268296")]
    public void Search_writes_exactly_one_datacontract_child(string element, string value)
    {
        var bytes = BirEnvelope.Search(TestEndpoint, element, value, out var action);

        Assert.Equal("http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/DaneSzukajPodmioty", action);
        var operation = Assert.Single(Body(bytes).Elements());
        Assert.Equal(Publ + "DaneSzukajPodmioty", operation.Name);
        var parameters = Assert.Single(operation.Elements());
        Assert.Equal(Publ + "pParametryWyszukiwania", parameters.Name);
        var child = Assert.Single(parameters.Elements());
        Assert.Equal(Data + element, child.Name);
        Assert.Equal(value, child.Value);
    }

    [Fact]
    public void FullReport_has_regon_and_report_name()
    {
        var bytes = BirEnvelope.FullReport(TestEndpoint, "000331501", "BIR12OsPrawna", out var action);

        Assert.Equal("http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/DanePobierzPelnyRaport", action);
        var operation = Assert.Single(Body(bytes).Elements());
        Assert.Equal(Publ + "DanePobierzPelnyRaport", operation.Name);
        Assert.Equal("000331501", operation.Element(Publ + "pRegon")!.Value);
        Assert.Equal("BIR12OsPrawna", operation.Element(Publ + "pNazwaRaportu")!.Value);
        Assert.Equal(2, operation.Elements().Count());
    }

    [Theory]
    [InlineData("https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc")]
    [InlineData("https://wyszukiwarkaregon.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc")]
    [InlineData("http://localhost:5080/fake/bir.svc")]
    public void Wsa_to_follows_the_endpoint(string address)
    {
        var endpoint = new Uri(address);

        var bytes = BirEnvelope.Wyloguj(endpoint, "0123456789abcdefghij", out _);

        var header = Canonical(bytes).Root!.Element(Soap + "Header")!;
        Assert.Equal(endpoint.AbsoluteUri, header.Element(Wsa + "To")!.Value);
    }

    [Fact]
    public void Values_are_escaped()
    {
        const string hostile = "a<b&\"c'>d";

        var bytes = BirEnvelope.Search(TestEndpoint, "Nip", hostile, out _);

        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("a&lt;b&amp;\"c'&gt;d", text, StringComparison.Ordinal);
        Assert.Equal(hostile, Body(bytes).Descendants(Data + "Nip").Single().Value);
    }

    [Fact]
    public void Illegal_xml_characters_are_rejected()
    {
        // an illegal character must fail here, in the caller's thread, not become invalid bytes on the wire
        Assert.ThrowsAny<ArgumentException>(() => BirEnvelope.Zaloguj(TestEndpoint, "abc\u0001def", out _));
        Assert.ThrowsAny<ArgumentException>(() => BirEnvelope.Search(TestEndpoint, "Nip", "1\u0000", out _));
    }

    [Fact]
    public void Envelope_is_utf8_without_bom()
    {
        var bytes = BirEnvelope.Search(TestEndpoint, "Nip", "zażółć", out _);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "a byte order mark was written");
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("<soap:Envelope", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<?xml", text, StringComparison.Ordinal);
        Assert.Contains("zażółć", text, StringComparison.Ordinal);
    }
}
