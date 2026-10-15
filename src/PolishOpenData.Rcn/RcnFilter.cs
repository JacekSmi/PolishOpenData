using System;
using System.Collections.Generic;
using System.Globalization;
using PolishOpenData.Geo;

namespace PolishOpenData.Rcn;

/// <summary>Builds the OGC FES 2.0 <c>filter</c> parameter of the RCN WFS. Does not validate: the client calls <see cref="RcnQuery.Validate"/> first.</summary>
internal static class RcnFilter
{
    /// <summary>Newest transaction first, ties by <c>gid</c>, so that pages are stable.</summary>
    public const string SortBy = "DOK_DATA DESC,gid";

    public static string TypeName(RcnLayer layer) => layer switch
    {
        RcnLayer.Premises => "ms:lokale",
        RcnLayer.Parcels => "ms:dzialki",
        RcnLayer.Buildings => "ms:budynki",
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null),
    };

    public static string Build(RcnQuery query)
    {
        // Fixed order: bbox, powiat, From, To, market, premises function.
        var conditions = new List<string>();
        if (query.Area is { } area)
        {
            conditions.Add(Bbox(area));
        }

        if (query.PowiatCode is not null)
        {
            conditions.Add(Comparison("PropertyIsEqualTo", "TERYT", query.PowiatCode));
        }

        if (query.From is { } from)
        {
            conditions.Add(Comparison("PropertyIsGreaterThanOrEqualTo", "DOK_DATA", Date(from)));
        }

        if (query.To is { } to)
        {
            conditions.Add(Comparison("PropertyIsLessThanOrEqualTo", "DOK_DATA", Date(to)));
        }

        if (query.Market is { } market)
        {
            conditions.Add(Comparison("PropertyIsEqualTo", "TRAN_RODZAJ_RYNKU", market == RcnMarket.Primary ? "rynekPierwotny" : "rynekWtorny"));
        }

        if (query.PremisesFunction is { } function)
        {
            conditions.Add(Comparison("PropertyIsEqualTo", "LOK_FUNKCJA", FunctionCode(function)));
        }

        var body = string.Concat(conditions);
        if (conditions.Count > 1)
        {
            body = "<fes:And>" + body + "</fes:And>";
        }

        return "<fes:Filter xmlns:fes=\"http://www.opengis.net/fes/2.0\" xmlns:gml=\"http://www.opengis.net/gml/3.2\">" + body + "</fes:Filter>";
    }

    // The one place where the envelope is written northing first, as EPSG:2180 demands in this URN form.
    private static string Bbox(Pl1992Bbox area) =>
        "<fes:BBOX><fes:ValueReference>GEOMETRIA</fes:ValueReference>"
        + "<gml:Envelope srsName=\"urn:ogc:def:crs:EPSG::2180\">"
        + "<gml:lowerCorner>" + Number(area.Min.Northing) + " " + Number(area.Min.Easting) + "</gml:lowerCorner>"
        + "<gml:upperCorner>" + Number(area.Max.Northing) + " " + Number(area.Max.Easting) + "</gml:upperCorner>"
        + "</gml:Envelope></fes:BBOX>";

    private static string Comparison(string op, string property, string literal) =>
        "<fes:" + op + "><fes:ValueReference>" + property + "</fes:ValueReference><fes:Literal>" + literal + "</fes:Literal></fes:" + op + ">";

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FunctionCode(RcnPremisesFunction function) => function switch
    {
        RcnPremisesFunction.Residential => "mieszkalna",
        RcnPremisesFunction.Garage => "garaz",
        RcnPremisesFunction.Commercial => "handlowoUslugowa",
        RcnPremisesFunction.Other => "inna",
        _ => throw new ArgumentOutOfRangeException(nameof(function), function, null),
    };
}
