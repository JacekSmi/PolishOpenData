using System;
using System.Linq;
using PolishOpenData.Demo.Formatting;
using PolishOpenData.Demo.Map;
using PolishOpenData.Demo.Pricing;
using PolishOpenData.Demo.Search;
using PolishOpenData.Geo;
using PolishOpenData.Rcn;
using PolishOpenData.Uldk;

namespace PolishOpenData.Demo.Tests;

public class CardTextTests
{
    // U+00A0 is the group separator; zl = "z" + U+0142; m2 = "m" + U+00B2.
    private const string PerM2 = "\u00A0z\u0142/m\u00B2";

    [Theory]
    [InlineData(250, "250 m")]
    [InlineData(500, "500 m")]
    [InlineData(1000, "1 km")]
    [InlineData(2000, "2 km")]
    public void Radius_in_words(int meters, string expected) => Assert.Equal(expected, CardText.Radius(meters));

    [Theory]
    [InlineData(12, "12 months")]
    [InlineData(24, "24 months")]
    [InlineData(60, "5 years")]
    public void Period_in_words(int months, string expected) => Assert.Equal(expected, CardText.Period(months));

    [Theory]
    [InlineData(SalesOutcome.NoSales, "No sales found here in this period.")]
    [InlineData(SalesOutcome.TooManySales, "More than 1,000 sales match here. Choose a smaller radius or a shorter period.")]
    [InlineData(SalesOutcome.OutOfRange, "Only places in Poland are supported.")]
    [InlineData(SalesOutcome.Ok, null)]
    public void Outcomes_without_figures_have_their_sentence(SalesOutcome outcome, string? expected) => Assert.Equal(expected, CardText.ForOutcome(outcome));

    [Fact]
    public void Scope_names_the_radius_and_the_period() =>
        Assert.Equal("Flats sold within 1 km in the last 5 years", CardText.Scope(1000, 60));

    [Theory]
    [InlineData(12, 10, "12 sales here, 10 in the statistics")]
    [InlineData(1, 1, "1 sale here, 1 in the statistics")]
    [InlineData(3, 0, "3 sales here, none in the statistics")]
    [InlineData(1234, 1000, "1\u00A0234 sales here, 1\u00A0000 in the statistics")]
    public void Sales_line(int total, int counted, string expected) => Assert.Equal(expected, CardText.Sales(total, counted));

    [Fact]
    public void The_small_sample_note_is_about_the_statistics_not_the_sales_here()
    {
        // 12 sales here and 3 counted: the note must not read as if there were fewer than 5 sales here.
        Assert.Equal("12 sales here, 3 in the statistics", CardText.Sales(12, 3));
        Assert.Equal("Fewer than 5 sales in the statistics: treat these figures as rough.", CardText.SmallSample);
    }

    [Fact]
    public void No_exclusions_give_no_line() => Assert.Null(CardText.Exclusions(new RcnExclusions(0, 0, 0, 0)));

    [Fact]
    public void Exclusions_are_told_in_plain_words_with_counts() =>
        Assert.Equal(
            "Left out: 2 sales of a share, 1 non-market sale, 3 with no price or area, 1 with another kind of right.",
            CardText.Exclusions(new RcnExclusions(MissingPriceOrArea: 3, Share: 2, NonMarketKind: 1, OtherRight: 1)));

    [Fact]
    public void Exclusions_leave_out_the_reasons_with_no_sale() =>
        Assert.Equal("Left out: 1 sale of a share, 4 non-market sales.", CardText.Exclusions(new RcnExclusions(0, 1, 4, 0)));

    [Fact]
    public void Quartiles_carry_the_unit_once() =>
        Assert.Equal("Q1 to Q3: 19\u00A0120 to 24\u00A0301" + PerM2, CardText.Quartiles(19_120.4m, 24_300.5m));

    [Fact]
    public void Newest_sale_warns_about_the_delay() =>
        Assert.Equal("Newest sale: 2026-08-14. RCN publishes with a delay of months.", CardText.Newest(new DateOnly(2026, 8, 14)));

    [Fact]
    public void A_missing_parcel_is_said_so() => Assert.Equal("No parcel found at this point.", CardText.Parcel(null));

