using System;

namespace PolishOpenData.Rcn;

/// <summary>How many rows <see cref="RcnPriceStatistics.Summarize"/> left out of the market-price sample, each row counted once under the first reason that applies.</summary>
/// <param name="MissingPriceOrArea">No price per square metre: the price or the usable area is missing or not above zero.</param>
/// <param name="Share">Only a share of the premises was sold (anything but <c>1/1</c>).</param>
/// <param name="NonMarketKind">A transaction kind other than <c>wolnyRynek</c>, e.g. an auction sale.</param>
/// <param name="OtherRight">A right other than ownership of the premises with the attached right.</param>
public sealed record RcnExclusions(int MissingPriceOrArea, int Share, int NonMarketKind, int OtherRight);

/// <summary>Price per square metre statistics over the market-price sample of a premises query.</summary>
/// <param name="Count">The rows in the sample.</param>
/// <param name="Median">The median price per square metre in zloty; null when <paramref name="Count"/> is 0.</param>
/// <param name="Q1">The first quartile (linear interpolation between closest ranks); null when <paramref name="Count"/> is 0.</param>
/// <param name="Q3">The third quartile (linear interpolation between closest ranks); null when <paramref name="Count"/> is 0.</param>
/// <param name="SmallSample"><see langword="true"/> when fewer than five rows are in the sample, so the numbers are weak evidence.</param>
/// <param name="Oldest">The earliest deed date among the counted rows.</param>
/// <param name="Newest">The latest deed date among the counted rows.</param>
/// <param name="Excluded">The rows left out, by reason.</param>
public sealed record RcnPriceSummary(
    int Count,
    decimal? Median,
    decimal? Q1,
    decimal? Q3,
    bool SmallSample,
    DateOnly? Oldest,
    DateOnly? Newest,
    RcnExclusions Excluded);
