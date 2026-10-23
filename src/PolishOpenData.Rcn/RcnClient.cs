using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.Rcn;

/// <summary>HttpClient-based implementation of <see cref="IRcnClient"/>.</summary>
public sealed class RcnClient : IRcnClient
{
    private readonly HttpClient _httpClient;
    private readonly RcnClientOptions _options;

    /// <summary>Creates a client. The <paramref name="httpClient"/> is not disposed by this class.</summary>
    public RcnClient(HttpClient httpClient, RcnClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _options = options ?? new RcnClientOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.PageSize, 1, nameof(options));
    }

    /// <inheritdoc/>
    public async Task<RcnTransactions> GetTransactionsAsync(RcnQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        var filter = RcnFilter.Build(query);
        var limit = query.MaxRecords;
        var items = new List<RcnTransaction>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // One row beyond MaxRecords tells exactly MaxRecords from more; the response numberMatched and next are never used.
            var count = Math.Min(_options.PageSize, limit + 1 - items.Count);
            var page = await GetPageAsync(query.Layer, filter, count, items.Count, cancellationToken).ConfigureAwait(false);
            items.AddRange(page);
            if (page.Count < count || items.Count > limit)
            {
                break;
            }
        }

        var truncated = items.Count > limit;
        if (truncated)
        {
            items.RemoveRange(limit, items.Count - limit);
        }

        return new RcnTransactions(items, truncated);
    }

    private async Task<IReadOnlyList<RcnTransaction>> GetPageAsync(RcnLayer layer, string filter, int count, int startIndex, CancellationToken cancellationToken)
    {
        var parameters = "?service=WFS&version=2.0.0&request=GetFeature"
            + "&typeNames=" + Uri.EscapeDataString(RcnFilter.TypeName(layer))
            + "&srsName=" + Uri.EscapeDataString("urn:ogc:def:crs:EPSG::2180")
            + "&count=" + count.ToString(CultureInfo.InvariantCulture)
            + "&startIndex=" + startIndex.ToString(CultureInfo.InvariantCulture)
            + "&sortBy=" + Uri.EscapeDataString(RcnFilter.SortBy)
            + "&filter=" + Uri.EscapeDataString(filter);
        using var request = HttpHelpers.CreateGet(new Uri(_options.BaseAddress, parameters));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            if (status != 429 && status < 500)
            {
                // RCN answers a bad request with HTTP 400 and an OGC exception report.
                var errorBody = await response.Content.ReadBytesAsync(cancellationToken).ConfigureAwait(false);
                if (RcnGmlReader.TryReadException(errorBody, response) is { } exception)
                {
                    throw exception;
                }

                // The body is read once (on .NET Framework the stream is gone after that), so build the error here, as ThrowForStatusAsync would.
                throw new PolishOpenDataApiException(
                    "RCN request failed with HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".",
                    response.StatusCode,
                    null,
                    HttpHelpers.Snippet(errorBody));
            }

            await response.ThrowForStatusAsync("RCN", cancellationToken).ConfigureAwait(false);
        }

        var body = await response.Content.ReadBytesAsync(cancellationToken).ConfigureAwait(false);
        // HttpHelpers.ReadBodyRejectingBlockPageAsync treats any body starting with a tag as a block page, which every GML answer is.
        var start = HttpHelpers.Snippet(body).TrimStart();
        if (string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase)
            || start.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || start.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase))
        {
            throw new PolishOpenDataApiException("RCN blocked the request (HTML block page).", response.StatusCode, "blocked", start, isTransient: true);
        }

        return RcnGmlReader.Read(body, layer, response);
    }
}
