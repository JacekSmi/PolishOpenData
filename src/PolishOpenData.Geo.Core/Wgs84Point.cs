using System;

namespace PolishOpenData.Geo;

/// <summary>
/// A position in WGS-84 (EPSG:4326) in degrees, restricted to Poland. Latitude comes first, then longitude;
/// the ranges do not overlap, so swapped axes are caught on construction.
/// </summary>
// Not a positional record: that would generate Deconstruct(out double, out double), an unnamed pair of numbers.
// The constructor parameters keep PascalCase names so that named arguments read like the properties.
public readonly record struct Wgs84Point
{
    /// <summary>Creates a point, checking that it lies in the range that covers Poland.</summary>
    /// <param name="Latitude">Degrees north; 48.9 to 55.0.</param>
    /// <param name="Longitude">Degrees east; 14.0 to 24.2.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is NaN, infinite or outside the range that covers Poland.</exception>
    public Wgs84Point(double Latitude, double Longitude)
    {
        this.Latitude = Latitude >= 48.9 && Latitude <= 55.0 ? Latitude : throw Out(nameof(Latitude), Latitude);
        this.Longitude = Longitude >= 14.0 && Longitude <= 24.2 ? Longitude : throw Out(nameof(Longitude), Longitude);
    }

    /// <summary>Degrees north.</summary>
    public double Latitude { get; }

    /// <summary>Degrees east.</summary>
    public double Longitude { get; }

    /// <summary>Converts to PL-1992 (EPSG:2180), accurate to well under a millimetre.</summary>
    public Pl1992Point ToPl1992() => Pl1992Projection.ToPl1992(this);

    /// <summary>Formats as <c>52.2229075, 21.0230574</c> (invariant culture, 7 decimals, about 1 cm).</summary>
    public override string ToString() =>
        FormattableString.Invariant($"{Latitude:F7}, {Longitude:F7}");

    private static ArgumentOutOfRangeException Out(string name, double value) =>
        new(name, value, "Outside the WGS-84 range that covers Poland (latitude 48.9-55.0, longitude 14.0-24.2; latitude comes first).");
}
