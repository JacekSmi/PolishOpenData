using System;
using System.IO;
using System.Text;
using System.Xml;

namespace PolishOpenData.Bir.Wire;

/// <summary>SOAP 1.2 + WS-Addressing 1.0 request envelopes for the BIR service, written with XmlWriter (AOT-safe, escapes all values).</summary>
internal static class BirEnvelope
{
    public const string Soap12 = "http://www.w3.org/2003/05/soap-envelope";
    public const string Wsa = "http://www.w3.org/2005/08/addressing";
    public const string Publ = "http://CIS/BIR/PUBL/2014/07";
    public const string Bir = "http://CIS/BIR/2014/07";
    public const string DataContract = "http://CIS/BIR/PUBL/2014/07/DataContract";

    private const string PublAction = "http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/";
    private const string BirAction = "http://CIS/BIR/2014/07/IUslugaBIR/";

    private static readonly UTF8Encoding Utf8NoBom = new(false, true);

    public static byte[] Zaloguj(Uri endpoint, string key, out string action) =>
        Write(endpoint, action = PublAction + "Zaloguj", Publ, "Zaloguj", w => w.WriteElementString("ns", "pKluczUzytkownika", Publ, key));

    public static byte[] Wyloguj(Uri endpoint, string sid, out string action) =>
        Write(endpoint, action = PublAction + "Wyloguj", Publ, "Wyloguj", w => w.WriteElementString("ns", "pIdentyfikatorSesji", Publ, sid));

    /// <summary>GetValue lives in another namespace and interface (no PUBL).</summary>
    public static byte[] GetValue(Uri endpoint, string parameterName, out string action) =>
        Write(endpoint, action = BirAction + "GetValue", Bir, "GetValue", w => w.WriteElementString("ns", "pNazwaParametru", Bir, parameterName));

    /// <summary>Exactly one search element; its name is Regon, Nip, Krs, Regony9zn, Regony14zn, Nipy or Krsy.</summary>
    public static byte[] Search(Uri endpoint, string element, string value, out string action) =>
        Write(endpoint, action = PublAction + "DaneSzukajPodmioty", Publ, "DaneSzukajPodmioty", w =>
        {
            w.WriteStartElement("ns", "pParametryWyszukiwania", Publ);
            w.WriteElementString("dat", element, DataContract, value);
            w.WriteEndElement();
        });

    public static byte[] FullReport(Uri endpoint, string regon, string reportName, out string action) =>
        Write(endpoint, action = PublAction + "DanePobierzPelnyRaport", Publ, "DanePobierzPelnyRaport", w =>
        {
            w.WriteElementString("ns", "pRegon", Publ, regon);
            w.WriteElementString("ns", "pNazwaRaportu", Publ, reportName);
        });

    private static byte[] Write(Uri endpoint, string action, string opNamespace, string operation, Action<XmlWriter> body)
    {
        using var buffer = new MemoryStream();
        // CheckCharacters (default true) turns an illegal XML character in an argument into an ArgumentException, not bad wire data.
        var settings = new XmlWriterSettings { Encoding = Utf8NoBom, OmitXmlDeclaration = true, Indent = false, CheckCharacters = true };
        using (var w = XmlWriter.Create(buffer, settings))
        {
            w.WriteStartElement("soap", "Envelope", Soap12);
            w.WriteStartElement("soap", "Header", Soap12);
            w.WriteElementString("wsa", "To", Wsa, endpoint.AbsoluteUri);
            w.WriteElementString("wsa", "Action", Wsa, action);
            w.WriteEndElement();
            w.WriteStartElement("soap", "Body", Soap12);
            w.WriteStartElement("ns", operation, opNamespace);
            body(w);
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        return buffer.ToArray();
    }
}
