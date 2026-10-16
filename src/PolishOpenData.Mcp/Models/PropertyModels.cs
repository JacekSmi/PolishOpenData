using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PolishOpenData.Mcp;

internal sealed record AreaView
{
    /// <summary>"address", "point" or "powiat".</summary>
    public required string Kind { get; init; }

    /// <summary>The centre of a radius search (the geocoded address point or the given coordinates).</summary>
    public GeoPosition? Center { get; init; }

    public int? RadiusMeters { get; init; }

    /// <summary>The four-digit TERYT powiat code, for a powiat search.</summary>
    public string? Powiat { get; init; }
}

internal sealed record PeriodView(string From, string To);

/// <summary>Price per square metre of the market-price sample of a premises search, in whole zloty.</summary>
internal sealed record PriceStatsView
{
    public required int Count { get; init; }

    public decimal? MedianPerSquareMetre { get; init; }

    public decimal? Q1PerSquareMetre { get; init; }

    public decimal? Q3PerSquareMetre { get; init; }

    /// <summary>True when fewer than 5 transactions are in the sample.</summary>
    public required bool SmallSample { get; init; }

    public required int ExcludedMissingPriceOrArea { get; init; }

    /// <summary>Rows where only a share of the premises was sold.</summary>
    public required int ExcludedShare { get; init; }

    public required int ExcludedNonMarketKind { get; init; }

    public required int ExcludedOtherRight { get; init; }
}

/// <summary>One transaction row; fields that do not apply to the kind (or are unknown) are left out.</summary>
internal sealed record TransactionView
{
    public required string Date { get; init; }

    /// <summary>"primary" or "secondary".</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Market { get; init; }

    /// <summary>The transaction kind as published, e.g. wolnyRynek.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SellerType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuyerType { get; init; }

    /// <summary>The gross price of the whole transaction in zloty (repeated on every row of a transaction over several items).</summary>
    public required decimal TotalPrice { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DocumentNumber { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Address { get; init; }

    /// <summary>WGS-84.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeoPosition? Position { get; init; }

    // premises
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Function { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Rooms { get; init; }

    /// <summary>The floor; -1 is below ground.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Floor { get; init; }

    /// <summary>Premises and buildings.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? UsableAreaSquareMetres { get; init; }

    /// <summary>The price of this premises, parcel or building in zloty.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Price { get; init; }

    /// <summary>Premises only, whole zloty.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PricePerSquareMetre { get; init; }

    // parcels
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParcelId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? LandAreaSquareMetres { get; init; }

    // buildings
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuildingKind { get; init; }
}

internal sealed record PropertyPricesResult
{
    public required AreaView Area { get; init; }

    public required PeriodView Period { get; init; }

    /// <summary>"premises", "parcels" or "buildings".</summary>
    public required string Kind { get; init; }

    /// <summary>"any", "primary" or "secondary".</summary>
    public required string Market { get; init; }

    /// <summary>"residential", "garage", "commercial", "other" or "any"; null unless the kind is premises.</summary>
    public string? PremisesFunction { get; init; }

    /// <summary>Price per square metre; premises only.</summary>
    public PriceStatsView? Statistics { get; init; }

    /// <summary>The transactions found in the area, before the maxTransactions cap.</summary>
    public required int TransactionCount { get; init; }

    /// <summary>The date of the newest transaction found (yyyy-MM-dd); null when there is none.</summary>
    public string? NewestTransaction { get; init; }

    /// <summary>Newest first, at most maxTransactions.</summary>
    public required IReadOnlyList<TransactionView> Transactions { get; init; }

    /// <summary>Found but not listed because of maxTransactions.</summary>
    public required int TransactionsOmitted { get; init; }

    /// <summary>The address point the search was centred on, for a search by address.</summary>
    public GeocodeMatch? MatchedAddress { get; init; }

    /// <summary>The geocoder's matches when an address did not resolve to a single address point.</summary>
    public IReadOnlyList<GeocodeMatch>? Candidates { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }

    public required IReadOnlyList<SourceInfo> Sources { get; init; }
}
