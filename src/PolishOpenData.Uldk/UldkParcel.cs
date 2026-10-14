using PolishOpenData.Geo;

namespace PolishOpenData.Uldk;

/// <summary>A cadastral parcel as ULDK reports it.</summary>
public sealed class UldkParcel
{
    /// <summary>The TERYT parcel identifier.</summary>
    public ParcelId Id { get; init; }

    /// <summary>The parcel number within its region, e.g. <c>41/1</c>.</summary>
    public required string Number { get; init; }

    /// <summary>
    /// The name of the cadastral region (<i>obręb</i>) as ULDK writes it, e.g. <c>Krzewina</c>, or a code-like name
    /// such as <c>5-05-07</c> in Warsaw. The region number is <see cref="ParcelId.Region"/>.
    /// </summary>
    public string? RegionName { get; init; }

    /// <summary>The commune name as ULDK writes it, e.g. <c>Warszawa (miasto)</c>. The TERYT code is <see cref="ParcelId.Unit"/>.</summary>
    public string? CommuneName { get; init; }

    /// <summary>The county name as ULDK writes it, e.g. <c>powiat Warszawa</c>.</summary>
    public string? CountyName { get; init; }

    /// <summary>The voivodeship name as ULDK writes it, e.g. <c>mazowieckie</c>.</summary>
    public string? VoivodeshipName { get; init; }

    /// <summary>The bounding box of the parcel in PL-1992.</summary>
    public Pl1992Bbox Extent { get; init; }

    /// <summary>The boundary, when it was asked for.</summary>
    public ParcelGeometry? Geometry { get; init; }

    /// <summary>The data source ULDK names for this parcel, e.g. <c>powiat m. Warszawa (wfs)</c>.</summary>
    public string? DataSource { get; init; }
}
