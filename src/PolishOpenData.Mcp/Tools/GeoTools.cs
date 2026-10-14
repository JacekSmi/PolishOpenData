using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PolishOpenData.Geo;
using PolishOpenData.Uldk;
using PolishOpenData.Uug;

namespace PolishOpenData.Mcp;

[McpServerToolType]
internal sealed class GeoTools(IUldkClient uldk, IUugClient uug, TimeProvider timeProvider)
{
    private const string UldkName = "GUGiK ULDK (Usługa Lokalizacji Działek Katastralnych)";
    private const string UldkEndpoint = "https://uldk.gugik.gov.pl/";
    private const string UugName = "GUGiK UUG (Usługa Geokodowania)";
    private const string UugEndpoint = "https://services.gugik.gov.pl/uug/";

    private const string AddressPointNote = "An address point marks a building entrance; the property may span more than this parcel.";
    private const string UnresolvedAddressNote = "The address did not resolve to a single address point; pick one of the candidates or give the full address with a house number.";

    [McpServerTool(Name = "geocode_address", Title = "Find coordinates of a Polish address", ReadOnly = true, OpenWorld = true, Idempotent = true, Destructive = false, UseStructuredContent = true, OutputSchemaType = typeof(GeocodeResult))]
    [Description("Finds the coordinates of a Polish address (for example \"Warszawa, Krucza 50\") with GUGiK's address geocoder (UUG). Each match says whether it is an address, a street or a city: a partial address gives a street or city match whose point is only a representative point. Coordinates are WGS-84 (latitude, longitude). Public data from the national address register via GUGiK, based on EGiB and the address registers. Never returns or accepts land-registry (KW) numbers or owner data.")]
    public async Task<CallToolResult> GeocodeAddress(
        [Description("Free-text address with the town and, for an exact point, the street and house number, e.g. \"Warszawa, Krucza 50\".")] string address,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return ToolResults.Error("Give an address, e.g. \"Warszawa, Krucza 50\".");
        }

