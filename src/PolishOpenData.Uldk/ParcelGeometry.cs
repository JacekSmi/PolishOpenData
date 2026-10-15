namespace PolishOpenData.Uldk;

/// <summary>Which boundary, if any, to fetch with a parcel.</summary>
public enum ParcelGeometryKind
{
    /// <summary>No boundary.</summary>
    None,

    /// <summary>The boundary in PL-1992 (EPSG:2180), the service's native system.</summary>
    Pl1992,

    /// <summary>The boundary in WGS-84 (EPSG:4326); costs one extra request.</summary>
    Wgs84,
}

/// <summary>A parcel boundary as WKT.</summary>
public sealed class ParcelGeometry
{
    /// <summary>The EPSG code of <see cref="Wkt"/>: 2180 (easting northing) or 4326 (longitude latitude).</summary>
    public int Srid { get; init; }

    /// <summary>The geometry in WKT, without the <c>SRID=…;</c> prefix.</summary>
    public required string Wkt { get; init; }
}
