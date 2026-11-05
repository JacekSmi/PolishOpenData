using System;
using System.Collections.Generic;

namespace PolishOpenData.Rcn;

/// <summary>Price per square metre statistics over RCN premises transactions.</summary>
public static class RcnPriceStatistics
{
    private const int SmallSampleBelow = 5;

    /// <summary>
    /// Summarises the price per square metre of the market-price sample: premises rows with a price per square metre,
    /// a share of <c>1/1</c> or none, a kind of <c>wolnyRynek</c> or none, and ownership of the premises with the
    /// attached right or no right stated. It does not filter by premises function: garages and shops are counted
    /// unless the query already asked for one function.
    /// </summary>
    /// <param name="transactions">A complete premises answer.</param>
    /// <exception cref="InvalidOperationException">The answer is truncated (narrow the query) or contains rows that are not premises.</exception>
    public static RcnPriceSummary Summarize(this RcnTransactions transactions) => SummarizeBy(transactions, Classify);

    // The classifier is a parameter only so that a test can hand the tally a reason that the enum does not have yet.
    internal static RcnPriceSummary SummarizeBy(RcnTransactions transactions, Func<RcnTransaction, RcnExclusionReason> classify)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        if (transactions.Truncated)
        {
            throw new InvalidOperationException(
                "The result is truncated, so statistics over it would be biased. Narrow the area or the period and query again.");
        }

        int missing = 0, share = 0, kind = 0, right = 0;
        var values = new List<decimal>();
        DateOnly? oldest = null, newest = null;
        foreach (var row in transactions.Items)
        {
            if (row.Layer != RcnLayer.Premises)
            {
                throw new InvalidOperationException("Price statistics need premises rows; query the premises layer.");
            }

            var reason = classify(row);
            switch (reason)
            {
                case RcnExclusionReason.MissingPriceOrArea: missing++; break;
                case RcnExclusionReason.Share: share++; break;
                case RcnExclusionReason.NonMarketKind: kind++; break;
                case RcnExclusionReason.OtherRight: right++; break;
                case RcnExclusionReason.None:
                    values.Add(row.PricePerSquareMetre!.Value);
                    if (oldest is null || row.Date < oldest) oldest = row.Date;
                    if (newest is null || row.Date > newest) newest = row.Date;
                    break;
                default:
                    // A reason added to the enum without a branch here would otherwise be counted as a market sale.
                    throw new InvalidOperationException($"The exclusion reason {reason} is not handled by the statistics.");
            }
        }

        values.Sort();
        var excluded = new RcnExclusions(missing, share, kind, right);
        return values.Count == 0
            ? new RcnPriceSummary(0, null, null, null, true, null, null, excluded)
            : new RcnPriceSummary(values.Count, Quantile(values, 0.5m), Quantile(values, 0.25m), Quantile(values, 0.75m),
                values.Count < SmallSampleBelow, oldest, newest, excluded);
    }

    /// <summary>
    /// Says whether a premises row counts in the market-price sample of <see cref="Summarize"/> and, if not, why:
    /// the first reason that applies wins (no price per square metre, a share other than <c>1/1</c>, a kind other
    /// than <c>wolnyRynek</c>, a right other than ownership of the premises with the attached right). A missing
    /// share, kind or right is allowed.
    /// </summary>
    /// <param name="transaction">A premises row.</param>
    /// <returns><see cref="RcnExclusionReason.None"/> when the row counts in the statistics.</returns>
    /// <exception cref="ArgumentException">The row is not a premises row.</exception>
    public static RcnExclusionReason Classify(RcnTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Layer != RcnLayer.Premises)
        {
            throw new ArgumentException("Price statistics need premises rows; classify a row from the premises layer.", nameof(transaction));
        }

        if (transaction.PricePerSquareMetre is null) return RcnExclusionReason.MissingPriceOrArea;
        if (transaction.Share is not null && transaction.Share != "1/1") return RcnExclusionReason.Share;
        if (transaction.Kind is not null && transaction.Kind != "wolnyRynek") return RcnExclusionReason.NonMarketKind;
        if (transaction.Right is not null && transaction.Right != "wlasnoscLokaluWrazZPrawemZwiazanym") return RcnExclusionReason.OtherRight;
        return RcnExclusionReason.None;
    }

    // Linear interpolation between closest ranks, position (n-1)*p, on sorted values.
    private static decimal Quantile(List<decimal> sorted, decimal p)
    {
        var position = (sorted.Count - 1) * p;
        var lower = (int)decimal.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Count - 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}
