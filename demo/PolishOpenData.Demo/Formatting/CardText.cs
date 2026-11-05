using PolishOpenData.Demo.Map;
using PolishOpenData.Demo.Pricing;
using PolishOpenData.Demo.Search;
using PolishOpenData.Rcn;
using PolishOpenData.Uldk;

namespace PolishOpenData.Demo.Formatting;

/// <summary>One line of the map legend: a dot colour and the prices it stands for. <paramref name="Hollow"/> marks the grey ring of a sale left out of the statistics.</summary>
public sealed record LegendEntry(string Color, string Text, bool Hollow);

/// <summary>
/// The sentences of the price card, so the components only bind them. Numbers go through <see cref="PolishFormat"/>; nothing here reads the current culture.
/// </summary>
internal static class CardText
{
    /// <summary>The note under a median of fewer than five counted sales (the sales here may be many more).</summary>
    public const string SmallSample = "Fewer than 5 sales in the statistics: treat these figures as rough.";

    /// <summary>The disclaimer every result carries.</summary>
    public const string NotAValuation = "Informational, not a valuation.";

    /// <summary>The unit of the legend's ranges.</summary>
    public const string LegendUnit = "Median price per m\u00B2 at a dot (z\u0142)";

    /// <summary>What the card says instead of figures for a search that found none to show; <c>null</c> for <see cref="SalesOutcome.Ok"/>. Too many sales is never "no sales".</summary>
    public static string? ForOutcome(SalesOutcome outcome) => outcome switch
    {
        SalesOutcome.NoSales => "No sales found here in this period.",
        SalesOutcome.TooManySales => "More than 1,000 sales match here. Choose a smaller radius or a shorter period.",
        SalesOutcome.OutOfRange => "Only places in Poland are supported.",
        _ => null,
    };

    /// <summary>"250 m", "500 m", "1 km", "2 km".</summary>
    public static string Radius(int meters) => meters % 1000 == 0 ? $"{meters / 1000} km" : $"{meters} m";

    /// <summary>"12 months", "24 months", "5 years".</summary>
    public static string Period(int months) => months % 12 == 0 && months > 24 ? $"{months / 12} years" : $"{months} months";

    /// <summary>"Flats sold within 500 m in the last 24 months".</summary>
    public static string Scope(int radiusMeters, int periodMonths) => $"Flats sold within {Radius(radiusMeters)} in the last {Period(periodMonths)}";

    /// <summary>"12 sales here, 10 in the statistics": every sale in the circle, then the counted ones.</summary>
    public static string Sales(int total, int counted) =>
        $"{PolishFormat.Count(total)} {(total == 1 ? "sale" : "sales")} here, {(counted == 0 ? "none" : PolishFormat.Count(counted))} in the statistics";

    /// <summary>Why sales are left out of the statistics, with counts, or <c>null</c> when none is.</summary>
    public static string? Exclusions(RcnExclusions excluded)
    {
        ArgumentNullException.ThrowIfNull(excluded);

        string?[] parts =
        [
            Part(excluded.Share, "sale of a share", "sales of a share"),
            Part(excluded.NonMarketKind, "non-market sale", "non-market sales"),
            Part(excluded.MissingPriceOrArea, "with no price or area", "with no price or area"),
            Part(excluded.OtherRight, "with another kind of right", "with another kind of right"),
        ];
        var told = parts.OfType<string>().ToList();
        return told.Count == 0 ? null : "Left out: " + string.Join(", ", told) + ".";

        static string? Part(int count, string one, string many) => count == 0 ? null : $"{PolishFormat.Count(count)} {(count == 1 ? one : many)}";
    }

    /// <summary>"Q1 to Q3: 19 120 to 24 301 zl/m2" (with U+0142 and U+00B2).</summary>
    public static string Quartiles(decimal q1, decimal q3) => $"Q1 to Q3: {PolishFormat.Whole(q1)} to {PolishFormat.PerSquareMetre(q3)}";

    /// <summary>The newest sale's date and the reminder that RCN lags.</summary>
    public static string Newest(DateOnly date) => $"Newest sale: {PolishFormat.Date(date)}. RCN publishes with a delay of months.";

    /// <summary>"Parcel 146518_8.0505.25/2 (number 25/2), region, commune", leaving out what ULDK did not give, or "No parcel found at this point.".</summary>
    public static string Parcel(UldkParcel? parcel)
    {
        if (parcel is null)
        {
            return "No parcel found at this point.";
        }

        var id = parcel.Id.ToString();
        var head = id.Length == 0 ? $"Parcel number {parcel.Number}" : $"Parcel {id} (number {parcel.Number})";
        return string.Join(", ", new[] { head, parcel.RegionName, parcel.CommuneName }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>Why one sale is not in the statistics, or <c>null</c> for a counted one.</summary>
    public static string? Excluded(RcnExclusionReason reason) => reason switch
    {
        RcnExclusionReason.Share => "Not in the statistics: a share of the flat was sold.",
        RcnExclusionReason.NonMarketKind => "Not in the statistics: not a free-market sale.",
        RcnExclusionReason.MissingPriceOrArea => "Not in the statistics: the price or the area is missing.",
        RcnExclusionReason.OtherRight => "Not in the statistics: another kind of right than ownership was sold.",
        _ => null,
    };

    /// <summary>"Primary market" or "Secondary market"; <c>null</c> when RCN does not say.</summary>
    public static string? Market(string? market) => market switch
    {
        "primary" => "Primary market",
        "secondary" => "Secondary market",
        _ => null,
    };

    /// <summary>The floor as published; a negative one is below ground.</summary>
    public static string Floor(int floor) => floor < 0 ? $"floor {floor} (below ground)" : $"floor {floor}";

    /// <summary>"1 room", "3 rooms".</summary>
    public static string Rooms(int rooms) => rooms == 1 ? "1 room" : $"{rooms} rooms";

    /// <summary>
    /// The legend of the dot colours: one range per non-empty class of <paramref name="scale"/> (classes between two equal edges hold nothing and are left out),
    /// one "every counted sale" colour when the scale has no edges, and the grey ring when some sales are left out of the statistics.
    /// </summary>
    public static IReadOnlyList<LegendEntry> Legend(PriceColorScale scale, bool anyExcluded, bool anyCounted = true)
    {
        ArgumentNullException.ThrowIfNull(scale);

        var entries = new List<LegendEntry>();
        var palette = PriceColorScale.Palette;
        var edges = scale.Breaks;
        if (anyCounted && edges.Count == 0)
        {
            entries.Add(new(palette[2], "every counted sale", false));
        }
        else if (anyCounted)
        {
            for (var i = 0; i <= edges.Count; i++)
            {
                var text = i == 0 ? $"below {PolishFormat.Whole(edges[0])}"
                    : i == edges.Count ? $"{PolishFormat.Whole(edges[i - 1])} and above"
                    : edges[i - 1] == edges[i] ? null
                    : $"{PolishFormat.Whole(edges[i - 1])} to {PolishFormat.Whole(edges[i])}";
                if (text is not null)
                {
                    entries.Add(new(palette[i], text, false));
                }
            }
        }

        if (anyExcluded)
        {
            entries.Add(new(MapDots.ExcludedColor, "not in the statistics", true));
        }

        return entries;
    }
}
