using System;
using PolishOpenData.Geo;

namespace PolishOpenData.Geo.Core.Tests;

public class CoordinateTests
{
    // Control pair from ULDK's own answers for parcel 146510_8.0507.41/1 (first vertex, srid 2180 and 4326).
    [Fact]
    public void Pl1992_to_wgs84_matches_gugik_within_1e7_degree()
    {
        var wgs = new Pl1992Point(638144.500192729, 486022.681429996).ToWgs84();
        Assert.Equal(52.2229074776239, wgs.Latitude, 1e-7);
        Assert.Equal(21.0230574318539, wgs.Longitude, 1e-7);
    }

    [Fact]
    public void Wgs84_to_pl1992_matches_gugik_within_1cm()
    {
        var pl = new Wgs84Point(52.2229074776239, 21.0230574318539).ToPl1992();
        Assert.Equal(638144.500192729, pl.Easting, 0.01);
        Assert.Equal(486022.681429996, pl.Northing, 0.01);
    }

    [Theory]
    [InlineData(49.0, 14.1)]
    [InlineData(54.9, 24.2)]
    [InlineData(49.0, 24.2)]
    [InlineData(54.9, 14.1)]
    public void Round_trip_is_stable_at_the_corners_of_poland(double lat, double lon)
    {
        var back = new Wgs84Point(lat, lon).ToPl1992().ToWgs84();
        Assert.Equal(lat, back.Latitude, 1e-9);
        Assert.Equal(lon, back.Longitude, 1e-9);
    }

    [Fact]
    public void Wgs84Point_swapped_axes_throw() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Wgs84Point(21.02, 52.22));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(500000, 5300000)]
    [InlineData(double.NaN, 486000)]
    [InlineData(500000, double.PositiveInfinity)]
    public void Pl1992Point_outside_poland_throws(double e, double n)
    {
        Assert.False(Pl1992Point.IsInPoland(e, n));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pl1992Point(e, n));
    }

    [Fact]
    public void Bbox_center_is_the_midpoint()
    {
        var bbox = new Pl1992Bbox(new Pl1992Point(600000, 480000), new Pl1992Point(600100, 480300));
        Assert.Equal(new Pl1992Point(600050, 480150), bbox.Center);
    }

    [Fact]
    public void ToString_uses_invariant_culture()
    {
        Assert.Equal("E 638144.50, N 486022.68", new Pl1992Point(638144.500192729, 486022.681429996).ToString());
        Assert.Equal("52.2229075, 21.0230574", new Wgs84Point(52.2229074776239, 21.0230574318539).ToString());
    }

    [Fact]
    public void Points_have_no_deconstruct_to_a_bare_pair()
    {
        Assert.Null(typeof(Pl1992Point).GetMethod("Deconstruct"));
        Assert.Null(typeof(Wgs84Point).GetMethod("Deconstruct"));
    }
}
