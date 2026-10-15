using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Xml;
using PolishOpenData.Geo;
using PolishOpenData.Internal;

namespace PolishOpenData.Rcn;

/// <summary>Reads the GML 3.2 / WFS 2.0 answers of the RCN service. Streaming; reads no geometry beyond <c>gml:boundedBy</c>.</summary>
internal static class RcnGmlReader
{
    private const string Wfs = "http://www.opengis.net/wfs/2.0";
    private const string Gml = "http://www.opengis.net/gml/3.2";
    private const string Ms = "http://mapserver.gis.umn.edu/mapserver";
    private const string Ows = "http://www.opengis.net/ows/1.1";

    private static readonly char[] Space = { ' ' };

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    /// <summary>The rows of a <c>wfs:FeatureCollection</c>.</summary>
    /// <exception cref="PolishOpenDataApiException">The body is an <c>ows:ExceptionReport</c>, or is not a feature collection this reader can use.</exception>
    public static IReadOnlyList<RcnTransaction> Read(byte[] body, RcnLayer layer, HttpResponseMessage response)
    {
        try
        {
            using var stream = new MemoryStream(body, writable: false);
            using var xml = XmlReader.Create(stream, Settings);
            if (xml.MoveToContent() != XmlNodeType.Element)
            {
                throw new FormatException("the body is empty");
            }

            if (IsExceptionReport(xml))
            {
                throw ReadExceptionReport(xml, body, response);
            }

            if (xml.NamespaceURI != Wfs || xml.LocalName != "FeatureCollection")
            {
                throw new FormatException("the root element is " + xml.Name + ", not wfs:FeatureCollection");
            }

            var rows = new List<RcnTransaction>();
            ReadChildren(xml, member =>
            {
                if (member.NamespaceURI == Wfs && member.LocalName == "member")
                {
                    ReadChildren(member, feature => rows.Add(ReadFeature(feature, layer)));
                }
                else
                {
                    member.Skip();
                }
            });
            return rows;
        }
        catch (Exception exception) when (exception is XmlException or FormatException or ArgumentOutOfRangeException)
        {
            throw HttpHelpers.UnreadableResponse(
                "RCN returned a response that could not be read (HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + "): " + exception.Message,
                response,
                body,
                exception);
        }
    }

    /// <summary>The error an <c>ows:ExceptionReport</c> body describes (status of <paramref name="response"/>), or null when the body is anything else.</summary>
    public static PolishOpenDataApiException? TryReadException(byte[] body, HttpResponseMessage response)
    {
        try
        {
            using var stream = new MemoryStream(body, writable: false);
            using var xml = XmlReader.Create(stream, Settings);
            return xml.MoveToContent() == XmlNodeType.Element && IsExceptionReport(xml) ? ReadExceptionReport(xml, body, response) : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static bool IsExceptionReport(XmlReader xml) => xml.NamespaceURI == Ows && xml.LocalName == "ExceptionReport";

    private static PolishOpenDataApiException ReadExceptionReport(XmlReader xml, byte[] body, HttpResponseMessage response)
    {
        string? code = null;
        var texts = new List<string>();
        while (xml.Read())
        {
            if (xml.NodeType != XmlNodeType.Element || xml.NamespaceURI != Ows)
            {
                continue;
            }

            if (xml.LocalName == "Exception")
            {
                code ??= xml.GetAttribute("exceptionCode");
            }
            else if (xml.LocalName == "ExceptionText")
            {
                texts.Add(xml.ReadElementContentAsString().Trim());
            }
        }

        return new PolishOpenDataApiException(
            "RCN reported an error" + (code is null ? string.Empty : " (" + code + ")") + ": " + (texts.Count == 0 ? "no text" : string.Join(" ", texts)),
            response.StatusCode,
            code,
            HttpHelpers.Snippet(body));
    }

    // Calls handle for each child element of the element xml is on; handle must consume that child. Leaves xml after the element.
    private static void ReadChildren(XmlReader xml, Action<XmlReader> handle)
    {
        if (xml.IsEmptyElement)
        {
            xml.Read();
            return;
        }

        xml.ReadStartElement();
        while (xml.MoveToContent() == XmlNodeType.Element)
        {
            handle(xml);
        }

        xml.ReadEndElement();
    }

    // All the text inside the element (nested elements such as gml:timePosition included), trimmed. Consumes the element.
    private static string ReadText(XmlReader xml)
    {
        var text = new StringBuilder();
        using (var inner = xml.ReadSubtree())
        {
            while (inner.Read())
            {
                if (inner.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                {
                    text.Append(inner.Value);
                }
            }
        }

        xml.Read();
        return text.ToString().Trim();
    }

    private static RcnTransaction ReadFeature(XmlReader xml, RcnLayer layer)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        Pl1992Point? location = null;
        ReadChildren(xml, child =>
        {
            if (child.NamespaceURI == Gml && child.LocalName == "boundedBy")
            {
                location = ReadEnvelopeCentre(child);
            }
            else if (child.NamespaceURI == Ms && child.LocalName != "GEOMETRIA")
            {
                var name = child.LocalName;
                var value = ReadText(child);
                if (value.Length > 0)
                {
                    fields[name] = value;
                }
            }
            else
            {
                child.Skip();
            }
        });

        var addressField = layer switch
        {
            RcnLayer.Premises => "LOK_ADRES",
            RcnLayer.Parcels => "DZI_ADRES",
            _ => "BUD_ADRES",
        };
        return new RcnTransaction
        {
            Layer = layer,
            Id = Required(fields, "TRAN_LOKALNY_ID_IIP"),
            Date = Date(Required(fields, "DOK_DATA")),
            Market = Get(fields, "TRAN_RODZAJ_RYNKU") switch { "rynekPierwotny" => RcnMarket.Primary, "rynekWtorny" => RcnMarket.Secondary, _ => null },
            Kind = Get(fields, "TRAN_RODZAJ_TRANS"),
            SellerType = Get(fields, "TRAN_SPRZEDAJACY"),
            BuyerType = Get(fields, "TRAN_KUPUJACY"),
            TotalPrice = Decimal("TRAN_CENA_BRUTTO", Required(fields, "TRAN_CENA_BRUTTO")),
            DocumentNumber = Get(fields, "DOK_OZNACZENIE"),
            Right = Get(fields, "NIER_PRAWO"),
            Share = Get(fields, "NIER_UDZIAL"),
            Location = location ?? throw new FormatException("a row has no gml:boundedBy"),
            Address = Address(Get(fields, addressField)),
            PremisesFunction = Get(fields, "LOK_FUNKCJA") switch
            {
                "mieszkalna" => RcnPremisesFunction.Residential,
                "garaz" => RcnPremisesFunction.Garage,
                "handlowoUslugowa" => RcnPremisesFunction.Commercial,
                "inna" => RcnPremisesFunction.Other,
                _ => null,
            },
            Rooms = Integer(fields, "LOK_LICZBA_IZB"),
            Floor = Integer(fields, "LOK_NR_KOND"),
            UsableArea = Decimal(fields, "LOK_POW_UZYT"),
            PremisesPrice = Decimal(fields, "LOK_CENA_BRUTTO"),
            ParcelId = Geo.ParcelId.TryParse(Get(fields, "DZI_ID_DZIALKI"), out var parcel) ? parcel : null,
            LandArea = Decimal(fields, "DZI_POW_EWID"),
            ParcelPrice = Decimal(fields, "DZI_CENA_BRUTTO"),
            BuildingId = Get(fields, "BUD_ID_BUDYNKU"),
            BuildingKind = Get(fields, "BUD_RODZAJ"),
            BuildingUsableArea = Decimal(fields, "BUD_POW_UZYT"),
            BuildingPrice = Decimal(fields, "BUD_CENA_BRUTTO"),
            Fields = fields,
        };
    }

    // gml:Envelope corners are written northing easting (EPSG:2180 axis order); for a point both are the point.
    private static Pl1992Point ReadEnvelopeCentre(XmlReader boundedBy)
    {
        string? lower = null;
        string? upper = null;
        ReadChildren(boundedBy, envelope => ReadChildren(envelope, corner =>
        {
            if (corner.LocalName == "lowerCorner")
            {
                lower = ReadText(corner);
            }
            else if (corner.LocalName == "upperCorner")
            {
                upper = ReadText(corner);
            }
            else
            {
                corner.Skip();
            }
        }));

        var (n1, e1) = Corner(lower);
        var (n2, e2) = Corner(upper);
        return new Pl1992Point((e1 + e2) / 2, (n1 + n2) / 2);
    }

    private static (double Northing, double Easting) Corner(string? text)
    {
        var parts = text?.Split(Space, StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 2 }
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var northing)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var easting))
        {
            throw new FormatException("an envelope corner is not 'northing easting': '" + text + "'");
        }

        return (northing, easting);
    }

    private static string? Get(Dictionary<string, string> fields, string name) => fields.TryGetValue(name, out var value) ? value : null;

    private static string Required(Dictionary<string, string> fields, string name) =>
        Get(fields, name) ?? throw new FormatException("a row has no " + name);

    private static DateOnly Date(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new FormatException("DOK_DATA '" + text + "' is not yyyy-MM-dd");

    private static decimal Decimal(string name, string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException(name + " '" + text + "' is not a number");

    private static decimal? Decimal(Dictionary<string, string> fields, string name) =>
        Get(fields, name) is { } text ? Decimal(name, text) : null;

    private static int? Integer(Dictionary<string, string> fields, string name) =>
        Get(fields, name) is { } text
            ? int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException(name + " '" + text + "' is not an integer")
            : null;

    // MSC:<town>;UL:<street>;NR_PORZ:<number>, each part optional. Text with none of them is not an address.
    private static RcnAddress? Address(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string? town = null;
        string? street = null;
        string? number = null;
        foreach (var part in text.Split(';'))
        {
            var colon = part.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var value = part.Substring(colon + 1).Trim();
            if (value.Length == 0)
            {
                continue;
            }

            switch (part.Substring(0, colon).Trim())
            {
                case "MSC":
                    town = value;
                    break;
                case "UL":
                    street = value;
                    break;
                case "NR_PORZ":
                    number = value;
                    break;
            }
        }

        return town is null && street is null && number is null ? null : new RcnAddress(town, street, number);
    }
}
