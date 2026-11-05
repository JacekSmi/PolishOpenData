using PolishOpenData.Demo.Geo;
using PolishOpenData.Demo.State;
using PolishOpenData.Geo;
using PolishOpenData.Rcn;
using PolishOpenData.Uldk;

namespace PolishOpenData.Demo.Search;

/// <summary>What the page asks GUGiK for once a place is picked: the parcel there (ULDK) and the flat sales around it (RCN).</summary>
internal sealed class PlaceSearch(IUldkClient uldk, IRcnClient rcn, Func<DateOnly> today)
{
    private const int MaxRecords = 1000;

    /// <summary>
    /// The parcel at the point with its outline: one ULDK request, asking for the PL-1992 geometry (the WGS-84 one costs a second request).
    /// An outline that cannot be built is <c>null</c>, the parcel is still returned; no parcel gives <c>ParcelResult(null, null)</c>.
    /// </summary>
    public async Task<ParcelResult> FindParcelAsync(Wgs84Point point, CancellationToken cancellationToken)
    {
        var parcel = await uldk.GetParcelAtAsync(point, ParcelGeometryKind.Pl1992, cancellationToken);
        return new ParcelResult(parcel, parcel?.Geometry is { } geometry ? ParcelOutline.ToGeoJson(geometry) : null);
    }

    /// <summary>
    /// The flat sales within <paramref name="radiusMeters"/> of the point over the last <paramref name="periodMonths"/> months: one RCN request, then the circle trim and the statistics.
    /// Mirrors the <c>property_prices</c> tool: keep it in step with <c>src/PolishOpenData.Mcp/Tools/PropertyTools.cs</c> (the coordinates branch: the Poland guard, the square of
    /// centre plus or minus the radius, the residential premises query, the truncation refusal, the circle trim dx2 + dy2 &lt;= r2 in PL-1992 metres,
    /// statistics only over the trimmed rows, the newest date of all trimmed rows, the whole-zloty price per square metre); a change in one is checked in the other.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not one of <see cref="PlaceUrlState.Radii"/> or the period not one of <see cref="PlaceUrlState.Months"/> (a caller bug, not user input).</exception>
    public async Task<SalesResult> FindSalesAsync(Wgs84Point point, int radiusMeters, int periodMonths, CancellationToken cancellationToken)
    {
        if (!PlaceUrlState.Radii.Contains(radiusMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), radiusMeters, "The radius is not one the page offers.");
        }

        if (!PlaceUrlState.Months.Contains(periodMonths))
        {
            throw new ArgumentOutOfRangeException(nameof(periodMonths), periodMonths, "The period is not one the page offers.");
        }

        var to = today();
        var from = to.AddMonths(-periodMonths);
        SalesResult Empty(SalesOutcome outcome) => new(outcome, [], 0, null, null, from, to);

        if (!TryBuildArea(point, radiusMeters, out var centre, out var area))
        {
            return Empty(SalesOutcome.OutOfRange);
        }

        var found = await rcn.GetTransactionsAsync(
            new RcnQuery { Area = area, From = from, To = to, Layer = RcnLayer.Premises, PremisesFunction = RcnPremisesFunction.Residential, MaxRecords = MaxRecords },
            cancellationToken);
        if (found.Truncated)
        {
            return Empty(SalesOutcome.TooManySales);
        }

        // The service answers a square; keep what lies within the radius of the centre (metres in PL-1992).
        // A row the map cannot place (it converts to a point outside the WGS-84 box of Poland, possible only at the very edge) is left out of
        // the count and the statistics too, so the dots and the numbers agree; the MCP tool lists such a row without a position instead.
        var rows = new List<(RcnTransaction Row, Wgs84Point Location)>();
        foreach (var row in found.Items)
        {
            var dx = row.Location.Easting - centre.Easting;
            var dy = row.Location.Northing - centre.Northing;
            if ((dx * dx) + (dy * dy) <= (double)radiusMeters * radiusMeters && TryToWgs84(row.Location, out var location))
            {
                rows.Add((row, location));
            }
        }

        if (rows.Count == 0)
        {
            return Empty(SalesOutcome.NoSales);
        }

        var summary = new RcnTransactions([.. rows.Select(r => r.Row)], false).Summarize();

        // Sales that share a point go in one group, newest first (ties keep the order RCN gave); the groups are ordered by their newest sale.
        var groups = rows
            .GroupBy(r => r.Row.Location)
            .Select(g => new SaleGroup(g.First().Location, [.. g.OrderByDescending(r => r.Row.Date).Select(r => ToView(r.Row, r.Location))]))
            .OrderByDescending(g => g.Sales[0].Date)
            .ToList();
        return new SalesResult(SalesOutcome.Ok, groups, rows.Count, summary, rows.Max(r => r.Row.Date), from, to);
    }

    /// <summary>
    /// The PL-1992 centre of the point and the square of centre plus or minus the radius, or <c>false</c> when the point is not in Poland or a corner
    /// leaves the grid. Inside Poland's box and with the radii the page offers a corner never leaves the grid; the guard mirrors <c>property_prices</c>.
    /// </summary>
    internal static bool TryBuildArea(Wgs84Point point, int radiusMeters, out Pl1992Point centre, out Pl1992Bbox area)
    {
        centre = default;
        area = default;
        if (!Wgs84Point.IsInPoland(point.Latitude, point.Longitude))
        {
            return false;
        }

        try
        {
            centre = point.ToPl1992();
            area = new Pl1992Bbox(
                new Pl1992Point(centre.Easting - radiusMeters, centre.Northing - radiusMeters),
                new Pl1992Point(centre.Easting + radiusMeters, centre.Northing + radiusMeters));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryToWgs84(Pl1992Point point, out Wgs84Point wgs84)
    {
        try
        {
            wgs84 = point.ToWgs84();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            wgs84 = default;
            return false;
        }
    }

    private static SaleView ToView(RcnTransaction row, Wgs84Point location)
    {
        var exclusion = RcnPriceStatistics.Classify(row);
        return new SaleView(
            location,
            row.Date,
            row.Market switch { RcnMarket.Primary => "primary", RcnMarket.Secondary => "secondary", _ => null },
            row.Address?.ToString() is { Length: > 0 } address ? address : null,
            row.Floor,
            row.Rooms,
            row.UsableArea,
            row.PremisesPrice,
            row.PricePerSquareMetre is { } perSquareMetre ? decimal.Round(perSquareMetre, 0, MidpointRounding.AwayFromZero) : null,
            exclusion == RcnExclusionReason.None,
            exclusion);
    }
}
