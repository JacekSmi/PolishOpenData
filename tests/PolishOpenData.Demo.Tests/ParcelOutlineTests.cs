using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using PolishOpenData.Demo.Geo;
using PolishOpenData.Geo;
using PolishOpenData.Tests.Shared;
using PolishOpenData.Uldk;

namespace PolishOpenData.Demo.Tests;

public class ParcelOutlineTests
{
    private const string Square4326Json = """{"type":"Polygon","coordinates":[[[20,50],[21,50],[21,51],[20,50]]]}""";

    // The answer to a ULDK request is a status line, then one pipe-delimited line whose last field is SRID=...;WKT;
    // the second fixture holds that EWKT alone.
    private static ParcelGeometry FromFixture(string path)
    {
        var line = Fixture.Read(path).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
        var ewkt = line[(line.LastIndexOf('|') + 1)..];
        var semicolon = ewkt.IndexOf(';', StringComparison.Ordinal);
        return new ParcelGeometry
        {
            Srid = int.Parse(ewkt["SRID=".Length..semicolon], CultureInfo.InvariantCulture),
            Wkt = ewkt[(semicolon + 1)..],
        };
    }

    private static double[] FirstPair(ParcelGeometry geometry) =>
        geometry.Wkt["POLYGON((".Length..].Split(',')[0].Split(' ').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

    private static int VertexCount(ParcelGeometry geometry) => geometry.Wkt.Count(c => c == ',') + 1;

    private static JsonElement Parse(string? geoJson)
    {
        Assert.NotNull(geoJson);
        return JsonDocument.Parse(geoJson).RootElement;
    }

    private static string? Outline(string wkt, int srid = 4326) => ParcelOutline.ToGeoJson(new ParcelGeometry { Srid = srid, Wkt = wkt });

    [Fact]
    public void Pl1992_fixture_is_converted_to_longitude_latitude()
    {
        var geometry = FromFixture("uldk/parcel-146510_8.0507.41_1.txt");
        Assert.Equal(2180, geometry.Srid);
        var expected = new Pl1992Point(FirstPair(geometry)[0], FirstPair(geometry)[1]).ToWgs84();

        var root = Parse(ParcelOutline.ToGeoJson(geometry));

        Assert.Equal("Polygon", root.GetProperty("type").GetString());
        var rings = root.GetProperty("coordinates");
        Assert.Equal(1, rings.GetArrayLength());
        var first = rings[0][0];
        Assert.Equal(expected.Longitude, first[0].GetDouble(), 1e-7);
        Assert.Equal(expected.Latitude, first[1].GetDouble(), 1e-7);
        Assert.InRange(first[0].GetDouble(), 20.8, 21.3); // longitude comes first; this is Warsaw
        Assert.InRange(first[1].GetDouble(), 52.0, 52.4);
        Assert.Equal(VertexCount(geometry), rings[0].GetArrayLength());
        Assert.Equal(rings[0][0].GetRawText(), rings[0][rings[0].GetArrayLength() - 1].GetRawText()); // the closing vertex stays
    }

    [Fact]
    public void Wgs84_fixture_passes_through()
    {
        var geometry = FromFixture("uldk/parcel-146510_8.0507.41_1-wgs84-geom.txt");
        Assert.Equal(4326, geometry.Srid);
        var pair = FirstPair(geometry);

        var ring = Parse(ParcelOutline.ToGeoJson(geometry)).GetProperty("coordinates")[0];

        Assert.Equal(pair[0], ring[0][0].GetDouble(), 1e-7);
        Assert.Equal(pair[1], ring[0][1].GetDouble(), 1e-7);
        Assert.Equal(VertexCount(geometry), ring.GetArrayLength());
    }

    [Fact]
    public void Both_fixtures_describe_the_same_place()
    {
        var fromGrid = Parse(ParcelOutline.ToGeoJson(FromFixture("uldk/parcel-146510_8.0507.41_1.txt"))).GetProperty("coordinates")[0][0];
        var fromDegrees = Parse(ParcelOutline.ToGeoJson(FromFixture("uldk/parcel-146510_8.0507.41_1-wgs84-geom.txt"))).GetProperty("coordinates")[0][0];

        // The same parcel, answered in both systems: the conversion must land on the vertex ULDK itself reports in degrees (about 10 cm).
        Assert.Equal(fromDegrees[0].GetDouble(), fromGrid[0].GetDouble(), 1e-6);
        Assert.Equal(fromDegrees[1].GetDouble(), fromGrid[1].GetDouble(), 1e-6);
    }

