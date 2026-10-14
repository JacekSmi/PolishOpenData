namespace PolishOpenData.Geo;

/// <summary>An axis-aligned rectangle in PL-1992 (EPSG:2180): south-west and north-east corners.</summary>
/// <param name="Min">The corner with the smallest easting and northing.</param>
/// <param name="Max">The corner with the largest easting and northing.</param>
public readonly record struct Pl1992Bbox(Pl1992Point Min, Pl1992Point Max)
{
    /// <summary>The midpoint of the rectangle.</summary>
    public Pl1992Point Center => new((Min.Easting + Max.Easting) / 2, (Min.Northing + Max.Northing) / 2);
}
