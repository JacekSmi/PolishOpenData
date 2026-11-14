using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.Sejm;

/// <summary>HttpClient-based implementation of <see cref="ISejmClient"/>.</summary>
public sealed class SejmClient : ISejmClient
{
    private const string Service = "Sejm";
    private const string JsonAccept = "application/json";
    private const int MaxJsonBytes = 2_000_000;
    private const int MaxLimit = 500;

    private readonly HttpClient _httpClient;
    private readonly SejmClientOptions _options;

    /// <summary>Creates a client. The <paramref name="httpClient"/> is not disposed by this class.</summary>
    /// <exception cref="ArgumentException"><see cref="SejmClientOptions.BaseAddress"/> is not an absolute https URI.</exception>
    public SejmClient(HttpClient httpClient, SejmClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _options = options ?? new SejmClientOptions();
        _options.Validate();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SejmTerm>> GetTermsAsync(CancellationToken cancellationToken = default)
    {
        var terms = await RequestAsync("term", (body, _) => Parse(body, SejmJson.Terms).Where(static t => t is not null).Select(SejmMapper.ToTerm).ToList(), cancellationToken).ConfigureAwait(false);
        return terms ?? [];
    }

    /// <inheritdoc/>
    public async Task<SejmTerm> GetCurrentTermAsync(CancellationToken cancellationToken = default)
    {
        var terms = await GetTermsAsync(cancellationToken).ConfigureAwait(false);
        return terms.FirstOrDefault(static t => t.Current)
            ?? throw new PolishOpenDataApiException(Service + " reports no current term.", HttpStatusCode.OK, null, null);
    }

    /// <inheritdoc/>
    public async Task<SejmPage<SejmProcessSummary>> SearchProcessesAsync(SejmProcessSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        RequireTerm(search.Term);
        RequirePaging(search.Limit, search.Offset);

        var query = new Query();
        if (!string.IsNullOrWhiteSpace(search.Title))
        {
            query.Add("title", search.Title!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search.DocumentType))
        {
            query.Add("documentType", search.DocumentType!.Trim());
        }

        if (search.ModifiedSince is { } since)
        {
            query.Add("modifiedSince", since.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
        }

        query.Add("limit", Number(search.Limit));
        query.Add("offset", Number(search.Offset));

        var path = "term" + Number(search.Term) + "/processes" + (search.PassedOnly ? "/passed" : string.Empty) + query;
        var page = await RequestAsync(path, (body, headers) =>
        {
            var items = Parse(body, SejmJson.Processes).Where(static p => p is not null).Select(SejmMapper.ToProcessSummary).ToList();
            var total = TotalCount(headers);
            return new SejmPage<SejmProcessSummary>(items, total, total is { } t ? search.Offset + items.Count < t : items.Count >= search.Limit);
        }, cancellationToken).ConfigureAwait(false);
        return page ?? new SejmPage<SejmProcessSummary>([], null, false);
    }

    /// <inheritdoc/>
    public Task<SejmProcess?> GetProcessAsync(int term, string number, CancellationToken cancellationToken = default)
    {
        RequireTerm(term);
        var print = SejmIds.Print(number);
        return RequestAsync("term" + Number(term) + "/processes/" + Uri.EscapeDataString(print), (body, _) => SejmMapper.ToProcess(Parse(body, SejmJson.Process)), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<SejmPrint?> GetPrintAsync(int term, string number, CancellationToken cancellationToken = default)
    {
        RequireTerm(term);
        var print = SejmIds.Print(number);
        return RequestAsync("term" + Number(term) + "/prints/" + Uri.EscapeDataString(print), (body, _) => SejmMapper.ToPrint(Parse(body, SejmJson.Print)), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SejmBill?> GetBillForPrintAsync(int term, string print, CancellationToken cancellationToken = default)
    {
        RequireTerm(term);
        var number = SejmIds.Print(print);
        var query = new Query().Add("print", number).Add("limit", "5");
        var bill = await RequestAsync("term" + Number(term) + "/bills" + query, (body, _) =>
        {
            var match = Parse(body, SejmJson.Bills).FirstOrDefault(b => b is not null && string.Equals(b.Print?.Trim(), number, StringComparison.Ordinal));
            return match is null ? null : SejmMapper.ToBill(match);
        }, cancellationToken).ConfigureAwait(false);
        return bill;
    }

    private static void RequireTerm(int term) => ArgumentOutOfRangeException.ThrowIfLessThan(term, 1);

    private static void RequirePaging(int limit, int offset)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxLimit);
        ArgumentOutOfRangeException.ThrowIfLessThan(offset, 0);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int? TotalCount(HttpResponseHeaders headers) =>
        headers.TryGetValues("X-Total-Count", out var values)
        && int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var total)
            ? total
            : null;

    private static T Parse<T>(byte[] body, JsonTypeInfo<T> typeInfo)
    {
        using var stream = new MemoryStream(body, writable: false);
        return JsonSerializer.Deserialize(stream, typeInfo) ?? throw new JsonException("The response is null.");
    }

    // One GET. 404, 204 and an empty 2xx body are all "nothing here" (null): the API is inconsistent about which it sends. A mapper or JSON
    // failure becomes an unreadable-response error; the WAF's 403 HTML page is a non-success status, so it never reaches the JSON parser.
    private async Task<T?> RequestAsync<T>(string relative, Func<byte[], HttpResponseHeaders, T?> handle, CancellationToken cancellationToken)
        where T : class
    {
        _options.Validate();   // the options are mutable: BaseAddress may have changed since construction
        using var request = HttpHelpers.CreateGet(new Uri(_options.ResolveBaseAddress(), relative));
        request.Headers.Remove("Accept");
        request.Headers.TryAddWithoutValidation("Accept", JsonAccept);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            await response.ThrowForStatusAsync(Service, cancellationToken).ConfigureAwait(false);
        }

        // HttpResponseMessage.Content is never null from a real HttpClient, but a custom handler on .NET Framework may leave it so.
        var body = response.Content is null ? [] : await response.Content.ReadBytesAsync(MaxJsonBytes, cancellationToken).ConfigureAwait(false);
        if (body.Length > MaxJsonBytes)
        {
            throw HttpHelpers.UnreadableResponse(Service + " returned more than " + Number(MaxJsonBytes) + " bytes; refusing to read it.", response, body);
        }

        if (body.Length == 0)
        {
            return null;
        }

        try
        {
            return handle(body, response.Headers);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw HttpHelpers.UnreadableResponse(Service + " returned a response this library cannot read: " + ex.Message, response, body, ex);
        }
    }

    /// <summary>Builds a query string with escaped values, in the order given.</summary>
    private sealed class Query
    {
        private readonly StringBuilder _text = new();

        public Query Add(string name, string value)
        {
            _ = _text.Append(_text.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
            return this;
        }

        public override string ToString() => _text.ToString();
    }
}
