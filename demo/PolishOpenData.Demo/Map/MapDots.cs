using PolishOpenData.Demo.Pricing;
using PolishOpenData.Demo.Search;

namespace PolishOpenData.Demo.Map;

/// <summary>
/// One dot as the map script draws it. Only numbers, text and a boolean, so the interop's camelCased JSON
/// (<c>lat</c>, <c>lon</c>, <c>color</c>, <c>counted</c>, <c>count</c>, <c>index</c>) is all the script has to know about.
/// </summary>
/// <param name="Lat">Latitude of the group's point.</param>
/// <param name="Lon">Longitude of the group's point.</param>
/// <param name="Color">The fill as <c>#rrggbb</c>: the price class colour of a counted group, <see cref="MapDots.ExcludedColor"/> otherwise.</param>
/// <param name="Counted">Whether any sale of the group is in the price statistics; a group that is not counted is drawn as a small hollow dot.</param>
/// <param name="Count">The number of sales in the group, the excluded ones too; the map shows it when it is above one.</param>
/// <param name="Index">The position of the group in the result's list, handed back when the dot is clicked.</param>
internal sealed record MapDot(double Lat, double Lon, string Color, bool Counted, int Count, int Index);

/// <summary>Turns the sale groups of a result into the dots the map draws.</summary>
internal static class MapDots
{
    /// <summary>The grey of a group with no counted sale. Not in <see cref="PriceColorScale.Palette"/>, so a hollow grey dot never reads as a price class.</summary>
    public const string ExcludedColor = "#6f7a89";

    /// <summary>
    /// One dot per group, in the order of <paramref name="groups"/> (<see cref="MapDot.Index"/> is the position). A group is counted when at least one
    /// of its sales is, and then takes the colour of the median price per square metre of its counted sales; the excluded sales of the group do not
    /// count towards the colour, only towards <see cref="MapDot.Count"/>.
    /// </summary>
    public static IReadOnlyList<MapDot> From(IReadOnlyList<SaleGroup> groups, PriceColorScale scale)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(scale);

        var dots = new MapDot[groups.Count];
        for (var i = 0; i < dots.Length; i++)
        {
            var group = groups[i];
            var prices = group.Sales.Where(s => s.Counted && s.PricePerSquareMetre is not null).Select(s => s.PricePerSquareMetre!.Value).Order().ToArray();
            dots[i] = prices.Length == 0
                ? new MapDot(group.Location.Latitude, group.Location.Longitude, ExcludedColor, false, group.Sales.Count, i)
                : new MapDot(group.Location.Latitude, group.Location.Longitude, scale.ColorOf(Median(prices)), true, group.Sales.Count, i);
        }

        return dots;
    }

    // The middle value, or the mean of the two middle ones; the values are sorted and there is at least one.
    private static decimal Median(decimal[] sorted) =>
        sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
}
