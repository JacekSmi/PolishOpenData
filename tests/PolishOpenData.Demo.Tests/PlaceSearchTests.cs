using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Demo.Geo;
using PolishOpenData.Demo.Search;
using PolishOpenData.Geo;
using PolishOpenData.Rcn;
using PolishOpenData.Uldk;

namespace PolishOpenData.Demo.Tests;

public class PlaceSearchTests
{
    private static readonly DateOnly Today = new(2026, 10, 23);

    // The Krucza 50 address point of the UUG fixture; the rows below are placed relative to the centre exactly as the search computes it.
    private static readonly Wgs84Point Centre = new Pl1992Point(637659.46, 486969.16).ToWgs84();
    private static readonly Pl1992Point CentrePl = Centre.ToPl1992();

    private const string Ownership = "wlasnoscLokaluWrazZPrawemZwiazanym";

    private static Pl1992Point At(double dx, double dy) => new(CentrePl.Easting + dx, CentrePl.Northing + dy);

    private static DateOnly Day(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A market-price flat unless a parameter says otherwise.
    private static RcnTransaction Row(
        Pl1992Point at, string date, decimal? price = 600_000m, decimal? area = 50m, string? share = "1/1", string? kind = "wolnyRynek",
        string? right = Ownership, RcnMarket? market = RcnMarket.Secondary, decimal? totalPrice = null, RcnAddress? address = null,
        int? floor = null, int? rooms = null) =>
        new()
        {
            Layer = RcnLayer.Premises,
            Id = "row",
            Date = Day(date),
            Location = at,
            Market = market,
            Kind = kind,
            Share = share,
            Right = right,
            PremisesPrice = price,
            UsableArea = area,
            TotalPrice = totalPrice ?? price,
            Address = address,
            Floor = floor,
            Rooms = rooms,
            Fields = new Dictionary<string, string>(),
        };

    private static (PlaceSearch Search, FakeRcn Rcn) Create(IReadOnlyList<RcnTransaction> rows, bool truncated = false)
    {
        var rcn = new FakeRcn(rows, truncated);
        return (new PlaceSearch(new FakeUldk(null), rcn, () => Today), rcn);
    }

    private static Task<SalesResult> SalesAsync(PlaceSearch search, int radius = 500, int months = 24) =>
        search.FindSalesAsync(Centre, radius, months, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(250, 12)]
    [InlineData(500, 24)]
    [InlineData(1000, 60)]
    [InlineData(2000, 24)]
    public async Task Sales_use_the_residential_premises_query(int radius, int months)
    {
        var (search, rcn) = Create([]);

        var result = await SalesAsync(search, radius, months);

        var query = Assert.Single(rcn.Queries);
        Assert.Equal(RcnLayer.Premises, query.Layer);
        Assert.Equal(RcnPremisesFunction.Residential, query.PremisesFunction);
        Assert.Equal(Today.AddMonths(-months), query.From);
        Assert.Equal(Today, query.To);
        Assert.Equal(1000, query.MaxRecords);
        Assert.Null(query.Market);
        Assert.Null(query.PowiatCode);
        var area = Assert.NotNull(query.Area);
        Assert.Equal(CentrePl.Easting - radius, area.Min.Easting, 6);
        Assert.Equal(CentrePl.Northing - radius, area.Min.Northing, 6);
        Assert.Equal(CentrePl.Easting + radius, area.Max.Easting, 6);
        Assert.Equal(CentrePl.Northing + radius, area.Max.Northing, 6);
        Assert.Equal(Today.AddMonths(-months), result.From);
        Assert.Equal(Today, result.To);
    }

    [Fact]
    public async Task Rows_outside_the_circle_are_dropped()
    {
        // (400, 400) lies inside the square of 500 m but 565 m from the centre; a row exactly on the circle stays (dx2 + dy2 <= r2).
        var (search, _) = Create(
        [
            Row(At(300, 0), "2026-03-01", floor: 1),
            Row(At(400, 400), "2026-04-01", floor: 2),
            Row(At(0, -500), "2026-02-01", floor: 3),
            Row(At(-500, 1), "2026-01-01", floor: 4),
        ]);

        var result = await SalesAsync(search);

        Assert.Equal(SalesOutcome.Ok, result.Outcome);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { 1, 3 }, result.Groups.SelectMany(g => g.Sales).Select(s => s.Floor).Order().Select(f => f!.Value));
        Assert.Equal(Day("2026-03-01"), result.Newest);
    }

