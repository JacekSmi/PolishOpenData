using System;
using System.Threading.Tasks;
using PolishOpenData.Geo;
using PolishOpenData.Uldk;
using PolishOpenData.Uug;

namespace PolishOpenData.SmokeTests;

/// <summary>The ULDK parcel client and the UUG geocoder against the live GUGiK services (gated and budgeted by <see cref="Live"/>).</summary>
public sealed class GeoLiveTests
{
    // A parcel in Warsaw (Śródmieście), the same one the ULDK fixtures use.
    private static readonly ParcelId Parcel = ParcelId.Parse("146510_8.0507.41/1");

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Uldk_finds_the_parcel_by_id_and_by_its_extent_center()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new UldkClient(http);

        var parcel = await client.GetParcelByIdAsync(Parcel, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(parcel);
        Assert.Equal(Parcel, parcel.Id);
        // Pins the EPSG:2180 wire order (easting, northing) against the real service.
        var atCenter = await client.GetParcelAtAsync(parcel.Extent.Center, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(atCenter);
        Assert.Equal(Parcel, atCenter.Id);
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Uldk_wgs84_point_finds_the_same_parcel()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new UldkClient(http);

        var parcel = await client.GetParcelByIdAsync(Parcel, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(parcel);
        var atPoint = await client.GetParcelAtAsync(parcel.Extent.Center.ToWgs84(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(atPoint);
        Assert.Equal(Parcel, atPoint.Id);
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Uug_geocodes_krucza_50_inside_warsaw_and_reverse_finds_an_address_nearby()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var client = new UugClient(http);

        var matches = await client.GeocodeAsync("Warszawa, Krucza 50", TestContext.Current.CancellationToken);

        var match = Assert.Single(matches);
        Assert.Equal(UugMatchKind.Address, match.Kind);
        Assert.InRange(match.Location.Latitude, 52.1, 52.4);
        Assert.InRange(match.Location.Longitude, 20.8, 21.3);

        var reverse = await client.ReverseGeocodeAsync(match.Location, TestContext.Current.CancellationToken);

        Assert.NotNull(reverse);
        Assert.Equal(UugMatchKind.Address, reverse.Kind);
        Assert.True(reverse.DistanceMeters < 50, "Reverse geocoding found an address " + reverse.DistanceMeters + " m away.");
    }

    [Fact(Timeout = Live.TestTimeout)]
    public async Task Uldk_and_uug_agree_on_an_address()
    {
        Live.SkipUnlessEnabled();
        using var http = Live.CreateHttpClient();
        var uug = new UugClient(http);
        var uldk = new UldkClient(http);

        var match = Assert.Single(await uug.GeocodeAsync("Warszawa, Krucza 50", TestContext.Current.CancellationToken));
        var parcel = await uldk.GetParcelAtAsync(match.Location, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(parcel);
        Assert.StartsWith("1465", parcel.Id.ToString(), StringComparison.Ordinal);
    }
}