    [Fact]
    public void Numbers_are_written_to_seven_decimals_without_trailing_zeros()
    {
        Assert.Equal(
            """{"type":"Polygon","coordinates":[[[21.0230574,52.2229075],[21.5,52.25],[21,53],[21.0230574,52.2229075]]]}""",
            Outline("POLYGON((21.0230574318539 52.2229074776239,21.50 52.250,21 53,21.0230574318539 52.2229074776239))"));
    }

    [Fact]
    public void Polygon_with_a_hole_keeps_the_hole_as_an_inner_ring()
    {
        Assert.Equal(
            """{"type":"Polygon","coordinates":[[[20,50],[21,50],[21,51],[20,51],[20,50]],[[20.2,50.2],[20.4,50.2],[20.4,50.4],[20.2,50.2]]]}""",
            Outline("POLYGON((20 50,21 50,21 51,20 51,20 50),(20.2 50.2,20.4 50.2,20.4 50.4,20.2 50.2))"));
    }

    [Fact]
    public void Hole_vertices_are_converted_too()
    {
        // Two nested squares in PL-1992 around the first vertex of the fixture parcel.
        var root = Parse(Outline(
            "POLYGON((638000 486000,638100 486000,638100 486100,638000 486000),(638010 486010,638040 486010,638040 486040,638010 486010))",
            srid: 2180));

        var hole = root.GetProperty("coordinates")[1];
        var expected = new Pl1992Point(638010, 486010).ToWgs84();
        Assert.Equal(expected.Longitude, hole[0][0].GetDouble(), 1e-7);
        Assert.Equal(expected.Latitude, hole[0][1].GetDouble(), 1e-7);
    }

    [Fact]
    public void Multipolygon_gives_a_MultiPolygon()
    {
        Assert.Equal(
            """{"type":"MultiPolygon","coordinates":[[[[20,50],[21,50],[21,51],[20,50]]],[[[22,52],[23,52],[23,53],[22,52]],[[22.2,52.2],[22.4,52.2],[22.4,52.4],[22.2,52.2]]]]}""",
            Outline("MULTIPOLYGON(((20 50,21 50,21 51,20 50)),((22 52,23 52,23 53,22 52),(22.2 52.2,22.4 52.2,22.4 52.4,22.2 52.2)))"));
    }

    [Theory]
    [InlineData("POLYGON((20 50,21 50,21 51,20 50))")]
    [InlineData("POLYGON ((20 50, 21 50, 21 51, 20 50))")]
    [InlineData("  polygon  (  ( 20   50 ,21 50,\t21\r\n51 , 20 50 ) )  \n")]
    [InlineData("Polygon((20 50,21 50,21 51,20 50))")]
    [InlineData("POLYGON((2.0E1 +50.0,21.00 50,2.1e+1 5.1e1,20 50))")]
    public void Whitespace_variants_parse(string wkt) => Assert.Equal(Square4326Json, Outline(wkt));

    [Fact]
    public void Whitespace_variants_parse_in_a_multipolygon() =>
        Assert.Equal(
            """{"type":"MultiPolygon","coordinates":[[[[20,50],[21,50],[21,51],[20,50]]],[[[22,52],[23,52],[23,53],[22,52]]]]}""",
            Outline(" MULTIPOLYGON ( ( ( 20 50 , 21 50 , 21 51 , 20 50 ) ) , ( ( 22 52 , 23 52 , 23 53 , 22 52 ) ) ) "));

