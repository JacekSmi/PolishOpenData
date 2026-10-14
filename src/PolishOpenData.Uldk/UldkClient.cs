using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Geo;
using PolishOpenData.Internal;

namespace PolishOpenData.Uldk;

/// <summary>HttpClient-based implementation of <see cref="IUldkClient"/>.</summary>
public sealed class UldkClient : IUldkClient
{
    private const string ParcelFields = "id,parcel,region,commune,county,voivodeship,geom_extent,datasource";
    private const int Wgs84Srid = 4326;
    private const int Pl1992Srid = 2180;

    private readonly HttpClient _httpClient;
    private readonly UldkClientOptions _options;

    /// <summary>Creates a client. The <paramref name="httpClient"/> is not disposed by this class.</summary>
    public UldkClient(HttpClient httpClient, UldkClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _options = options ?? new UldkClientOptions();
    }

    /// <inheritdoc/>
    public Task<UldkParcel?> GetParcelByIdAsync(ParcelId id, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("The parcel identifier is empty.", nameof(id));
        }

        return GetParcelAsync("GetParcelById&id=" + Uri.EscapeDataString(id.ToString()), geometry, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<UldkParcel?> GetParcelAtAsync(Pl1992Point point, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default) =>
        GetParcelAsync("GetParcelByXY&xy=" + FormatXy(point), geometry, cancellationToken);

    /// <inheritdoc/>
    public Task<UldkParcel?> GetParcelAtAsync(Wgs84Point point, ParcelGeometryKind geometry = ParcelGeometryKind.None, CancellationToken cancellationToken = default) =>
        GetParcelAsync("GetParcelByXY&xy=" + FormatXy(point), geometry, cancellationToken);

    /// <summary>ULDK wants easting first for 2180.</summary>
    internal static string FormatXy(Pl1992Point point) => Number(point.Easting) + "," + Number(point.Northing) + "," + Pl1992Srid.ToString(CultureInfo.InvariantCulture);

    /// <summary>ULDK wants longitude first for 4326 (WKT order), the reverse of <see cref="Wgs84Point"/>'s constructor.</summary>
    internal static string FormatXy(Wgs84Point point) => Number(point.Longitude) + "," + Number(point.Latitude) + "," + Wgs84Srid.ToString(CultureInfo.InvariantCulture);

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private async Task<UldkParcel?> GetParcelAsync(string lookup, ParcelGeometryKind geometry, CancellationToken cancellationToken)
    {
        var fields = geometry == ParcelGeometryKind.Pl1992 ? ParcelFields + ",geom_wkt" : ParcelFields;
        var rows = await SendAsync("?request=" + lookup + "&result=" + fields + "&srid=2180", cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return null;
        }

        var parcel = ToParcel(rows[0]);
        if (geometry != ParcelGeometryKind.Wgs84)
        {
            return parcel;
        }

        var geometryRows = await SendAsync("?request=GetParcelById&id=" + Uri.EscapeDataString(parcel.Id.ToString()) + "&result=geom_wkt&srid=4326", cancellationToken).ConfigureAwait(false);
        return geometryRows.Count == 0 ? parcel : ToParcel(rows[0], geometryRows[0][0]);
    }

    private async Task<IReadOnlyList<string[]>> SendAsync(string query, CancellationToken cancellationToken)
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
            throw new PolishOpenDataApiException("ULDK blocked the request (HTML block page).", response.StatusCode, "blocked", text, isTransient: true);
        }

        return UldkResponse.Parse(Encoding.UTF8.GetString(body));
    }

    private static UldkParcel ToParcel(string[] row, string? wkt = null)
    {
        // ULDK appends a geom_wkt field only when asked; row order is the order of `result=`.
        wkt ??= row.Length > 8 ? row[8] : null;
        var extent = row.Length > 7 ? row[6].Split(',') : [];
        if (extent.Length != 4 || !ParcelId.TryParse(row[0], out var id))
        {
            throw new PolishOpenDataApiException("ULDK returned a parcel that could not be read.", null, null, HttpHelpers.Truncate(string.Join("|", row)));
        }

        return new UldkParcel
        {
            Id = id,
            Number = row[1],
            Region = NullIfEmpty(row[2]),
            Commune = NullIfEmpty(row[3]),
            County = NullIfEmpty(row[4]),
            Voivodeship = NullIfEmpty(row[5]),
            Extent = new Pl1992Bbox(
                new Pl1992Point(Coordinate(extent[0]), Coordinate(extent[1])),
                new Pl1992Point(Coordinate(extent[2]), Coordinate(extent[3]))),
            DataSource = NullIfEmpty(row[7]),
            Geometry = wkt is null ? null : ToGeometry(wkt),
        };
    }

    private static double Coordinate(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    private static ParcelGeometry ToGeometry(string ewkt)
    {
        const string Prefix = "SRID=";
        var semicolon = ewkt.IndexOf(';');
        if (!ewkt.StartsWith(Prefix, StringComparison.Ordinal) || semicolon < 0 ||
            !int.TryParse(ewkt.AsSpan(Prefix.Length, semicolon - Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var srid))
        {
            throw new PolishOpenDataApiException("ULDK returned a geometry that could not be read.", null, null, HttpHelpers.Truncate(ewkt));
        }

        return new ParcelGeometry { Srid = srid, Wkt = ewkt.Substring(semicolon + 1) };
    }

    private static async Task ThrowForStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (status == 429)
        {
            throw new QuotaExceededException("ULDK rate limit reached (HTTP 429).", response.StatusCode, null, null, HttpHelpers.GetRetryAfter(response));
        }

        if (status >= 500)
        {
            _ = response.EnsureSuccessStatusCode();
        }

        var body = await response.Content.ReadStringAsync(cancellationToken).ConfigureAwait(false);
        throw new PolishOpenDataApiException(
            "ULDK request failed with HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".",
            response.StatusCode,
            null,
            HttpHelpers.Truncate(body));
    }
}
