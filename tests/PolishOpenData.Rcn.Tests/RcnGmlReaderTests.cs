using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using PolishOpenData.Geo;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Rcn.Tests;

public class RcnGmlReaderTests
{
    private static HttpResponseMessage Response(HttpStatusCode status = HttpStatusCode.OK) => new(status);

    private static IReadOnlyList<RcnTransaction> Read(string fixture, RcnLayer layer = RcnLayer.Premises, HttpStatusCode status = HttpStatusCode.OK) =>
        RcnGmlReader.Read(Fixture.ReadBytes("rcn/" + fixture), layer, Response(status));

    [Fact]
    public void Premises_row_maps_the_typed_fields()
    {
        var rows = Read("premises-warsaw-centre.xml");

        Assert.Equal(3, rows.Count);
        var t = rows[0];
        Assert.Equal(RcnLayer.Premises, t.Layer);
        Assert.Equal("00000000-0000-4000-8000-000000000001", t.Id);
        Assert.Equal(new DateOnly(2025, 12, 19), t.Date);
        Assert.Equal(RcnMarket.Secondary, t.Market);
        Assert.Equal("wolnyRynek", t.Kind);
        Assert.Equal("osobaFizyczna", t.SellerType);
        Assert.Equal("osobaFizyczna", t.BuyerType);
        Assert.Equal(750000m, t.TotalPrice);
        Assert.Equal("11038/2025", t.DocumentNumber);
        Assert.Equal("wlasnoscLokaluWrazZPrawemZwiazanym", t.Right);
        Assert.Equal("1/1", t.Share);
        Assert.Equal(new Pl1992Point(637803.204077, 486387.107712), t.Location);
        Assert.Equal(new RcnAddress("Testowo", "ulica Przykładowa", "11"), t.Address);
        Assert.Equal("ulica Przykładowa 11, Testowo", t.Address!.ToString());
        Assert.Equal(RcnPremisesFunction.Residential, t.PremisesFunction);
        Assert.Equal(2, t.Rooms);
        Assert.Equal(3, t.Floor);
        Assert.Equal(36m, t.UsableArea);
        Assert.Equal(750000m, t.PremisesPrice);
        Assert.Equal(750000m / 36m, t.PricePerSquareMetre);
        Assert.Null(t.ParcelId);
        Assert.Null(t.BuildingId);
        Assert.Equal("wlasnoscLokaluWrazZPrawemZwiazanym", t.Fields["NIER_PRAWO"]);
        Assert.Equal("NOTARY-REDACTED", t.Fields["DOK_TWORCA"]);
        Assert.Equal("146510_8.0504.40_BUD.40_LOK", t.Fields["LOK_ID_LOKALU"]);
        Assert.DoesNotContain("GEOMETRIA", t.Fields.Keys);
        Assert.DoesNotContain("SERWIS_RCN", t.Fields.Keys); // empty
        Assert.All(t.Fields.Keys, k => Assert.Matches("^[A-Za-z_]+$", k)); // no gml: or boundedBy keys
        Assert.Equal("2026-04-10T10:59:16Z", t.Fields["TRAN_WERSJA_ID"]);
    }