    [Theory]
    [InlineData("POLYGON EMPTY")]
    [InlineData("MULTIPOLYGON EMPTY")]
    [InlineData("MULTIPOLYGON(EMPTY)")]
    [InlineData("MULTIPOLYGON(((20 50,21 50,21 51,20 50)),EMPTY)")]
    [InlineData("POLYGON Z EMPTY")]
    [InlineData("POLYGON Z ((20 50 1,21 50 1,21 51 1,20 50 1))")]
    [InlineData("POLYGON M ((20 50 1,21 50 1,21 51 1,20 50 1))")]
    [InlineData("POLYGONZ((20 50 1,21 50 1,21 51 1,20 50 1))")]
    [InlineData("POLYGON((20 50 1,21 50 1,21 51 1,20 50 1))")] // a vertex with three numbers is not silently cut to two
    [InlineData("GEOMETRYCOLLECTION(POLYGON((20 50,21 50,21 51,20 50)))")]
    [InlineData("POINT(20 50)")]
    [InlineData("MULTIPOINT((20 50))")]
    [InlineData("LINESTRING(20 50,21 51)")]
    [InlineData("MULTILINESTRING((20 50,21 51))")]
    [InlineData("CURVEPOLYGON((20 50,21 50,21 51,20 50))")]
    public void Empty_z_and_unsupported_types_give_null(string wkt) => Assert.Null(Outline(wkt));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("POLYGON")]
    [InlineData("POLYGON(")]
    [InlineData("POLYGON((")]
    [InlineData("POLYGON(())")]
    [InlineData("POLYGON((20 50,21 50,21 51,20 50)")] // a missing parenthesis
    [InlineData("POLYGON((20 50,21 50,21 51,20 50))x")] // text after the geometry
    [InlineData("POLYGON((20 50,21 50,21 51,20 50)) POLYGON((20 50,21 50,21 51,20 50))")]
    [InlineData("POLYGON((20 50,21 50,21 51))")] // too few vertices
    [InlineData("POLYGON((20 50,21 50,21 51,22 51))")] // ring not closed
    [InlineData("POLYGON((20 50,21 50,21 51,20 50),)")]
    [InlineData("POLYGON((20 50,,21 50,21 51,20 50))")]
    [InlineData("POLYGON((20 abc,21 50,21 51,20 50))")]
    [InlineData("POLYGON((20,50,21,50,21,51,20,50))")] // single numbers instead of pairs
    [InlineData("POLYGON((20 50,21 50,21 51,20 50)))")]
    [InlineData("POLYGON((20-50,21 50,21 51,20 50))")]
    [InlineData("POLYGON((NaN NaN,21 50,21 51,NaN NaN))")]
    [InlineData("POLYGON((Infinity 50,21 50,21 51,Infinity 50))")]
    [InlineData("POLYGON((1e999 50,21 50,21 51,1e999 50))")] // parses to infinity
    [InlineData("SRID=4326;POLYGON((20 50,21 50,21 51,20 50))")] // the prefix is the caller's business
    public void Garbage_gives_null(string wkt) => Assert.Null(Outline(wkt));

    [Theory]
    [InlineData("POLYGON((0 0,100 0,100 100,0 0))", 2180)] // PL-1992 metres outside the grid range of Poland
    [InlineData("POLYGON((638000 486000,638100 486000,638100 9999999,638000 486000))", 2180)] // one bad vertex spoils the outline
    [InlineData("POLYGON((100000 100000,100100 100000,100100 100100,100000 100000))", 2180)] // inside the grid range, outside Poland in WGS-84
    [InlineData("POLYGON((50 20,51 20,51 21,50 20))", 4326)] // latitude first by mistake
    [InlineData("POLYGON((20 50,21 50,21 99,20 50))", 4326)]
    [InlineData("POLYGON((20 50,21 50,21 51,20 50))", 3857)] // an unknown system
    [InlineData("POLYGON((20 50,21 50,21 51,20 50))", 0)]
    public void Vertex_outside_Poland_or_unknown_srid_gives_null(string wkt, int srid) => Assert.Null(Outline(wkt, srid));

    [Fact]
    public void Culture_does_not_matter()
    {
        // A parse or a write that follows the current culture would read "20.5" wrongly or write a decimal comma.
        var geometry = new ParcelGeometry { Srid = 4326, Wkt = "POLYGON((20.5 50.25,21.5 50.25,21.5 51.75,20.5 50.25))" };
        var grid = FromFixture("uldk/parcel-146510_8.0507.41_1.txt");

        foreach (var culture in new[] { "pl-PL", "en-US", "" })
        {
            Assert.Equal(
                """{"type":"Polygon","coordinates":[[[20.5,50.25],[21.5,50.25],[21.5,51.75],[20.5,50.25]]]}""",
                CultureScope.Run(culture, () => ParcelOutline.ToGeoJson(geometry)));
            Assert.Equal(
                CultureScope.Run("", () => ParcelOutline.ToGeoJson(grid)),
                CultureScope.Run(culture, () => ParcelOutline.ToGeoJson(grid)));
        }
    }
}