    [Fact]
    public async Task Truncated_result_has_no_statistics()
    {
        var (search, _) = Create([Row(At(0, 0), "2026-03-01")], truncated: true);

        var result = await SalesAsync(search);

        Assert.Equal(SalesOutcome.TooManySales, result.Outcome);
        Assert.Null(result.Summary);
        Assert.Null(result.Newest);
        Assert.Empty(result.Groups);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Newest_date_covers_all_rows_in_the_circle()
    {
        // The newest row in the circle is a share (not counted), so the counted sample ends earlier; a newer row beyond the circle does not count at all.
        var (search, _) = Create(
        [
            Row(At(500, 500), "2026-09-30"),
            Row(At(10, 10), "2026-08-15", share: "1/2"),
            Row(At(20, 20), "2026-05-01"),
        ]);

        var result = await SalesAsync(search);

        Assert.Equal(Day("2026-08-15"), result.Newest);
        Assert.Equal(Day("2026-05-01"), result.Summary!.Newest);
    }

    [Fact]
    public async Task Counted_and_excluded_rows_are_flagged()
    {
        var (search, _) = Create(
        [
            Row(At(1, 0), "2026-06-01"),
            Row(At(2, 0), "2026-06-02", price: 800_000m),
            Row(At(3, 0), "2026-06-03", share: "1/2"),
            Row(At(4, 0), "2026-06-04", kind: "licytacja"),
            Row(At(5, 0), "2026-06-05", right: "spoldzielczeWlasnosciowePrawoDoLokalu"),
            Row(At(6, 0), "2026-06-06", price: null),
            Row(At(7, 0), "2026-06-07", area: 0m),
        ]);

        var result = await SalesAsync(search);

        var byDay = result.Groups.SelectMany(g => g.Sales).ToDictionary(s => s.Date);
        Assert.Equal(7, result.TotalCount);
        Assert.Equal(RcnExclusionReason.None, byDay[Day("2026-06-01")].Exclusion);
        Assert.Equal(RcnExclusionReason.None, byDay[Day("2026-06-02")].Exclusion);
        Assert.Equal(RcnExclusionReason.Share, byDay[Day("2026-06-03")].Exclusion);
        Assert.Equal(RcnExclusionReason.NonMarketKind, byDay[Day("2026-06-04")].Exclusion);
        Assert.Equal(RcnExclusionReason.OtherRight, byDay[Day("2026-06-05")].Exclusion);
        Assert.Equal(RcnExclusionReason.MissingPriceOrArea, byDay[Day("2026-06-06")].Exclusion);
        Assert.Equal(RcnExclusionReason.MissingPriceOrArea, byDay[Day("2026-06-07")].Exclusion);
        Assert.All(byDay.Values, s => Assert.Equal(s.Exclusion == RcnExclusionReason.None, s.Counted));
        Assert.Equal(2, byDay.Values.Count(s => s.Counted));
        Assert.Equal(2, result.Summary!.Count);
        Assert.Equal(new RcnExclusions(MissingPriceOrArea: 2, Share: 1, NonMarketKind: 1, OtherRight: 1), result.Summary.Excluded);
    }

    [Fact]
    public async Task Sales_at_one_point_are_grouped()
    {
        var shared = At(30, 40);
        var elsewhere = At(-100, 60);
        var (search, _) = Create(
        [
            Row(shared, "2025-06-01", floor: 1),
            Row(elsewhere, "2026-03-10", floor: 9),
            Row(shared, "2026-01-05", floor: 2),
            Row(shared, "2025-09-01", floor: 3),
        ]);

        var result = await SalesAsync(search);

        // Groups by the newest sale in each, newest first; sales inside a group newest first.
        Assert.Equal(2, result.Groups.Count);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(elsewhere.ToWgs84(), result.Groups[0].Location);
        Assert.Equal(new[] { 9 }, result.Groups[0].Sales.Select(s => s.Floor!.Value));
        Assert.Equal(shared.ToWgs84(), result.Groups[1].Location);
        Assert.Equal(new[] { 2, 3, 1 }, result.Groups[1].Sales.Select(s => s.Floor!.Value));
        Assert.All(result.Groups[1].Sales, s => Assert.Equal(shared.ToWgs84(), s.Location));
    }

    [Fact]
    public async Task Sales_of_one_day_keep_the_library_order()
    {
        var (search, _) = Create(
        [
            Row(At(5, 5), "2026-02-02", floor: 1),
            Row(At(5, 5), "2026-02-02", floor: 2),
            Row(At(5, 5), "2026-02-02", floor: 3),
        ]);

        var result = await SalesAsync(search);

        Assert.Equal(new[] { 1, 2, 3 }, Assert.Single(result.Groups).Sales.Select(s => s.Floor!.Value));
    }

    [Fact]
    public async Task No_rows_is_NoSales()
    {
        foreach (var rows in new IReadOnlyList<RcnTransaction>[] { [], [Row(At(450, 450), "2026-03-01")] })
        {
            var (search, _) = Create(rows);

            var result = await SalesAsync(search);

            Assert.Equal(SalesOutcome.NoSales, result.Outcome);
            Assert.Empty(result.Groups);
            Assert.Equal(0, result.TotalCount);
            Assert.Null(result.Summary);
            Assert.Null(result.Newest);
            Assert.Equal(Today.AddMonths(-24), result.From);
            Assert.Equal(Today, result.To);
        }
    }

    [Fact]
    public async Task A_place_outside_Poland_is_refused()
    {
        var (search, rcn) = Create([Row(At(0, 0), "2026-03-01")]);

        // default(Wgs84Point) skips the validating constructor: 0, 0 is outside Poland.
        var result = await search.FindSalesAsync(default, 500, 24, TestContext.Current.CancellationToken);

        Assert.Equal(SalesOutcome.OutOfRange, result.Outcome);
        Assert.Empty(result.Groups);
        Assert.Null(result.Summary);
        Assert.Empty(rcn.Queries);
    }

    [Fact]
    public void Corners_outside_the_grid_are_OutOfRange()
    {
        // The offered radii (at most 2000 m) can never push a corner off the PL-1992 grid inside Poland's box, so the guard is checked
        // directly: FindSalesAsync answers OutOfRange whenever TryBuildArea says no.
        var corner = new Wgs84Point(48.9, 14.0);

        Assert.True(PlaceSearch.TryBuildArea(corner, 2000, out _, out _));
        Assert.False(PlaceSearch.TryBuildArea(corner, 50_000, out _, out _));
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(100, 24)]
    [InlineData(2500, 24)]
    [InlineData(500, 0)]
    [InlineData(500, 6)]
    [InlineData(500, 36)]
    public async Task A_radius_or_period_that_is_not_offered_is_a_caller_bug(int radius, int months)
    {
        var (search, rcn) = Create([]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => SalesAsync(search, radius, months));

        Assert.Empty(rcn.Queries);
    }

    [Fact]
    public async Task A_row_the_map_cannot_place_is_left_out()
    {
        // Near the southern edge of Poland's WGS-84 box, a row 400 m south is still in the circle but cannot be a map point.
        var edge = new Wgs84Point(48.9, 19.0);
        var edgePl = edge.ToPl1992();
        var south = new Pl1992Point(edgePl.Easting, edgePl.Northing - 400);
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => south.ToWgs84());
        var inside = new Pl1992Point(edgePl.Easting, edgePl.Northing + 100);
        var rcn = new FakeRcn([Row(south, "2026-03-01"), Row(inside, "2026-02-01")], false);
        var search = new PlaceSearch(new FakeUldk(null), rcn, () => Today);

        var result = await search.FindSalesAsync(edge, 500, 24, TestContext.Current.CancellationToken);

        Assert.Equal(SalesOutcome.Ok, result.Outcome);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.Summary!.Count);
        Assert.Equal(Day("2026-02-01"), result.Newest);
    }

    [Fact]
    public async Task Price_is_the_premises_price_not_the_total()
    {
        var (search, _) = Create([Row(At(10, 10), "2026-03-01", price: 750_000m, area: 54.2m, totalPrice: 1_500_000m)]);

        var sale = Assert.Single(Assert.Single((await SalesAsync(search)).Groups).Sales);

        Assert.Equal(750_000m, sale.Price);
        Assert.Equal(54.2m, sale.UsableArea);
        // 750000 / 54.2 = 13837.62...: whole zloty, as property_prices shows it.
        Assert.Equal(13_838m, sale.PricePerSquareMetre);
    }

    [Fact]
    public async Task A_sale_carries_what_RCN_published()
    {
        var (search, _) = Create(
        [
            Row(At(1, 1), "2026-03-03", market: RcnMarket.Primary, address: new RcnAddress("Warszawa", "ulica Krucza", "50"), floor: 3, rooms: 2),
            Row(At(2, 2), "2026-03-02", market: null, address: new RcnAddress(null, null, null)),
            Row(At(3, 3), "2026-03-01", address: null),
        ]);

        var sales = (await SalesAsync(search)).Groups.SelectMany(g => g.Sales).ToList();

        Assert.Equal("primary", sales[0].Market);
        Assert.Equal("ulica Krucza 50, Warszawa", sales[0].Address);
        Assert.Equal(3, sales[0].Floor);
        Assert.Equal(2, sales[0].Rooms);
        Assert.Null(sales[1].Market);
        Assert.Null(sales[1].Address);
        Assert.Equal("secondary", sales[2].Market);
        Assert.Null(sales[2].Address);
    }

    [Fact]
    public async Task Parcel_outline_comes_from_the_pl1992_geometry()
    {
        var geometry = new ParcelGeometry { Srid = 2180, Wkt = "POLYGON((637700 486900,637720 486900,637720 486920,637700 486920,637700 486900))" };
        var parcel = new UldkParcel { Number = "12/3", Geometry = geometry };
        var uldk = new FakeUldk(parcel);
        var search = new PlaceSearch(uldk, new FakeRcn([], false), () => Today);

        var result = await search.FindParcelAsync(Centre, TestContext.Current.CancellationToken);

        var call = Assert.Single(uldk.Calls);
        Assert.Equal((Centre, ParcelGeometryKind.Pl1992), call);
        Assert.Same(parcel, result.Parcel);
        Assert.NotNull(result.OutlineGeoJson);
        Assert.Equal(ParcelOutline.ToGeoJson(geometry), result.OutlineGeoJson);
        Assert.StartsWith("{\"type\":\"Polygon\"", result.OutlineGeoJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_parcel_gives_an_empty_result()
    {
        var uldk = new FakeUldk(null);
        var search = new PlaceSearch(uldk, new FakeRcn([], false), () => Today);

        var result = await search.FindParcelAsync(Centre, TestContext.Current.CancellationToken);

        Assert.Single(uldk.Calls);
        Assert.Equal(new ParcelResult(null, null), result);
    }

    [Theory]
    [InlineData("POLYGON EMPTY")]
    [InlineData("not wkt")]
    [InlineData(null)]
    public async Task A_parcel_without_a_usable_outline_is_still_returned(string? wkt)
    {
        var parcel = new UldkParcel { Number = "12/3", Geometry = wkt is null ? null : new ParcelGeometry { Srid = 2180, Wkt = wkt } };
        var search = new PlaceSearch(new FakeUldk(parcel), new FakeRcn([], false), () => Today);

        var result = await search.FindParcelAsync(Centre, TestContext.Current.CancellationToken);

        Assert.Same(parcel, result.Parcel);
        Assert.Null(result.OutlineGeoJson);
    }

    private sealed class FakeRcn(IReadOnlyList<RcnTransaction> items, bool truncated) : IRcnClient
    {
        public List<RcnQuery> Queries { get; } = [];

        public Task<RcnTransactions> GetTransactionsAsync(RcnQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new RcnTransactions(items, truncated));
        }
    }

    private sealed class FakeUldk(UldkParcel? parcel) : IUldkClient
    {
        public List<(Wgs84Point Point, ParcelGeometryKind Geometry)> Calls { get; } = [];

        public Task<UldkParcel?> GetParcelAtAsync(Wgs84Point point, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default)
        {
            Calls.Add((point, geometry));
            return Task.FromResult(parcel);
        }

        public Task<UldkParcel?> GetParcelByIdAsync(ParcelId id, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UldkParcel?> GetParcelAtAsync(Pl1992Point point, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<UldkParcel>> FindParcelsAsync(string regionAndNumber, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
