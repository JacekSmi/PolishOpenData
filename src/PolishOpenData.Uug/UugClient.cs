using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        var response = await SendAsync("?request=GetAddress&address=" + Uri.EscapeDataString(address.Trim()), cancellationToken).ConfigureAwait(false);
        var kind = ToKind(response.Type);
        // GetAddress answers x = easting, y = northing in EPSG:2180.
        return OrderedRows(response)
            .Select(row => ToMatch(row, kind, new Pl1992Point(Required(row.X), Required(row.Y)).ToWgs84()))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<UugMatch?> ReverseGeocodeAsync(Wgs84Point point, CancellationToken cancellationToken = default)
    {
        // WKT order: longitude first, the reverse of Wgs84Point's constructor.
        var location = "POINT(" + Number(point.Longitude) + " " + Number(point.Latitude) + ")";
        var response = await SendAsync("?request=GetAddressReverse&location=" + Uri.EscapeDataString(location) + "&srid=" + Wgs84Srid.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        var kind = ToKind(response.Type);
        // With srid=4326 the service answers x = longitude, y = latitude.
        return OrderedRows(response)
            .Select(row => ToMatch(row, kind, new Wgs84Point(Required(row.Y), Required(row.X))))
            .FirstOrDefault();
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static double Required(double? coordinate) =>
        coordinate ?? throw new PolishOpenDataApiException("UUG returned a result without coordinates.", null, null, null);

    private static IEnumerable<UugRow> OrderedRows(UugResponse response) =>
        response.Results is null
            ? []
            : response.Results
                .OrderBy(pair => int.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var key) ? key : int.MaxValue)
                .Select(pair => pair.Value);

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

    private async Task<UugResponse> SendAsync(string query, CancellationToken cancellationToken)
    {
        using var request = HttpHelpers.CreateGet(new Uri(_options.BaseAddress, query));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForStatusAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var body = await response.Content.ReadBytesAsync(cancellationToken).ConfigureAwait(false);
        var text = HttpHelpers.Snippet(body);
        // The F5 block page answers 200 with HTML.
        if (response.Content.Headers.ContentType?.MediaType == "text/html" || text.TrimStart().StartsWith('<'))
        {
            throw new PolishOpenDataApiException("UUG blocked the request (HTML block page).", response.StatusCode, "blocked", text, isTransient: true);
        }

        try
        {
            return JsonSerializer.Deserialize(body, UugJson.Response)
                ?? throw HttpHelpers.UnreadableResponse("UUG returned an empty response.", response, body);
        }
        catch (JsonException exception)
        {
            throw HttpHelpers.UnreadableResponse("UUG returned a response that could not be read: " + exception.Message, response, body, exception);
        }
    }

    private static async Task ThrowForStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (status == 429)
        {
            throw new QuotaExceededException("UUG rate limit reached (HTTP 429).", response.StatusCode, null, null, HttpHelpers.GetRetryAfter(response));
        }

        if (status >= 500)
        {
            _ = response.EnsureSuccessStatusCode();
        }

        var body = await response.Content.ReadStringAsync(cancellationToken).ConfigureAwait(false);
        throw new PolishOpenDataApiException(
            "UUG request failed with HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".",
            response.StatusCode,
            null,
            HttpHelpers.Truncate(body));
    }
}
