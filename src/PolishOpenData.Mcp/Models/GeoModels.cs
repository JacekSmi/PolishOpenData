using System.Collections.Generic;

namespace PolishOpenData.Mcp;

/// <summary>A WGS-84 position in degrees, as the tools write it.</summary>
internal sealed record GeoPosition(double Latitude, double Longitude);

internal sealed record GeocodeMatch
{
    /// <summary>"address", "street", "city" or "other".</summary>
    public required string Kind { get; init; }

    public string? City { get; init; }

    public string? Street { get; init; }

    public string? Number { get; init; }

    public string? PostalCode { get; init; }

    /// <summary>TERYT code of the commune (gmina).</summary>
    public string? Teryt { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    /// <summary>How well the query matched, 0 to 1.</summary>
    public double? Accuracy { get; init; }
}

internal sealed record GeocodeResult
{
    public required IReadOnlyList<GeocodeMatch> Matches { get; init; }

    /// <summary>Set when there is no match.</summary>
    public string? Note { get; init; }

    public required SourceInfo Source { get; init; }
}

internal sealed record ParcelView
{
    public required string ParcelId { get; init; }

    public required string Number { get; init; }

    public string? Region { get; init; }

    public string? Commune { get; init; }

    public string? County { get; init; }

    public string? Voivodeship { get; init; }

    /// <summary>The middle of the parcel's bounding box (not necessarily inside an irregular parcel).</summary>
    public required GeoPosition Center { get; init; }

    /// <summary>The full boundary as WKT in EPSG:4326 (longitude latitude); only with includeGeometry.</summary>
    public string? GeometryWkt { get; init; }
}

internal sealed record FindParcelResult
{
    public required IReadOnlyList<ParcelView> Parcels { get; init; }

    /// <summary>The address point the parcel was found at, for a search by address.</summary>
    public GeocodeMatch? MatchedAddress { get; init; }

    /// <summary>The geocoder's matches when an address did not resolve to a single address point.</summary>
    public IReadOnlyList<GeocodeMatch>? Candidates { get; init; }

    public string? Note { get; init; }

    public required IReadOnlyList<SourceInfo> Sources { get; init; }
}
