using System;
using System.Collections.Generic;
using System.Linq;
using PolishOpenData.Demo.Pricing;

namespace PolishOpenData.Demo.Tests;

public class PriceColorScaleTests
{
    private static decimal[] OneToTwenty => Enumerable.Range(1, 20).Select(i => (decimal)i).ToArray();

    private static int[] CountPerClass(PriceColorScale scale, IEnumerable<decimal> prices)
    {
        var counts = new int[5];
        foreach (var price in prices)
        {
            counts[scale.ClassOf(price)]++;
        }

        return counts;
    }

    // Hue in degrees (0 to 360) from a "#rrggbb" string; a grey has no hue and is reported as 0, which the red test then rejects.
    private static double Hue(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
        var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
        var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));
        if (delta == 0)
        {
            return 0;
        }

        var sector = max == r ? (g - b) / delta % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        var hue = sector * 60;
        return hue < 0 ? hue + 360 : hue;
    }

    // Brightness as the weighted sum of the sRGB channels (0 to 255); enough to tell a ramp that gets lighter from one that does not.
    private static double Luma(string hex) =>
        (0.2126 * Convert.ToInt32(hex.Substring(1, 2), 16)) + (0.7152 * Convert.ToInt32(hex.Substring(3, 2), 16)) + (0.0722 * Convert.ToInt32(hex.Substring(5, 2), 16));

    [Fact]
    public void Classes_follow_quantile_edges()
    {
        var scale = PriceColorScale.Create(OneToTwenty);

        // Linear interpolation at position (n - 1) * p, the same rule as the Rcn library's quantile: 19 * 0.2 = 3.8 gives 4 + 0.8 * (5 - 4) = 4.8.
        Assert.Equal([4.8m, 8.6m, 12.4m, 16.2m], scale.Breaks);
        Assert.Equal([4, 4, 4, 4, 4], CountPerClass(scale, OneToTwenty));
    }

    [Fact]
    public void A_price_equal_to_an_edge_belongs_to_the_higher_class()
    {
        // With six values the edges fall exactly on the values at ranks 1 to 4: 10, 20, 30, 40.
        var scale = PriceColorScale.Create([0m, 10m, 20m, 30m, 40m, 50m]);

        Assert.Equal([10m, 20m, 30m, 40m], scale.Breaks);
        Assert.Equal(0, scale.ClassOf(9.99m));
        Assert.Equal(1, scale.ClassOf(10m));
        Assert.Equal(2, scale.ClassOf(20m));
        Assert.Equal(3, scale.ClassOf(30m));
        Assert.Equal(4, scale.ClassOf(40m));
        Assert.Equal(0, scale.ClassOf(-1m));
        Assert.Equal(0, scale.ClassOf(0m));
        Assert.Equal(4, scale.ClassOf(1_000_000_000m));
    }

    [Fact]
    public void The_order_of_the_input_does_not_matter()
    {
        var shuffled = OneToTwenty.OrderBy(v => v % 7).ThenBy(v => v).ToArray();

        Assert.NotEqual(OneToTwenty, shuffled);
        Assert.Equal(PriceColorScale.Create(OneToTwenty).Breaks, PriceColorScale.Create(shuffled).Breaks);
    }

    [Fact]
    public void Outliers_do_not_wash_out_the_scale()
    {
        // 19 ordinary prices from 10 000 to 11 800 and one at 1 000 000 (about 100 times the rest).
        var prices = Enumerable.Range(0, 19).Select(i => 10_000m + (i * 100m)).Append(1_000_000m).ToArray();
        var scale = PriceColorScale.Create(prices);

        Assert.All(scale.Breaks, edge => Assert.InRange(edge, 10_000m, 11_800m));
        Assert.Equal([4, 4, 4, 4, 4], CountPerClass(scale, prices));
        Assert.Equal(4, scale.ClassOf(1_000_000m));
    }

    [Fact]
    public void Single_value_and_empty_input_are_valid()
    {
        // Fewer than five values, or all values equal: no edges, and every price falls into the middle class (2).
        var inputs = new decimal[][]
        {
            [],
            [21_444m],
            [21_444m, 30_000m],
            [1m, 2m, 3m, 4m],
            [25_000m, 25_000m, 25_000m, 25_000m, 25_000m, 25_000m, 25_000m],
        };

        foreach (var input in inputs)
        {
            var scale = PriceColorScale.Create(input);

            Assert.Empty(scale.Breaks);
            foreach (var price in new[] { 0m, 2m, 21_444m, 25_000m, 1_000_000_000m })
            {
                Assert.Equal(2, scale.ClassOf(price));
                Assert.Equal(PriceColorScale.Palette[2], scale.ColorOf(price));
            }
        }
    }

    [Fact]
    public void Five_different_values_are_enough_for_a_full_scale()
    {
        var scale = PriceColorScale.Create([10m, 20m, 30m, 40m, 50m]);

        Assert.Equal(4, scale.Breaks.Count);
        Assert.Equal([0, 1, 2, 3, 4], new[] { 10m, 20m, 30m, 40m, 50m }.Select(scale.ClassOf));
    }

    [Fact]
    public void Tied_prices_keep_four_non_decreasing_edges_and_valid_classes()
    {
        // Nine equal prices and one higher: all four edges sit on the tie, so the tie and everything above it is class 4 and the other classes stay empty.
        var prices = Enumerable.Repeat(1m, 9).Append(5m).ToArray();
        var scale = PriceColorScale.Create(prices);

        Assert.Equal([1m, 1m, 1m, 1m], scale.Breaks);
        Assert.Equal([0, 0, 0, 0, 10], CountPerClass(scale, prices));
        Assert.Equal(0, scale.ClassOf(0.5m));
    }

    [Fact]
    public void Palette_has_five_distinct_colours_and_no_red_hue()
    {
        var palette = PriceColorScale.Palette;

        Assert.Equal(5, palette.Count);
        Assert.Equal(5, palette.Distinct().Count());
        Assert.All(palette, hex =>
        {
            Assert.Equal(7, hex.Length);
            Assert.Equal('#', hex[0]);
            Assert.All(hex.Skip(1), c => Assert.True(c is (>= '0' and <= '9') or (>= 'a' and <= 'f'), $"{hex} is not lowercase #rrggbb"));

            // The parcel outline is bright red, so no dot class may sit between 345 and 15 degrees (a grey is reported as 0 and fails too).
            var hue = Hue(hex);
            Assert.False(hue >= 345 || hue <= 15, $"{hex} has red hue {hue:F0}");
        });
    }

    [Fact]
    public void Palette_gets_lighter_with_price()
    {
        var luma = PriceColorScale.Palette.Select(Luma).ToArray();

        Assert.Equal(luma.OrderBy(v => v), luma);
        Assert.True(luma[^1] - luma[0] > 100, "the ends of the ramp must differ clearly");
    }

    [Fact]
    public void ColorOf_is_the_palette_entry_of_ClassOf()
    {
        var scale = PriceColorScale.Create(OneToTwenty);

        for (var price = 0m; price <= 21m; price += 0.5m)
        {
            Assert.Equal(PriceColorScale.Palette[scale.ClassOf(price)], scale.ColorOf(price));
        }

        Assert.Equal(PriceColorScale.Palette[0], scale.ColorOf(1m));
        Assert.Equal(PriceColorScale.Palette[4], scale.ColorOf(20m));
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => PriceColorScale.Create(null!));
    }
}
