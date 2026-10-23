using System;
using System.Collections.Generic;
using System.Linq;
using PolishOpenData.Geo;

namespace PolishOpenData.Rcn.Tests;

public class RcnPriceStatisticsTests
{
    private static RcnTransaction Row(
        decimal? perMetre = 1000,
        string? share = null,
        string? kind = null,
        string? right = null,
        DateOnly? date = null,
        RcnLayer layer = RcnLayer.Premises,
        decimal? area = 1) =>
        new()
        {
            Layer = layer,
            Id = Guid.NewGuid().ToString(),
            Date = date ?? new DateOnly(2026, 1, 1),
            TotalPrice = 1,
            Location = new Pl1992Point(637800, 486300),
            Fields = new Dictionary<string, string>(),
            Share = share,
            Kind = kind,
            Right = right,
            UsableArea = area,
            PremisesPrice = perMetre,
        };

    private static RcnPriceSummary Summarize(IEnumerable<RcnTransaction> rows, bool truncated = false) =>
        new RcnTransactions(rows.ToList(), truncated).Summarize();

    private static RcnPriceSummary Summarize(params decimal[] values) => Summarize(values.Select(v => Row(v)));

    [Fact]
    public void Median_of_odd_and_even_samples()
    {
        Assert.Equal(30m, Summarize(50, 10, 30, 20, 40).Median);
        Assert.Equal(25m, Summarize(40, 10, 30, 20).Median);
    }

    [Fact]
    public void Quartiles_use_linear_interpolation()
    {
        var nine = Summarize(1, 2, 3, 4, 5, 6, 7, 8, 9);
        Assert.Equal((3m, 5m, 7m), (nine.Q1, nine.Median, nine.Q3));

        var four = Summarize(10, 20, 30, 40);
        Assert.Equal((17.5m, 25m, 32.5m), (four.Q1, four.Median, four.Q3));
    }

    [Fact]
    public void Fewer_than_five_is_a_small_sample()
    {
        Assert.True(Summarize(1, 2, 3, 4).SmallSample);
        Assert.False(Summarize(1, 2, 3, 4, 5).SmallSample);

        var none = Summarize();
        Assert.Equal(0, none.Count);
        Assert.Null(none.Median);
        Assert.Null(none.Q1);
        Assert.Null(none.Q3);
        Assert.Null(none.Oldest);
        Assert.Null(none.Newest);
        Assert.True(none.SmallSample);
    }

    [Fact]
    public void Missing_price_or_area_is_excluded_and_counted()
    {
        var summary = Summarize(new[] { Row(1000), Row(null), Row(0), Row(1000, area: 0), Row(1000, area: null) });

        Assert.Equal(1, summary.Count);
        Assert.Equal(4, summary.Excluded.MissingPriceOrArea);
    }

    [Fact]
    public void A_share_is_excluded_but_one_over_one_is_not()
    {
        var summary = Summarize(new[] { Row(share: "1/64"), Row(share: "1/1"), Row(share: null) });

        Assert.Equal(2, summary.Count);
        Assert.Equal(1, summary.Excluded.Share);
    }

    [Fact]
    public void Auction_sale_is_excluded_empty_and_wolnyRynek_kind_are_counted()
    {
        var summary = Summarize(new[] { Row(kind: "sprzedazPrzetargowa"), Row(kind: "wolnyRynek"), Row(kind: null) });

        Assert.Equal(2, summary.Count);
        Assert.Equal(1, summary.Excluded.NonMarketKind);
    }

    [Fact]
    public void Other_right_is_excluded()
    {
        var summary = Summarize(new[]
        {
            Row(right: "spoldzielczePrawoDoLokalu"),
            Row(right: "wlasnoscLokaluWrazZPrawemZwiazanym"),
            Row(right: null),
        });

        Assert.Equal(2, summary.Count);
        Assert.Equal(1, summary.Excluded.OtherRight);
    }

    [Fact]
    public void Exclusion_precedence_counts_each_row_once()
    {
        var summary = Summarize(new[]
        {
            Row(null, share: "1/2", kind: "sprzedazPrzetargowa", right: "x"),
            Row(share: "1/2", kind: "sprzedazPrzetargowa", right: "x"),
            Row(kind: "sprzedazPrzetargowa", right: "x"),
            Row(right: "x"),
        });

        Assert.Equal(0, summary.Count);
        Assert.Equal(new RcnExclusions(1, 1, 1, 1), summary.Excluded);
    }

    [Fact]
    public void Oldest_and_newest_cover_counted_rows_only()
    {
        var summary = Summarize(new[]
        {
            Row(date: new DateOnly(2026, 3, 1)),
            Row(date: new DateOnly(2026, 5, 28)),
            Row(date: new DateOnly(2020, 1, 1), share: "1/2"),
            Row(null, date: new DateOnly(2030, 1, 1)),
        });

        Assert.Equal(new DateOnly(2026, 3, 1), summary.Oldest);
        Assert.Equal(new DateOnly(2026, 5, 28), summary.Newest);
    }

    [Fact]
    public void Summarize_refuses_a_truncated_result()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Summarize(new[] { Row() }, truncated: true));

        Assert.Contains("Narrow the area", ex.Message);
    }

    [Fact]
    public void Summarize_refuses_parcel_rows()
    {
        Assert.Throws<InvalidOperationException>(() => Summarize(new[] { Row(), Row(layer: RcnLayer.Parcels) }));
    }
}
