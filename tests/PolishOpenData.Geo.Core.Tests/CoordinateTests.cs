using System;
using PolishOpenData.Geo;

namespace PolishOpenData.Geo.Core.Tests;

public class CoordinateTests
{
    // Control pairs from ULDK's own answers (first vertex of result=geom_wkt, srid 2180 and 4326):
    // 146510_8.0507.41/1 in Warsaw, and 200505_2.0014.51 near Poland's eastern edge (recorded 2026-10-15).
    [Theory]
    [InlineData(638144.500192729, 486022.681429996, 52.2229074776239, 21.0230574318539)]
    [InlineData(811786.778983666, 536805.693048013, 52.6072959061805, 23.6068998827821)]
    public void Pl1992_to_wgs84_matches_gugik_within_1e9_degree(double easting, double northing, double latitude, double longitude)
    {
        var wgs = new Pl1992Point(easting, northing).ToWgs84();
        Assert.Equal(latitude, wgs.Latitude, 1e-9);
        Assert.Equal(longitude, wgs.Longitude, 1e-9);
    }

    [Theory]
    [InlineData(638144.500192729, 486022.681429996, 52.2229074776239, 21.0230574318539)]
    [InlineData(811786.778983666, 536805.693048013, 52.6072959061805, 23.6068998827821)]
    public void Wgs84_to_pl1992_matches_gugik_within_1mm(double easting, double northing, double latitude, double longitude)
    {
        var pl = new Wgs84Point(latitude, longitude).ToPl1992();
        Assert.Equal(easting, pl.Easting, 0.001);
        Assert.Equal(northing, pl.Northing, 0.001);
    }

    [Fact]
    public void Pl1992_point_outside_the_wgs84_range_throws_without_the_swapped_axes_hint()
    {
        // The south-west corner of the grid box lies south of latitude 48.9.
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new Pl1992Point(100_000, 100_000).ToWgs84());

        Assert.Contains("PL-1992 point lies outside", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("latitude comes first", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(52.2, 21.0, true)]
    [InlineData(21.0, 52.2, false)]
    [InlineData(0, 0, false)]
    [InlineData(double.NaN, 21.0, false)]
    public void Wgs84Point_IsInPoland(double latitude, double longitude, bool expected) =>
        Assert.Equal(expected, Wgs84Point.IsInPoland(latitude, longitude));

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

    [Theory]
    [InlineData(600100, 480000)]
    [InlineData(600000, 480300)]
    public void Bbox_with_min_east_or_north_of_max_throws(double minEasting, double minNorthing) =>
        Assert.Throws<ArgumentException>(() => new Pl1992Bbox(new Pl1992Point(minEasting, minNorthing), new Pl1992Point(600050, 480150)));

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
