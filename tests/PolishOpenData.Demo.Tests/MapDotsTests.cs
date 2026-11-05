using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PolishOpenData.Demo.Map;
using PolishOpenData.Demo.Pricing;
using PolishOpenData.Demo.Search;
using PolishOpenData.Geo;
using PolishOpenData.Rcn;

namespace PolishOpenData.Demo.Tests;

public class MapDotsTests
{
    private static readonly Wgs84Point Here = new(52.2285, 21.0172);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // The scale of the prices 1 to 20: edges at 4.8, 8.6, 12.4 and 16.2, so a price of 2 is class 0, 5 class 1, 10 class 2 and 18 class 4.
    private static PriceColorScale Scale { get; } = PriceColorScale.Create(Enumerable.Range(1, 20).Select(i => (decimal)i).ToArray());

    private static SaleView Counted(decimal pricePerSquareMetre) => Sale(pricePerSquareMetre, counted: true);

    private static SaleView Excluded(decimal pricePerSquareMetre) => Sale(pricePerSquareMetre, counted: false);

    private static SaleView Sale(decimal pricePerSquareMetre, bool counted) =>
        new(
            Here,
            new DateOnly(2026, 9, 1),
            "secondary",
            "ulica Krucza 50",
            2,
            3,
            50m,
            pricePerSquareMetre * 50m,
            pricePerSquareMetre,
            counted,
            counted ? RcnExclusionReason.None : RcnExclusionReason.Share);

    private static SaleGroup Group(Wgs84Point location, params SaleView[] sales) => new(location, sales);

    private static SaleGroup Group(params SaleView[] sales) => Group(Here, sales);

    [Fact]
    public void Counted_groups_use_the_scale_colour()
    {
        // Newest first: the first sale is 18 (class 4), the mean of the counted ones is 7 (class 1), and the excluded 20 would pull the median to 10 (class 2).
        // Only the median of the counted sales, 2, is class 0.
        var dots = MapDots.From([Group(Counted(18m), Counted(1m), Counted(2m), Excluded(20m))], Scale);

        var dot = Assert.Single(dots);
        Assert.True(dot.Counted);
        Assert.Equal(PriceColorScale.Palette[0], dot.Color);
        Assert.Equal(Scale.ColorOf(2m), dot.Color);
    }

    [Fact]
    public void The_median_of_an_even_number_of_sales_is_the_mean_of_the_middle_two()
    {
        // 5 is class 1; the lower middle (4) would be class 0 and the upper middle (6) class 1 too, so only the mean tells 5 from 4.
        var dots = MapDots.From([Group(Counted(4m), Counted(6m))], Scale);

        Assert.Equal(PriceColorScale.Palette[1], Assert.Single(dots).Color);
    }

    [Fact]
    public void Groups_of_different_classes_get_their_own_colours()
    {
        var dots = MapDots.From([Group(Counted(2m)), Group(Counted(10m)), Group(Counted(19m))], Scale);

        Assert.Equal([PriceColorScale.Palette[0], PriceColorScale.Palette[2], PriceColorScale.Palette[4]], dots.Select(d => d.Color));
    }

    [Fact]
    public void A_group_with_only_excluded_sales_is_not_counted()
    {
        var dots = MapDots.From([Group(Excluded(10m), Excluded(12m)), Group(Counted(10m), Excluded(12m))], Scale);

        Assert.False(dots[0].Counted);
        Assert.Equal(MapDots.ExcludedColor, dots[0].Color);
        Assert.DoesNotContain(MapDots.ExcludedColor, PriceColorScale.Palette);

        // One counted sale is enough for a group to count, and the excluded one does not change its colour.
        Assert.True(dots[1].Counted);
        Assert.Equal(Scale.ColorOf(10m), dots[1].Color);
    }

    [Fact]
    public void Count_is_the_number_of_sales_in_the_group()
    {
        var dots = MapDots.From(
            [
                Group(Counted(10m)),
                Group(Counted(10m), Counted(11m), Excluded(12m)),
                Group(Excluded(10m), Excluded(12m)),
            ],
            Scale);

        Assert.Equal([1, 3, 2], dots.Select(d => d.Count));
    }

    [Fact]
    public void Index_matches_the_group_position()
    {
        var location = new Wgs84Point(52.1, 20.9);
        var dots = MapDots.From([Group(Here, Counted(10m)), Group(location, Excluded(10m)), Group(Here, Counted(3m))], Scale);

        Assert.Equal([0, 1, 2], dots.Select(d => d.Index));
        Assert.Equal(location.Latitude, dots[1].Lat);
        Assert.Equal(location.Longitude, dots[1].Lon);
    }

    [Fact]
    public void No_groups_make_no_dots()
    {
        Assert.Empty(MapDots.From([], Scale));
    }

    [Fact]
    public void Dots_serialize_with_the_names_the_script_reads()
    {
        // JS interop writes camelCase JSON (the web defaults); map.js reads exactly these names.
        var json = JsonSerializer.Serialize(new MapDot(52.5, 21.25, "#21918c", true, 3, 7), Web);

        Assert.Equal("""{"lat":52.5,"lon":21.25,"color":"#21918c","counted":true,"count":3,"index":7}""", json);
    }

    [Fact]
    public void No_library_types_cross_the_boundary()
    {
        // The dots go through System.Text.Json interop, so the record may hold nothing but numbers, text and booleans: no Wgs84Point, no SaleView.
        var allowed = new HashSet<Type> { typeof(double), typeof(string), typeof(bool), typeof(int) };

        var properties = typeof(MapDot).GetProperties();

        Assert.Equal(["Color", "Count", "Counted", "Index", "Lat", "Lon"], properties.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, p => Assert.True(allowed.Contains(p.PropertyType), $"{p.Name} is a {p.PropertyType}"));
        Assert.All(typeof(MapDot).GetConstructors().SelectMany(c => c.GetParameters()), p => Assert.True(allowed.Contains(p.ParameterType), $"{p.Name} is a {p.ParameterType}"));
    }
}
