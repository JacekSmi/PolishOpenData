using System;
using NetTopologySuite.IO;
using PolishOpenData.Geo.Nts;
using PolishOpenData.Uldk;
using Xunit;

namespace PolishOpenData.Geo.Nts.Tests;

public class NtsExtensionsTests
{
    private const string Square2180 =
        "POLYGON((638000.5 486000.2,638050.5 486000.2,638050.5 486040.2,638000.5 486040.2,638000.5 486000.2))";

    private static ParcelGeometry Parcel(int srid, string wkt) => new() { Srid = srid, Wkt = wkt };

    [Fact]
    public void ToNtsGeometry_Pl1992Polygon_HasSridAndMetricArea()
    {
        var g = Parcel(2180, Square2180).ToNtsGeometry();

        Assert.Equal("Polygon", g.GeometryType);
        Assert.Equal(2180, g.SRID);
        Assert.Equal(2000, g.Area, 6);
        Assert.True(g.IsValid);
    }

    [Fact]
    public void ToNtsGeometry_Wgs84_KeepsLonLatOrder()
    {
        var g = Parcel(4326, "POLYGON((21 52,21.001 52,21.001 52.001,21 52.001,21 52))").ToNtsGeometry();

        Assert.Equal(4326, g.SRID);
        Assert.Equal(21, g.EnvelopeInternal.MinX);
        Assert.Equal(52, g.EnvelopeInternal.MinY);
    }

    [Fact]
    public void ToNtsGeometry_MultiPolygon_ReturnsMultiPolygon()
    {
        var g = Parcel(2180, "MULTIPOLYGON(((0 0,10 0,10 10,0 10,0 0)),((20 0,30 0,30 10,20 10,20 0)))").ToNtsGeometry();

        Assert.Equal("MultiPolygon", g.GeometryType);
        Assert.Equal(2, g.NumGeometries);
    }

    [Fact]
    public void ToNtsGeometry_PolygonWithHole_SubtractsHoleFromArea()
    {
        var g = Parcel(2180, "POLYGON((0 0,100 0,100 100,0 100,0 0),(10 10,20 10,20 20,10 20,10 10))").ToNtsGeometry();

        Assert.Equal(9900, g.Area, 6);
    }

    [Fact]
    public void ToNtsGeometry_SelfIntersecting_DoesNotThrowAndIsInvalid()
    {
        var g = Parcel(2180, "POLYGON((0 0,10 10,10 0,0 10,0 0))").ToNtsGeometry();

        Assert.False(g.IsValid);
    }

    [Fact]
    public void ToNtsGeometry_GarbageWkt_Throws() =>
        Assert.Throws<ParseException>(() => Parcel(2180, "not wkt").ToNtsGeometry());

    [Fact]
    public void ToNtsGeometry_NullArgument_Throws() =>
        Assert.Throws<ArgumentNullException>(() => NtsExtensions.ToNtsGeometry(null!));

    [Fact]
    public void Pl1992Point_ToNtsPoint_MapsEastingToXAndNorthingToY()
    {
        var p = new Pl1992Point(638025.5, 486020.2).ToNtsPoint();

        Assert.Equal(638025.5, p.X);
        Assert.Equal(486020.2, p.Y);
        Assert.Equal(2180, p.SRID);
    }

    [Fact]
    public void Wgs84Point_ToNtsPoint_MapsLongitudeToXAndLatitudeToY()
    {
        var p = new Wgs84Point(52.1, 21.2).ToNtsPoint();

        Assert.Equal(21.2, p.X);
        Assert.Equal(52.1, p.Y);
        Assert.Equal(4326, p.SRID);
    }

    [Fact]
    public void ParcelGeometry_Contains_Pl1992Point()
    {
        var parcel = Parcel(2180, Square2180).ToNtsGeometry();

        Assert.True(parcel.Contains(new Pl1992Point(638010, 486010).ToNtsPoint()));
        Assert.False(parcel.Contains(new Pl1992Point(638150, 486010).ToNtsPoint()));
    }
}
