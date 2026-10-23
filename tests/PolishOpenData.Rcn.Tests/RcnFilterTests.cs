using System;
using PolishOpenData.Geo;

namespace PolishOpenData.Rcn.Tests;

public class RcnFilterTests
{
    private const string Open = "<fes:Filter xmlns:fes=\"http://www.opengis.net/fes/2.0\" xmlns:gml=\"http://www.opengis.net/gml/3.2\">";

    private static Pl1992Bbox Bbox(double width = 300, double height = 300) =>
        new(new Pl1992Point(637800, 486300), new Pl1992Point(637800 + width, 486300 + height));

    [Fact]
    public void Filter_bbox_is_written_northing_first()
    {
        var filter = RcnFilter.Build(new RcnQuery { Area = Bbox() });

        Assert.Contains("<gml:lowerCorner>486300 637800</gml:lowerCorner><gml:upperCorner>486600 638100</gml:upperCorner>", filter);
        Assert.Contains("srsName=\"urn:ogc:def:crs:EPSG::2180\"", filter);
        Assert.Contains("<fes:ValueReference>GEOMETRIA</fes:ValueReference>", filter);
    }

    [Fact]
    public void Filter_with_one_condition_has_no_And()
    {
        var filter = RcnFilter.Build(new RcnQuery { PowiatCode = "1465" });

        Assert.Equal(
            Open + "<fes:PropertyIsEqualTo><fes:ValueReference>TERYT</fes:ValueReference><fes:Literal>1465</fes:Literal></fes:PropertyIsEqualTo></fes:Filter>",
            filter);
    }

    [Fact]
    public void Filter_combines_all_conditions_in_a_fixed_order()
    {
        var filter = RcnFilter.Build(new RcnQuery
        {
            Area = Bbox(),
            PowiatCode = "1465",
            From = new DateOnly(2025, 6, 1),
            To = new DateOnly(2025, 12, 31),
            Market = RcnMarket.Secondary,
            PremisesFunction = RcnPremisesFunction.Residential,
        });

        Assert.Equal(
            Open + "<fes:And>"
            + "<fes:BBOX><fes:ValueReference>GEOMETRIA</fes:ValueReference><gml:Envelope srsName=\"urn:ogc:def:crs:EPSG::2180\"><gml:lowerCorner>486300 637800</gml:lowerCorner><gml:upperCorner>486600 638100</gml:upperCorner></gml:Envelope></fes:BBOX>"
            + "<fes:PropertyIsEqualTo><fes:ValueReference>TERYT</fes:ValueReference><fes:Literal>1465</fes:Literal></fes:PropertyIsEqualTo>"
            + "<fes:PropertyIsGreaterThanOrEqualTo><fes:ValueReference>DOK_DATA</fes:ValueReference><fes:Literal>2025-06-01</fes:Literal></fes:PropertyIsGreaterThanOrEqualTo>"
            + "<fes:PropertyIsLessThanOrEqualTo><fes:ValueReference>DOK_DATA</fes:ValueReference><fes:Literal>2025-12-31</fes:Literal></fes:PropertyIsLessThanOrEqualTo>"
            + "<fes:PropertyIsEqualTo><fes:ValueReference>TRAN_RODZAJ_RYNKU</fes:ValueReference><fes:Literal>rynekWtorny</fes:Literal></fes:PropertyIsEqualTo>"
            + "<fes:PropertyIsEqualTo><fes:ValueReference>LOK_FUNKCJA</fes:ValueReference><fes:Literal>mieszkalna</fes:Literal></fes:PropertyIsEqualTo>"
            + "</fes:And></fes:Filter>",
            filter);
    }

    [Fact]
    public void Filter_writes_the_primary_market_code()
    {
        var filter = RcnFilter.Build(new RcnQuery { PowiatCode = "1465", Market = RcnMarket.Primary });

        Assert.Contains("<fes:Literal>rynekPierwotny</fes:Literal>", filter);
    }

