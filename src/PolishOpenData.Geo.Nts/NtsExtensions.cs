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

    private static NtsGeometryServices ServicesOf(int srid) =>
        srid switch { 2180 => Pl1992Services, 4326 => Wgs84Services, _ => ServicesFor(srid) };

    private static NtsGeometryServices ServicesFor(int srid) =>
        new(CoordinateArraySequenceFactory.Instance, new PrecisionModel(), srid);

    /// <summary>
    /// Parses the parcel boundary into a NetTopologySuite geometry whose <c>SRID</c> is <see cref="ParcelGeometry.Srid"/>.
    /// </summary>
    /// <remarks>
    /// Measures are in the units of the coordinate system: metres for 2180 (fetch it with
    /// <see cref="ParcelGeometryKind.Pl1992"/> for <c>Area</c> and <c>Distance</c>), degrees for 4326, where <c>Area</c> is
    /// in square degrees and not useful. A boundary that is not a valid polygon (<c>IsValid == false</c>) is returned as is.
    /// Geometries of the SRIDs 2180 and 4326 share one factory each; any other SRID gets a new factory per call.
    /// </remarks>
    /// <param name="geometry">The boundary returned by the ULDK client.</param>
    /// <returns>The parsed geometry, usually a Polygon or MultiPolygon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="ParcelGeometry.Wkt"/> starts with an <c>SRID=</c> prefix.</exception>
    /// <exception cref="ParseException">The WKT cannot be parsed, including a ring that is not closed.</exception>
    public static Geometry ToNtsGeometry(this ParcelGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        // An EWKT prefix would override Srid inside the reader and stamp a different SRID than the caller declared.
        if (geometry.Wkt.AsSpan().TrimStart().StartsWith("SRID=".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Wkt must not carry an SRID= prefix; set ParcelGeometry.Srid instead.", nameof(geometry));
        }

        try
        {
            return new WKTReader(ServicesOf(geometry.Srid)).Read(geometry.Wkt);
        }
        catch (ArgumentException ex)
        {
            // NTS reports some malformed input (an unclosed ring, a NaN ordinate) as ArgumentException.
            throw new ParseException(ex.Message, ex);
        }
    }

    /// <summary>A NetTopologySuite point with <c>X</c> = easting, <c>Y</c> = northing and <c>SRID</c> 2180.</summary>
    /// <param name="point">The PL-1992 point.</param>
    /// <returns>The point, in the same frame as a 2180 <see cref="ToNtsGeometry"/> result.</returns>
    public static Point ToNtsPoint(this Pl1992Point point) =>
        Pl1992Services.CreateGeometryFactory(2180).CreatePoint(new Coordinate(point.Easting, point.Northing));

    /// <summary>A NetTopologySuite point with <c>X</c> = longitude, <c>Y</c> = latitude and <c>SRID</c> 4326.</summary>
    /// <param name="point">The WGS-84 point.</param>
    /// <returns>The point, in the same frame as a 4326 <see cref="ToNtsGeometry"/> result. Note the swapped order: X is longitude.</returns>
    public static Point ToNtsPoint(this Wgs84Point point) =>
        Wgs84Services.CreateGeometryFactory(4326).CreatePoint(new Coordinate(point.Longitude, point.Latitude));
}