    [Fact]
    public void Parcel_line_has_the_id_number_region_and_commune()
    {
        var parcel = new UldkParcel { Id = ParcelId.Parse("146518_8.0505.25/2"), Number = "25/2", RegionName = "0505", CommuneName = "Warszawa" };

        Assert.Equal("Parcel 146518_8.0505.25/2 (number 25/2), 0505, Warszawa", CardText.Parcel(parcel));
    }

    [Fact]
    public void Parcel_line_skips_what_is_missing() =>
        Assert.Equal("Parcel number 12/3", CardText.Parcel(new UldkParcel { Number = "12/3" }));

    [Theory]
    [InlineData(RcnExclusionReason.Share, "Not in the statistics: a share of the flat was sold.")]
    [InlineData(RcnExclusionReason.NonMarketKind, "Not in the statistics: not a free-market sale.")]
    [InlineData(RcnExclusionReason.MissingPriceOrArea, "Not in the statistics: the price or the area is missing.")]
    [InlineData(RcnExclusionReason.OtherRight, "Not in the statistics: another kind of right than ownership was sold.")]
    public void Exclusion_reason_in_plain_words(RcnExclusionReason reason, string expected) => Assert.Equal(expected, CardText.Excluded(reason));

    [Fact]
    public void A_counted_sale_has_no_exclusion_note() => Assert.Null(CardText.Excluded(RcnExclusionReason.None));

    [Theory]
    [InlineData("primary", "Primary market")]
    [InlineData("secondary", "Secondary market")]
    [InlineData(null, null)]
    [InlineData("other", null)]
    public void Market_in_words(string? market, string? expected) => Assert.Equal(expected, CardText.Market(market));

    [Theory]
    [InlineData(3, "floor 3")]
    [InlineData(0, "floor 0")]
    [InlineData(-1, "floor -1 (below ground)")]
    public void Floor_as_published(int floor, string expected) => Assert.Equal(expected, CardText.Floor(floor));

    [Theory]
    [InlineData(1, "1 room")]
    [InlineData(3, "3 rooms")]
    public void Rooms_in_words(int rooms, string expected) => Assert.Equal(expected, CardText.Rooms(rooms));

    [Fact]
    public void Legend_has_a_range_per_class()
    {
        // The prices 1 to 20 cut at 4.8, 8.6, 12.4 and 16.2; whole zloty in the legend.
        var scale = PriceColorScale.Create(Enumerable.Range(1, 20).Select(i => (decimal)i * 1000).ToArray());

        var legend = CardText.Legend(scale, anyExcluded: false);

        Assert.Equal(PriceColorScale.Palette, legend.Select(e => e.Color));
        Assert.Equal(
            ["below 4\u00A0800", "4\u00A0800 to 8\u00A0600", "8\u00A0600 to 12\u00A0400", "12\u00A0400 to 16\u00A0200", "16\u00A0200 and above"],
            legend.Select(e => e.Text));
        Assert.All(legend, e => Assert.False(e.Hollow));
    }

    [Fact]
    public void Legend_skips_the_classes_between_equal_edges()
    {
        // 10 is the 20, 40 and 60 % quantile, so classes 1 and 2 hold nothing.
        var scale = PriceColorScale.Create([10m, 10m, 10m, 10m, 10m, 10m, 10m, 10m, 20m, 30m]);
        Assert.Equal(scale.Breaks[0], scale.Breaks[2]);

        var legend = CardText.Legend(scale, anyExcluded: false);

        Assert.Equal([PriceColorScale.Palette[0], PriceColorScale.Palette[3], PriceColorScale.Palette[4]], legend.Select(e => e.Color));
    }

    [Fact]
    public void Legend_without_edges_has_one_colour_and_the_excluded_entry()
    {
        var legend = CardText.Legend(PriceColorScale.Create([10m, 20m]), anyExcluded: true);

        Assert.Collection(
            legend,
            e => Assert.Equal((PriceColorScale.Palette[2], "every counted sale", false), (e.Color, e.Text, e.Hollow)),
            e => Assert.Equal((MapDots.ExcludedColor, "not in the statistics", true), (e.Color, e.Text, e.Hollow)));
    }

    [Fact]
    public void Legend_with_no_counted_sale_has_only_the_excluded_entry()
    {
        var legend = CardText.Legend(PriceColorScale.Create(Array.Empty<decimal>()), anyExcluded: true, anyCounted: false);

        Assert.Equal(MapDots.ExcludedColor, Assert.Single(legend).Color);
    }
}
