using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Geo;
using PolishOpenData.Internal;

namespace PolishOpenData.Uug;

/// <summary>HttpClient-based implementation of <see cref="IUugClient"/>.</summary>
public sealed class UugClient : IUugClient
{
    private const int Wgs84Srid = 4326;

    private readonly HttpClient _httpClient;
    private readonly UugClientOptions _options;

    /// <summary>Creates a client. The <paramref name="httpClient"/> is not disposed by this class.</summary>
    public UugClient(HttpClient httpClient, UugClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _options = options ?? new UugClientOptions();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<UugMatch>> GeocodeAsync(string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("The address is empty.", nameof(address));
        }

        var (type, rows) = await SendAsync("?request=GetAddress&address=" + Uri.EscapeDataString(address.Trim()), cancellationToken).ConfigureAwait(false);
        var kind = ToKind(type);
        // GetAddress answers x = easting, y = northing in EPSG:2180.
        return rows
            .Select(row => ToMatch(row, kind, Location(() => new Pl1992Point(Required(row.X), Required(row.Y)).ToWgs84())))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<UugMatch?> ReverseGeocodeAsync(Wgs84Point point, CancellationToken cancellationToken = default)
    {
        // default(Wgs84Point) skips the validating constructor (0, 0 is outside Poland).
        if (!Wgs84Point.IsInPoland(point.Latitude, point.Longitude))
        {
            throw new ArgumentOutOfRangeException(nameof(point), point, "Outside the WGS-84 range that covers Poland.");
        }

        // WKT order: longitude first, the reverse of Wgs84Point's constructor.
        var location = "POINT(" + Number(point.Longitude) + " " + Number(point.Latitude) + ")";
        var (type, rows) = await SendAsync("?request=GetAddressReverse&location=" + Uri.EscapeDataString(location) + "&srid=" + Wgs84Srid.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        var kind = ToKind(type);
        // With srid=4326 the service answers x = longitude, y = latitude.
        return rows
            .Select(row => ToMatch(row, kind, Location(() => new Wgs84Point(Required(row.Y), Required(row.X)))))
            .FirstOrDefault();
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static double Required(double? coordinate) =>
        coordinate ?? throw new PolishOpenDataApiException("UUG returned a result without coordinates.", HttpStatusCode.OK, null, null);

    /// <summary>Builds a result's point; coordinates outside Poland are an answer the library cannot use, not a caller error.</summary>
    private static Wgs84Point Location(Func<Wgs84Point> build)
    {
        try
        {
            return build();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new PolishOpenDataApiException("UUG returned a result with coordinates outside Poland.", HttpStatusCode.OK, null, null, exception);
        }
    }

    /// <summary>The rows of <c>results</c> in key order: an object keyed "1", "2", …, or null / an empty array for no match.</summary>
    private static List<UugRow> ReadRows(JsonElement results) => results.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => [],
        JsonValueKind.Array when results.GetArrayLength() == 0 => [],
        JsonValueKind.Object => results.EnumerateObject()
            .OrderBy(property => int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var key) ? key : int.MaxValue)
            .Select(property => property.Value.Deserialize(UugJson.Row) ?? throw new JsonException("A result is null."))
            .ToList(),
        _ => throw new JsonException("The results are neither an object nor empty."),
    };

    private static UugMatchKind ToKind(string? type) => type switch
    {
        "address" => UugMatchKind.Address,
        "street" => UugMatchKind.Street,
        "city" => UugMatchKind.City,
        _ => UugMatchKind.Other,
    };

    private static UugMatch ToMatch(UugRow row, UugMatchKind kind, Wgs84Point location) => new()
    {
        Kind = kind,
        City = row.City,
        CityPart = row.Citypart,
        Street = row.Street,
        Number = row.Number,
        PostalCode = row.Code,
        Teryt = row.Teryt,
        Simc = row.Simc,
        Ulic = row.Ulic,
        Location = location,
        Accuracy = row.Accuracy,
        DistanceMeters = row.Distance,
    };

    private async Task<(string? Type, List<UugRow> Rows)> SendAsync(string query, CancellationToken cancellationToken)
    {
        using var request = HttpHelpers.CreateGet(new Uri(_options.BaseAddress, query));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await response.ThrowForStatusAsync("UUG", cancellationToken).ConfigureAwait(false);
        }

        var body = await response.ReadBodyRejectingBlockPageAsync("UUG", cancellationToken).ConfigureAwait(false);
        try
        {
            var answer = JsonSerializer.Deserialize(body, UugJson.Response)
                ?? throw HttpHelpers.UnreadableResponse("UUG returned an empty response.", response, body);
            return (answer.Type, ReadRows(answer.Results));
        }
        catch (JsonException exception)
        {
            throw HttpHelpers.UnreadableResponse("UUG returned a response that could not be read: " + exception.Message, response, body, exception);
        }
    }
}
