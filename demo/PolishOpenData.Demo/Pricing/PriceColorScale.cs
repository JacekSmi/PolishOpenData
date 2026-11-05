namespace PolishOpenData.Demo.Pricing;

/// <summary>
/// Five colour classes for the dots on the map, cut at the 20, 40, 60 and 80 % quantiles of the prices per square metre in view,
/// so a single very high or very low sale cannot wash out the scale the way equal-width classes would.
/// </summary>
public sealed class PriceColorScale
{
    private const int ClassCount = 5;

    // The class every price gets when there is nothing to compare with: the middle of the ramp.
    private const int MiddleClass = 2;

    // The viridis ramp sampled at five points: dark purple (cheapest) to yellow (dearest). It gets steadily lighter, which reads as "more",
    // stays apart for the common kinds of colour-blindness, and has no red: the parcel outline is bright red.
    // Judged by eye on the softened orthophoto (dense streets, a park, forest, fields) with the thin white edge the map gives every counted dot:
    // all five classes read there, the green and the dark purple included, so the ramp is kept as sampled.
    private static readonly string[] Colors = ["#440154", "#3b528b", "#21918c", "#5ec962", "#fde725"];

    private PriceColorScale(decimal[] breaks) => Breaks = Array.AsReadOnly(breaks);

    /// <summary>Five distinct lowercase <c>#rrggbb</c> colours, from the lowest price class (index 0) to the highest (index 4).</summary>
    public static IReadOnlyList<string> Palette { get; } = Array.AsReadOnly(Colors);

    /// <summary>
    /// The four class edges (20, 40, 60, 80 %), in increasing order, or empty when the scale is degenerate (see <see cref="Create"/>).
    /// When many prices are equal, edges can be equal too; the classes in between then stay empty.
    /// </summary>
    public IReadOnlyList<decimal> Breaks { get; }

    /// <summary>
    /// Builds the scale from the prices per square metre of the sales in view. The edges use linear interpolation between the closest
    /// ranks, position <c>(n - 1) * p</c> on the sorted values, which is the rule of the Rcn library's quartiles.
    /// With fewer than five prices, or when all are equal, there is nothing to rank: <see cref="Breaks"/> is empty and every price is class 2.
    /// An empty collection is valid.
    /// </summary>
    public static PriceColorScale Create(IReadOnlyCollection<decimal> pricesPerSquareMetre)
    {
        ArgumentNullException.ThrowIfNull(pricesPerSquareMetre);

        var sorted = pricesPerSquareMetre.Order().ToArray();
        if (sorted.Length < ClassCount || sorted[0] == sorted[^1])
        {
            return new PriceColorScale([]);
        }

        var breaks = new decimal[ClassCount - 1];
        for (var i = 0; i < breaks.Length; i++)
        {
            breaks[i] = Quantile(sorted, (i + 1) / (decimal)ClassCount);
        }

        return new PriceColorScale(breaks);
    }

    /// <summary>
    /// The class of a price, 0 (cheapest) to 4: the number of edges that are at or below it, so a price equal to an edge belongs to the higher class.
    /// A scale without edges answers 2 for every price.
    /// </summary>
    public int ClassOf(decimal price)
    {
        if (Breaks.Count == 0)
        {
            return MiddleClass;
        }

        var result = 0;
        foreach (var edge in Breaks)
        {
            if (price >= edge)
            {
                result++;
            }
        }

        return result;
    }

    /// <summary>The palette colour of <see cref="ClassOf"/>.</summary>
    public string ColorOf(decimal price) => Colors[ClassOf(price)];

    private static decimal Quantile(decimal[] sorted, decimal p)
    {
        var position = (sorted.Length - 1) * p;
        var lower = (int)decimal.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }
}