        var matches = await uug.GeocodeAsync(address, cancellationToken).ConfigureAwait(false);
        return ToolResults.Ok(
            new GeocodeResult { Matches = [.. matches.Select(ToMatch)], Source = UugSource() },
            McpJson.Relaxed.GeocodeResult);
    }

    [McpServerTool(Name = "find_parcel", Title = "Find a cadastral parcel", ReadOnly = true, OpenWorld = true, Idempotent = true, Destructive = false, UseStructuredContent = true, OutputSchemaType = typeof(FindParcelResult))]
    [Description("Finds Polish cadastral parcels (działki ewidencyjne) with GUGiK's ULDK service, by exactly one of: a parcel ID (TERYT format, e.g. 146510_8.0507.41/1), WGS-84 coordinates (latitude + longitude; latitude comes first), an address, or a region name with a parcel number (e.g. \"Krzewina 134\", which can match several parcels). Returns the parcel ID, number, region, commune, county, voivodeship and the center of its bounding box in WGS-84; includeGeometry adds the full boundary as WKT in EPSG:4326 (can be long for a large parcel; not available for regionAndNumber searches). An address must resolve to a single address point to pick a parcel, otherwise the geocoder's candidates are returned. Data comes from EGiB via GUGiK. Never returns or accepts land-registry (KW) numbers or owner data.")]
    public async Task<CallToolResult> FindParcel(
        [Description("Parcel ID in TERYT format: commune code, region, optional sheet and number, e.g. 146510_8.0507.41/1.")] string? parcelId = null,
        [Description("WGS-84 latitude in degrees north, 48.9 to 55.0 (give together with longitude).")] double? latitude = null,
        [Description("WGS-84 longitude in degrees east, 14.0 to 24.2 (give together with latitude).")] double? longitude = null,
        [Description("Address with a house number, e.g. \"Warszawa, Krucza 50\". It is geocoded first, so a street or town alone gives candidates, not a parcel.")] string? address = null,
        [Description("Region name and parcel number, e.g. \"Krzewina 134\"; can match several parcels. No geometry is returned for this search.")] string? regionAndNumber = null,
        [Description("Also return the parcel boundary as WKT in EPSG:4326 (not for regionAndNumber).")] bool includeGeometry = false,
        CancellationToken cancellationToken = default)
    {
        if ((latitude is null) != (longitude is null))
        {
            return ToolResults.Error("Give latitude and longitude together.");
        }

        var inputs = new[] { parcelId, address, regionAndNumber }.Count(v => !string.IsNullOrWhiteSpace(v)) + (latitude is null ? 0 : 1);
        if (inputs != 1)
        {
            return ToolResults.Error("Give exactly one of: parcelId, latitude+longitude, address, regionAndNumber.");
        }

        var kind = includeGeometry ? ParcelGeometryKind.Wgs84 : ParcelGeometryKind.None;

        if (!string.IsNullOrWhiteSpace(parcelId))
        {
            if (!ParcelId.TryParse(parcelId, out var id))
            {
                return ToolResults.Error("'" + parcelId + "' is not a valid parcel ID (TERYT format, e.g. 146510_8.0507.41/1).");
            }

            return Single(await uldk.GetParcelByIdAsync(id, kind, cancellationToken).ConfigureAwait(false), null, null, UldkSource());
        }

        if (latitude is { } lat && longitude is { } lon)
        {
            Wgs84Point point;
            try
            {
                point = new Wgs84Point(lat, lon);
            }
            catch (ArgumentOutOfRangeException)
            {
                return ToolResults.Error("Coordinates must be WGS-84 within Poland: latitude 48.9 to 55.0 and longitude 14.0 to 24.2. "
                    + "Latitude comes first (Warsaw is about latitude 52.2, longitude 21.0); a swapped pair is rejected.");
            }

            return Single(await uldk.GetParcelAtAsync(point, kind, cancellationToken).ConfigureAwait(false), null, null, UldkSource());
        }

        if (!string.IsNullOrWhiteSpace(address))
        {
            return await FindByAddressAsync(address, kind, cancellationToken).ConfigureAwait(false);
        }

        var found = await uldk.FindParcelsAsync(regionAndNumber!, cancellationToken).ConfigureAwait(false);
        return ToolResults.Ok(
            new FindParcelResult
            {
                Parcels = [.. found.Select(p => ToView(p, null))],
                Note = found.Count == 0
                    ? "ULDK has no parcel for this region name and number."
                    : includeGeometry ? "Geometry is not available for regionAndNumber searches; look a parcel up by its parcelId to get its boundary." : null,
                Sources = [UldkSource()],
            },
            McpJson.Relaxed.FindParcelResult);
    }

    private async Task<CallToolResult> FindByAddressAsync(string address, ParcelGeometryKind kind, CancellationToken cancellationToken)
    {
        var matches = await uug.GeocodeAsync(address, cancellationToken).ConfigureAwait(false);

        // The same address may come back more than once (e.g. one point per entrance record); one distinct address is enough.
        var points = matches
            .Where(m => m.Kind == UugMatchKind.Address)
            .DistinctBy(m => (m.City, m.CityPart, m.Street, m.Number, m.PostalCode, m.Teryt, m.Simc, m.Ulic))
            .ToList();
        if (points.Count != 1)
        {
            return ToolResults.Ok(
                new FindParcelResult { Parcels = [], Candidates = [.. matches.Select(ToMatch)], Note = UnresolvedAddressNote, Sources = [UugSource()] },
                McpJson.Relaxed.FindParcelResult);
        }

        var parcel = await uldk.GetParcelAtAsync(points[0].Location, kind, cancellationToken).ConfigureAwait(false);
        return Single(parcel, ToMatch(points[0]), AddressPointNote, UugSource(), UldkSource());
    }

    private static CallToolResult Single(UldkParcel? parcel, GeocodeMatch? matched, string? note, params SourceInfo[] sources) =>
        ToolResults.Ok(
            new FindParcelResult
            {
                Parcels = parcel is null ? [] : [ToView(parcel, parcel.Geometry?.Wkt)],
                MatchedAddress = matched,
                Note = parcel is null ? "ULDK has no parcel at this location or with this ID." : note,
                Sources = sources,
            },
            McpJson.Relaxed.FindParcelResult);

    private static ParcelView ToView(UldkParcel parcel, string? wkt)
    {
        var center = parcel.Extent.Center.ToWgs84();
        return new ParcelView
        {
            ParcelId = parcel.Id.ToString(),
            Number = parcel.Number,
            Region = parcel.RegionName,
            Commune = parcel.CommuneName,
            County = parcel.CountyName,
            Voivodeship = parcel.VoivodeshipName,
            Center = new GeoPosition(center.Latitude, center.Longitude),
            GeometryWkt = wkt,
        };
    }

    private static GeocodeMatch ToMatch(UugMatch match) => new()
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

    private SourceInfo UldkSource() => new(UldkName, UldkEndpoint, timeProvider.GetUtcNow(), null);

    private SourceInfo UugSource() => new(UugName, UugEndpoint, timeProvider.GetUtcNow(), null);
}
