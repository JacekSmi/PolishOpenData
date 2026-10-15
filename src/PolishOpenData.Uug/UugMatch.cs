using PolishOpenData.Geo;

namespace PolishOpenData.Uug;

/// <summary>What a <see cref="UugMatch"/> points at, from the <c>type</c> of the service's answer.</summary>
public enum UugMatchKind
{
    /// <summary>A single address point.</summary>
    Address,

    /// <summary>A street; <see cref="UugMatch.Location"/> is a representative point on it, not an address.</summary>
    Street,

    /// <summary>A city or village; <see cref="UugMatch.Location"/> is a representative point in it.</summary>
    City,

    /// <summary>Any other kind the service may add.</summary>
    Other,
}

/// <summary>One match of the UUG geocoder.</summary>
public sealed class UugMatch
{
    /// <summary>What the match is: an address, a street or a city.</summary>
    public UugMatchKind Kind { get; init; }

    /// <summary>The city name, e.g. <c>Warszawa</c>.</summary>
    public string? City { get; init; }

    /// <summary>The part of the city (<i>część miejscowości</i>), when the service gives one.</summary>
    public string? CityPart { get; init; }

    /// <summary>The street as the register writes it, with its prefix, e.g. <c>ulica Krucza</c>.</summary>
    public string? Street { get; init; }

    /// <summary>The building number, e.g. <c>50</c> or <c>3/5</c>.</summary>
    public string? Number { get; init; }

    /// <summary>The postal code, e.g. <c>00-025</c>.</summary>
    public string? PostalCode { get; init; }

    /// <summary>The TERYT code of the municipality (<i>gmina</i>), e.g. <c>146501</c>.</summary>
    public string? Teryt { get; init; }

    /// <summary>The SIMC code of the locality.</summary>
    public string? Simc { get; init; }

    /// <summary>The ULIC code of the street.</summary>
    public string? Ulic { get; init; }

    /// <summary>Where the match is.</summary>
    public Wgs84Point Location { get; init; }

    /// <summary>How well the query matched, from 0 to 1; null for a reverse match.</summary>
    public double? Accuracy { get; init; }

    /// <summary>The distance from the queried point, in metres; set only for a reverse match.</summary>
    public double? DistanceMeters { get; init; }
}
