using System;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;
using NetTopologySuite.IO;
using PolishOpenData.Uldk;

namespace PolishOpenData.Geo.Nts;

/// <summary>Converts PolishOpenData geometry into NetTopologySuite types.</summary>
public static class NtsExtensions
{
    // The services object fixes the SRID the reader stamps on what it parses. It is thread-safe, so the two SRIDs the
    // services return are shared; WKTReader itself is not, so a reader is created per call.
    private static readonly NtsGeometryServices Pl1992Services = ServicesFor(2180);
    private static readonly NtsGeometryServices Wgs84Services = ServicesFor(4326);

    private static NtsGeometryServices ServicesFor(int srid) =>
        new(CoordinateArraySequenceFactory.Instance, new PrecisionModel(), srid);

    /// <summary>
    /// Parses the parcel boundary into a NetTopologySuite geometry whose <c>SRID</c> is <see cref="ParcelGeometry.Srid"/>.
    /// </summary>
    /// <remarks>
    /// Measures are in the units of the coordinate system: metres for 2180 (fetch it with
    /// <see cref="ParcelGeometryKind.Pl1992"/> for <c>Area</c> and <c>Distance</c>), degrees for 4326, where <c>Area</c> is
    /// in square degrees and not useful. A boundary that is not a valid polygon (<c>IsValid == false</c>) is returned as is.
    /// </remarks>
    /// <param name="geometry">The boundary returned by the ULDK client.</param>
    /// <returns>The parsed geometry, usually a Polygon or MultiPolygon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ParseException">The WKT cannot be parsed.</exception>
    public static Geometry ToNtsGeometry(this ParcelGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        return new WKTReader(geometry.Srid switch { 2180 => Pl1992Services, 4326 => Wgs84Services, var other => ServicesFor(other) }).Read(geometry.Wkt);
    }
}
