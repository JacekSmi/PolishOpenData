using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Uug;

namespace PolishOpenData.Mcp;

/// <summary>What the geocoder made of an address: the single address point when it resolved to exactly one, and all matches.</summary>
internal sealed record AddressLookup(UugMatch? Point, IReadOnlyList<UugMatch> Matches);

/// <summary>Geocodes an address and decides whether it names one address point (shared by find_parcel and property_prices).</summary>
internal static class AddressResolution
{
    public const string UnresolvedNote = "The address did not resolve to a single address point; pick one of the candidates or give the full address with a house number.";

    private const string UugName = "GUGiK UUG (Usługa Geokodowania)";
    private const string UugEndpoint = "https://services.gugik.gov.pl/uug/";

    public static async Task<AddressLookup> ResolveAsync(IUugClient uug, string address, CancellationToken cancellationToken)
    {
        var matches = await uug.GeocodeAsync(address, cancellationToken).ConfigureAwait(false);

        // The same address may come back more than once (e.g. one point per entrance record); one distinct address is enough.
        var points = matches
            .Where(m => m.Kind == UugMatchKind.Address)
            .DistinctBy(m => (m.City, m.CityPart, m.Street, m.Number, m.PostalCode, m.Teryt, m.Simc, m.Ulic))
            .ToList();
        return new AddressLookup(points.Count == 1 ? points[0] : null, matches);
    }

    public static GeocodeMatch ToMatch(UugMatch match) => new()
    {
        Kind = match.Kind switch
        {
            UugMatchKind.Address => "address",
            UugMatchKind.Street => "street",
            UugMatchKind.City => "city",
            _ => "other",
        },
        City = match.City,
        Street = match.Street,
        Number = match.Number,
        PostalCode = match.PostalCode,
        Teryt = match.Teryt,
        Latitude = match.Location.Latitude,
        Longitude = match.Location.Longitude,
        Accuracy = match.Accuracy,
    };

    public static SourceInfo UugSource(TimeProvider timeProvider) => new(UugName, UugEndpoint, timeProvider.GetUtcNow(), null);
}