    [Theory]
    [InlineData(RcnPremisesFunction.Residential, "mieszkalna")]
    [InlineData(RcnPremisesFunction.Garage, "garaz")]
    [InlineData(RcnPremisesFunction.Commercial, "handlowoUslugowa")]
    [InlineData(RcnPremisesFunction.Other, "inna")]
    public void Filter_adds_premises_function(RcnPremisesFunction function, string code)
    {
        var filter = RcnFilter.Build(new RcnQuery { PowiatCode = "1465", PremisesFunction = function });

        Assert.Contains($"<fes:ValueReference>LOK_FUNKCJA</fes:ValueReference><fes:Literal>{code}</fes:Literal>", filter);
    }

    [Theory]
    [InlineData(RcnLayer.Premises, "ms:lokale")]
    [InlineData(RcnLayer.Parcels, "ms:dzialki")]
    [InlineData(RcnLayer.Buildings, "ms:budynki")]
    public void Type_names_per_layer(RcnLayer layer, string name) =>
        Assert.Equal(name, RcnFilter.TypeName(layer));

    [Fact]
    public void Sort_is_newest_first_then_gid() =>
        Assert.Equal("DOK_DATA DESC,gid", RcnFilter.SortBy);

    [Fact]
    public void Query_defaults_are_premises_and_one_thousand_rows()
    {
        var query = new RcnQuery();

        Assert.Equal(RcnLayer.Premises, query.Layer);
        Assert.Equal(1000, query.MaxRecords);
    }

    [Theory]
    [InlineData("146")]
    [InlineData("14650")]
    [InlineData("abcd")]
    [InlineData(" 1465")]
    public void Validate_rejects_a_powiat_code_that_is_not_four_digits(string code) =>
        Assert.Throws<ArgumentException>(() => new RcnQuery { PowiatCode = code }.Validate());

    [Fact]
    public void Validate_rejects_neither_area_nor_powiat() =>
        Assert.Throws<ArgumentException>(() => new RcnQuery().Validate());

    [Fact]
    public void Validate_rejects_an_area_above_25_square_kilometres() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RcnQuery { Area = Bbox(5_100, 5_000) }.Validate());

    [Fact]
    public void Validate_rejects_from_after_to() =>
        Assert.Throws<ArgumentException>(() => new RcnQuery
        {
            PowiatCode = "1465",
            From = new DateOnly(2025, 12, 31),
            To = new DateOnly(2025, 6, 1),
        }.Validate());

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public void Validate_rejects_max_records_out_of_range(int maxRecords) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RcnQuery { PowiatCode = "1465", MaxRecords = maxRecords }.Validate());

    [Fact]
    public void Validate_rejects_premises_function_on_another_layer() =>
        Assert.Throws<ArgumentException>(() => new RcnQuery
        {
            PowiatCode = "1465",
            Layer = RcnLayer.Parcels,
            PremisesFunction = RcnPremisesFunction.Residential,
        }.Validate());

    [Fact]
    public void Validate_accepts_an_area_of_exactly_25_square_kilometres() =>
        new RcnQuery { Area = Bbox(5_000, 5_000) }.Validate();

    [Theory]
    [InlineData(523989.43796825287, 486000.5)]   // found by search: width 5000.000000000058 m
    [InlineData(261906.02118844105, 486000.5)]   // width 5000.000000000029 m
    [InlineData(500000.5, 522893.90518668847)]
    [InlineData(524288, 486000)]   // the exact boundary: always fine
    public void Validate_accepts_a_5_km_square_around_a_centre_where_floating_point_error_widens_it(double easting, double northing)
    {
        // Corners in different binades (below and above 2^18 / 2^19) make the width 5000.00000000006 m.
        var area = new Pl1992Bbox(new Pl1992Point(easting - 2500, northing - 2500), new Pl1992Point(easting + 2500, northing + 2500));

        new RcnQuery { Area = area }.Validate();
    }

    [Fact]
    public void Validate_still_rejects_5100_by_5000_metres_near_a_binade_boundary() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RcnQuery
        {
            Area = new Pl1992Bbox(new Pl1992Point(524288 - 2550, 486000 - 2500), new Pl1992Point(524288 + 2550, 486000 + 2500)),
        }.Validate());

    [Fact]
    public void Validate_accepts_a_powiat_only_query_with_no_dates() =>
        new RcnQuery { PowiatCode = "1465" }.Validate();
}
