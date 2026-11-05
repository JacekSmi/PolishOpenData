using PolishOpenData.Geo;
using PolishOpenData.Rcn;

namespace PolishOpenData.Demo.Search;

/// <summary>
/// One premises sale as the page shows it: what RCN publishes about the flat, and whether the sale counts in the price statistics.
/// <paramref name="Price"/> is the price of this premises (<c>PremisesPrice</c>), never the price of the whole deed;
/// <paramref name="PricePerSquareMetre"/> is that price over the usable area in whole zloty, as <c>property_prices</c> rounds it.
/// <paramref name="Market"/> is <c>"primary"</c>, <c>"secondary"</c> or <c>null</c>. Nothing about the notary, the deed, the buyer or the seller is carried.
/// </summary>
/// <param name="Location">Where the map draws the sale.</param>
/// <param name="Date">The date of the deed.</param>
/// <param name="Market">Primary or secondary market, when RCN says.</param>
/// <param name="Address">Street, number and town as RCN writes them, or <c>null</c> when there is none.</param>
/// <param name="Floor">The floor; -1 is below ground.</param>
/// <param name="Rooms">The number of rooms.</param>
/// <param name="UsableArea">The usable area in square metres.</param>
/// <param name="Price">The price of this premises in zloty.</param>
/// <param name="PricePerSquareMetre">The price per square metre in whole zloty; <c>null</c> without a price or an area above zero.</param>
/// <param name="Counted">Whether the sale is in the price statistics (<paramref name="Exclusion"/> is <see cref="RcnExclusionReason.None"/>).</param>
/// <param name="Exclusion">Why the sale is left out of the statistics, or <see cref="RcnExclusionReason.None"/>.</param>
public sealed record SaleView(
    Wgs84Point Location,
    DateOnly Date,
    string? Market,
    string? Address,
    int? Floor,
    int? Rooms,
    decimal? UsableArea,
    decimal? Price,
    decimal? PricePerSquareMetre,
    bool Counted,
    RcnExclusionReason Exclusion);