    [Fact]
    public void Price_per_m2_uses_the_premises_price_not_the_total()
    {
        var rows = Read("synthetic-two-premises.xml");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(900000m, r.TotalPrice));
        Assert.Equal(8000m, rows[0].PricePerSquareMetre);
        Assert.Equal(8000m, rows[1].PricePerSquareMetre);
        Assert.Equal(rows[0].Id, rows[1].Id);
    }

    [Fact]
    public void Missing_or_zero_price_or_area_gives_null_price_per_m2()
    {
        var rows = Read("synthetic-edge-values.xml");

        Assert.Null(rows[0].PremisesPrice);
        Assert.Null(rows[0].PricePerSquareMetre);
        Assert.Equal(0m, rows[1].UsableArea);
        Assert.Null(rows[1].PricePerSquareMetre);
        Assert.NotNull(rows[2].PricePerSquareMetre);
    }

    [Fact]
    public void Share_is_null_when_empty_and_kept_when_published()
    {
        var rows = Read("synthetic-edge-values.xml");

        Assert.Null(rows[0].Share);
        Assert.Equal("1/2", rows[2].Share);
    }

    [Fact]
    public void Address_without_number_keeps_town_and_street()
    {
        var address = Read("synthetic-edge-values.xml")[3].Address;

        Assert.Equal(new RcnAddress("Testowo", "ulica Przykładowa", null), address);
        Assert.Equal("ulica Przykładowa, Testowo", address!.ToString());
    }

    [Fact]
    public void Unparsable_address_is_null_but_kept_in_fields()
    {
        var row = Read("synthetic-edge-values.xml")[4];

        Assert.Null(row.Address);
        Assert.Equal("foo", row.Fields["LOK_ADRES"]);
    }

    [Fact]
    public void Unknown_fields_land_in_Fields()
    {
        var row = Read("synthetic-edge-values.xml")[5];

        Assert.Equal("nowa wartość", row.Fields["ZZZ_NEW"]);
    }

    [Fact]
    public void Parcel_row_has_parcel_id_land_area_and_envelope_centre_location()
    {
        var t = Read("parcels-warsaw.xml", RcnLayer.Parcels)[0];

        Assert.Equal(RcnLayer.Parcels, t.Layer);
        Assert.Equal(ParcelId.Parse("146516_8.0659.3/7"), t.ParcelId);
        Assert.Equal(1907m, t.LandArea);
        Assert.Equal(1614230m, t.ParcelPrice);
        Assert.Equal(1680000m, t.TotalPrice);
        Assert.Null(t.Address); // DZI_ADRES is empty in this row
        Assert.Null(t.Market); // TRAN_RODZAJ_RYNKU is empty in this row
        Assert.Null(t.PricePerSquareMetre);
        Assert.Equal((645161.967647 + 645210.726345) / 2, t.Location.Easting, 6);
        Assert.Equal((477533.419603 + 477596.339217) / 2, t.Location.Northing, 6);
    }

    [Fact]
    public void Building_row_has_building_fields()
    {
        var t = Read("buildings-warsaw.xml", RcnLayer.Buildings)[0];

        Assert.Equal(RcnLayer.Buildings, t.Layer);
        Assert.Equal("146507_8.0112.323_BUD", t.BuildingId);
        Assert.Equal("mieszkalny", t.BuildingKind);
        Assert.Null(t.BuildingUsableArea);
        Assert.Null(t.BuildingPrice);
        Assert.Equal("Testowo", t.Address!.Town);
    }

    [Fact]
    public void Empty_collection_gives_no_rows() => Assert.Empty(Read("empty.xml"));

    [Fact]
    public void Exception_report_becomes_an_api_exception()
    {
        var ex = Assert.Throws<PolishOpenDataApiException>(() => Read("exception-unknown-property.xml", status: HttpStatusCode.BadRequest));

        Assert.Equal("InvalidParameterValue", ex.ErrorCode);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("Property 'NOPE' is unknown", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ExceptionReport", ex.ResponseSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReadException_returns_null_for_a_feature_collection_and_for_non_xml()
    {
        using var response = Response();

        Assert.Null(RcnGmlReader.TryReadException(Fixture.ReadBytes("rcn/empty.xml"), response));
        Assert.Null(RcnGmlReader.TryReadException(Encoding.UTF8.GetBytes("not xml"), response));
        Assert.NotNull(RcnGmlReader.TryReadException(Fixture.ReadBytes("rcn/exception-unknown-property.xml"), response));
    }

    [Fact]
    public void Unreadable_date_throws_instead_of_dropping_the_row()
    {
        var ex = Assert.Throws<PolishOpenDataApiException>(() => Read("synthetic-bad-date.xml"));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Contains("DOK_DATA", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.ResponseSnippet);
    }

    [Fact]
    public void Not_xml_throws_unreadable()
    {
        var ex = Assert.Throws<PolishOpenDataApiException>(() => Read("block-page.html"));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.StartsWith("<", ex.ResponseSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Dtd_is_rejected()
    {
        var ex = Assert.Throws<PolishOpenDataApiException>(() => Read("synthetic-dtd.xml"));

        Assert.IsAssignableFrom<System.Xml.XmlException>(ex.InnerException);
    }
}
