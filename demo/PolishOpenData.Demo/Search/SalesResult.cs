using PolishOpenData.Geo;
using PolishOpenData.Rcn;

namespace PolishOpenData.Demo.Search;

/// <summary>How a sales search ended.</summary>
public enum SalesOutcome
{
    /// <summary>At least one sale lies within the radius; <see cref="SalesResult.Summary"/> and <see cref="SalesResult.Groups"/> are set.</summary>
    Ok,

    /// <summary>No sale lies within the radius in this period.</summary>
    NoSales,

    /// <summary>RCN holds more sales than one answer carries, so there are no groups and no statistics: ask for a smaller radius or a shorter period.</summary>
    TooManySales,

    /// <summary>The place is not in Poland (or the search square leaves the grid RCN uses); nothing was asked.</summary>
    OutOfRange,
}

/// <summary>The sales that share one map point (RCN premises rows often sit on exactly the same point), newest first.</summary>
/// <param name="Location">The point the dot is drawn at.</param>
/// <param name="Sales">At least one sale, newest first; sales of one day keep the order RCN gave.</param>
public sealed record SaleGroup(Wgs84Point Location, IReadOnlyList<SaleView> Sales);

/// <summary>
/// What a place search found. <paramref name="Groups"/> are ordered by their newest sale, newest first.
/// <paramref name="TotalCount"/> counts every sale in the circle, the excluded ones too; <paramref name="Summary"/> covers only the counted ones.
/// <paramref name="Newest"/> is the latest date of all sales in the circle, as <c>property_prices</c> reports it, not only the counted ones.
/// </summary>
/// <param name="Outcome">How the search ended; the other values are empty unless it is <see cref="SalesOutcome.Ok"/>.</param>
/// <param name="Groups">The sales by map point.</param>
/// <param name="TotalCount">The number of sales in the circle.</param>
/// <param name="Summary">Price per square metre statistics over the counted sales.</param>
/// <param name="Newest">The date of the newest sale in the circle.</param>
/// <param name="From">The first day of the period asked for.</param>
/// <param name="To">The last day of the period asked for (today).</param>
public sealed record SalesResult(
    SalesOutcome Outcome,
    IReadOnlyList<SaleGroup> Groups,
    int TotalCount,
    RcnPriceSummary? Summary,
    DateOnly? Newest,
    DateOnly From,
    DateOnly To);
