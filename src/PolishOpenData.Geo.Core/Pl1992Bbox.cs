using System;

namespace PolishOpenData.Geo;

/// <summary>An axis-aligned rectangle in PL-1992 (EPSG:2180): south-west and north-east corners.</summary>
/// <param name="Min">The corner with the smallest easting and northing.</param>
/// <param name="Max">The corner with the largest easting and northing.</param>
/// <exception cref="ArgumentException"><paramref name="Min"/> lies east or north of <paramref name="Max"/>.</exception>
public readonly record struct Pl1992Bbox(Pl1992Point Min, Pl1992Point Max)
{
    /// <summary>The corner with the smallest easting and northing.</summary>
    // Checked here because a positional record has no constructor body; a `with` expression skips the check.
    public Pl1992Point Min { get; init; } = Min.Easting <= Max.Easting && Min.Northing <= Max.Northing
        ? Min
        : throw new ArgumentException("The minimum corner lies east or north of the maximum corner.", nameof(Min));

    /// <summary>The midpoint of the rectangle.</summary>
    public Pl1992Point Center => new((Min.Easting + Max.Easting) / 2, (Min.Northing + Max.Northing) / 2);
}
