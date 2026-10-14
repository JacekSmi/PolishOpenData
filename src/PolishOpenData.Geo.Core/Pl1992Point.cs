using System;

namespace PolishOpenData.Geo;

/// <summary>
/// A position in PL-1992 (EPSG:2180), the national metric grid used by GUGiK services, in metres.
/// Easting comes first, then northing; there is no X/Y here because the registries disagree on which is which.
/// </summary>
/// <param name="Easting">Metres east of the false origin; 100 000 to 900 000.</param>
/// <param name="Northing">Metres north of the false origin; 100 000 to 830 000.</param>
/// <exception cref="ArgumentOutOfRangeException">A coordinate is NaN, infinite or outside the range that covers Poland.</exception>
public readonly record struct Pl1992Point(double Easting, double Northing)
{
    /// <summary>Metres east of the false origin.</summary>
    public double Easting { get; } = InRange(Easting, 100_000, 900_000) ? Easting : throw Out(nameof(Easting), Easting);

    /// <summary>Metres north of the false origin.</summary>
    public double Northing { get; } = InRange(Northing, 100_000, 830_000) ? Northing : throw Out(nameof(Northing), Northing);

    /// <summary>True when both coordinates are finite and inside the range that covers Poland.</summary>
    public static bool IsInPoland(double easting, double northing) =>
        InRange(easting, 100_000, 900_000) && InRange(northing, 100_000, 830_000);

    /// <summary>Converts to WGS-84 (EPSG:4326), accurate to well under a millimetre.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The point is inside the PL-1992 range but its WGS-84 position falls outside Poland's latitude and longitude range.</exception>
    public Wgs84Point ToWgs84() => Pl1992Projection.ToWgs84(this);

    /// <summary>Formats as <c>E 638144.50, N 486022.68</c> (invariant culture, centimetres).</summary>
    public override string ToString() =>
        FormattableString.Invariant($"E {Easting:F2}, N {Northing:F2}");

    private static bool InRange(double value, double min, double max) => value >= min && value <= max;

    private static ArgumentOutOfRangeException Out(string name, double value) =>
        new(name, value, "Outside the PL-1992 range that covers Poland (or not a finite number).");
}
