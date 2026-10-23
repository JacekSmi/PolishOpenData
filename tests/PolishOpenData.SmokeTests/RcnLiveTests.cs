using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Geo;
using PolishOpenData.Rcn;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// The RCN transaction-price client and the <c>property_prices</c> tool against the live GUGiK RCN service (gated and budgeted by <see cref="Live"/>;
/// no key). The service publishes no request limit, so each test sends a single bounded query.
/// </summary>
public sealed class RcnLiveTests
{
    // About 300 m square in central Warsaw (Śródmieście), where premises sales are dense.
    private static readonly Pl1992Bbox Warsaw = new(new Pl1992Point(637800, 486300), new Pl1992Point(638100, 486600));

    // 1 km square around it: the 300 m box held only 20 residential sales in 2025, too few for a median that means something.
    private static readonly Pl1992Bbox WarsawWide = new(new Pl1992Point(637500, 486000), new Pl1992Point(638500, 487000));

    private static RcnQuery WarsawQuery(int maxRecords, Pl1992Bbox? area = null) => new()
    {
        Area = area ?? Warsaw,
        From = new DateOnly(2025, 1, 1),
        To = new DateOnly(2025, 12, 31),
        MaxRecords = maxRecords,
    };

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Rcn_premises_in_the_warsaw_bbox_are_inside_it()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new RcnClient(http);

        var result = await client.GetTransactionsAsync(WarsawQuery(200), TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, t => Assert.Equal(RcnLayer.Premises, t.Layer));
        // Pins the northing-first FES envelope against the real service: with the axes swapped the box is far away or empty.
        Assert.All(result.Items, t =>
        {
            Assert.InRange(t.Location.Easting, Warsaw.Min.Easting, Warsaw.Max.Easting);
            Assert.InRange(t.Location.Northing, Warsaw.Min.Northing, Warsaw.Max.Northing);
        });
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Rcn_residential_median_price_per_m2_in_warsaw_is_plausible()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new RcnClient(http);
        var query = WarsawQuery(1000, WarsawWide);
        query.PremisesFunction = RcnPremisesFunction.Residential;

        var result = await client.GetTransactionsAsync(query, TestContext.Current.CancellationToken);

        // Summarize refuses a truncated answer; if this fails, lower the bbox.
        Assert.False(result.Truncated, "More than 1,000 rows in the bbox: lower it.");
        var summary = result.Summarize();
        TestContext.Current.TestOutputHelper?.WriteLine($"Rows {result.Items.Count}, sample {summary.Count}, median {summary.Median} PLN/m2, Q1 {summary.Q1}, Q3 {summary.Q3}, newest {summary.Newest}");
        Assert.True(summary.Count > 20, "Sample size " + summary.Count);
        Assert.InRange(summary.Median!.Value, 5_000m, 100_000m);
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Rcn_parcels_and_buildings_layers_answer()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new RcnClient(http);
        var ct = TestContext.Current.CancellationToken;

        var parcels = await client.GetTransactionsAsync(PowiatQuery(RcnLayer.Parcels), ct);
        var buildings = await client.GetTransactionsAsync(PowiatQuery(RcnLayer.Buildings), ct);

        Assert.NotEmpty(parcels.Items);
        Assert.All(parcels.Items, t => Assert.NotNull(t.ParcelId));
        Assert.NotEmpty(buildings.Items);
        Assert.All(buildings.Items, t => Assert.False(string.IsNullOrEmpty(t.BuildingId)));
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Mcp_property_prices_by_address_answers()
    {
        Live.SkipUnlessEnabled();
        var ct = TestContext.Current.CancellationToken;
        await using var session = await McpTestSession.StartAsync(http => http.ConfigureHttpClient(c => c.Timeout = Live.HttpTimeout), clock: null, ct);

        var result = await session.Client.CallToolAsync(
            "property_prices",
            new Dictionary<string, object?>
            {
                ["address"] = "Warszawa, Krucza 50",
                ["radiusMeters"] = 300,
                ["dateFrom"] = "2025-01-01",
                ["dateTo"] = "2025-12-31",
            },
            cancellationToken: ct);

        Live.SkipIfQuotaError(result);
        Assert.True(result.IsError != true, Live.Text(result));
        var data = result.StructuredContent!.Value;
        Assert.True(data.TryGetProperty("statistics", out var statistics) && statistics.ValueKind == JsonValueKind.Object, "No statistics in: " + Live.Text(result));
        TestContext.Current.TestOutputHelper?.WriteLine("transactionCount " + data.GetProperty("transactionCount") + ", statistics " + statistics.GetRawText());
    }

    // Powiat 1465 is Warsaw; a few rows of the first quarter of 2026 are enough to see the layer's identifiers.
    private static RcnQuery PowiatQuery(RcnLayer layer) => new()
    {
        PowiatCode = "1465",
        Layer = layer,
        From = new DateOnly(2026, 1, 1),
        To = new DateOnly(2026, 3, 31),
        MaxRecords = 3,
    };
}
