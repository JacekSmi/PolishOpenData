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
    public static RcnPriceSummary Summarize(this RcnTransactions transactions)
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

            if (row.PricePerSquareMetre is not { } perMetre) missing++;
            else if (row.Share is not null && row.Share != "1/1") share++;
            else if (row.Kind is not null && row.Kind != "wolnyRynek") kind++;
            else if (row.Right is not null && row.Right != "wlasnoscLokaluWrazZPrawemZwiazanym") right++;
            else
            {
                values.Add(perMetre);
                if (oldest is null || row.Date < oldest) oldest = row.Date;
                if (newest is null || row.Date > newest) newest = row.Date;
            }
        }

        values.Sort();
        var excluded = new RcnExclusions(missing, share, kind, right);
        return values.Count == 0
            ? new RcnPriceSummary(0, null, null, null, true, null, null, excluded)
            : new RcnPriceSummary(values.Count, Quantile(values, 0.5m), Quantile(values, 0.25m), Quantile(values, 0.75m),
                values.Count < SmallSampleBelow, oldest, newest, excluded);
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
