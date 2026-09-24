using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.BialaLista;

/// <summary>HttpClient-based implementation of <see cref="IBialaListaClient"/>.</summary>
public sealed class BialaListaClient : IBialaListaClient
{
    private const int MaxBatchSize = 30;
    private readonly HttpClient _httpClient;
    private readonly BialaListaClientOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly BialaListaQuotaTracker _quotaTracker;

    /// <summary>Creates a client. The <paramref name="httpClient"/> is not disposed by this class.</summary>
    /// <param name="httpClient">Transport.</param>
    /// <param name="options">Options; defaults when null.</param>
    /// <param name="timeProvider">Clock for the default date and quota days; system clock when null.</param>
    /// <param name="quotaTracker">
    /// Shared tracker; a private one is created when null. The upstream limit applies per IP address, not per
    /// client instance: create the client once (e.g. via <see cref="BialaListaServiceCollectionExtensions.AddBialaListaClient"/>)
    /// or pass one shared tracker explicitly — a client created without DI and without a shared tracker gets a
    /// private tracker that never sees requests made by other instances.
    /// </param>
    public BialaListaClient(HttpClient httpClient, BialaListaClientOptions? options = null, TimeProvider? timeProvider = null, BialaListaQuotaTracker? quotaTracker = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _options = options ?? new BialaListaClientOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _quotaTracker = quotaTracker ?? new BialaListaQuotaTracker(_timeProvider);
    }

    /// <inheritdoc/>
    public async Task<BialaListaResult<VatSubject?>> FindByNipAsync(Nip nip, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        RequireValue(nip.IsEmpty, nameof(nip));
        var response = await SendAsync("api/search/nip/" + nip.ToString(), date, BialaListaRequestKind.Search, BialaListaJson.EntityResponse, cancellationToken).ConfigureAwait(false);
        return MapEntity(response.Result);
    }

    /// <inheritdoc/>
    public async Task<BialaListaResult<IReadOnlyList<VatBatchEntry>>> FindByNipsAsync(IReadOnlyCollection<Nip> nips, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        var path = "api/search/nips/" + JoinBatch(nips, n => n.IsEmpty, n => n.ToString(), nameof(nips));
        var response = await SendAsync(path, date, BialaListaRequestKind.Search, BialaListaJson.EntryListResponse, cancellationToken).ConfigureAwait(false);
        return MapEntries(response.Result);
    }

    /// <inheritdoc/>
    public async Task<BialaListaResult<VatSubject?>> FindByRegonAsync(Regon regon, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        RequireValue(regon.IsEmpty, nameof(regon));
        var response = await SendAsync("api/search/regon/" + regon.ToString(), date, BialaListaRequestKind.Search, BialaListaJson.EntityResponse, cancellationToken).ConfigureAwait(false);
        return MapEntity(response.Result);
    }

    /// <inheritdoc/>
    public async Task<BialaListaResult<IReadOnlyList<VatBatchEntry>>> FindByRegonsAsync(IReadOnlyCollection<Regon> regons, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        var path = "api/search/regons/" + JoinBatch(regons, r => r.IsEmpty, r => r.ToString(), nameof(regons));
        var response = await SendAsync(path, date, BialaListaRequestKind.Search, BialaListaJson.EntryListResponse, cancellationToken).ConfigureAwait(false);
        return MapEntries(response.Result);
    }

    /// <inheritdoc/>
    public async Task<BialaListaResult<IReadOnlyList<VatSubject>>> FindByBankAccountAsync(Nrb account, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        RequireValue(account.IsEmpty, nameof(account));
        var response = await SendAsync("api/search/bank-account/" + account.ToString(), date, BialaListaRequestKind.Search, BialaListaJson.EntityListResponse, cancellationToken).ConfigureAwait(false);
        var list = response.Result ?? throw Malformed();
        return new BialaListaResult<IReadOnlyList<VatSubject>>(VatMapper.MapSubjects(list.Subjects), list.RequestId ?? string.Empty, VatMapper.ParseRequestTime(list.RequestDateTime, _timeProvider), VatMapper.UnknownKeys(list.Extra));
    }

    /// <inheritdoc/>
    public async Task<BialaListaResult<IReadOnlyList<VatBatchEntry>>> FindByBankAccountsAsync(IReadOnlyCollection<Nrb> accounts, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        var path = "api/search/bank-accounts/" + JoinBatch(accounts, a => a.IsEmpty, a => a.ToString(), nameof(accounts));
        var response = await SendAsync(path, date, BialaListaRequestKind.Search, BialaListaJson.EntryListResponse, cancellationToken).ConfigureAwait(false);
        return MapEntries(response.Result);
    }

    /// <inheritdoc/>
    public async Task<BialaListaResult<bool>> CheckBankAccountAsync(Nip nip, Nrb account, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        RequireValue(nip.IsEmpty, nameof(nip));
        RequireValue(account.IsEmpty, nameof(account));
        var response = await SendAsync("api/check/nip/" + nip.ToString() + "/bank-account/" + account.ToString(), date, BialaListaRequestKind.Check, BialaListaJson.CheckResponse, cancellationToken).ConfigureAwait(false);
        return MapCheck(response.Result);
    }

    /// <inheritdoc/>
    public async Task<BialaListaResult<bool>> CheckBankAccountAsync(Regon regon, Nrb account, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        RequireValue(regon.IsEmpty, nameof(regon));
        RequireValue(account.IsEmpty, nameof(account));
        var response = await SendAsync("api/check/regon/" + regon.ToString() + "/bank-account/" + account.ToString(), date, BialaListaRequestKind.Check, BialaListaJson.CheckResponse, cancellationToken).ConfigureAwait(false);
        return MapCheck(response.Result);
    }

    private async Task<T> SendAsync<T>(string path, DateOnly? date, BialaListaRequestKind kind, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where T : class
    {
        if (_options.TrackQuota)
        {
            _quotaTracker.Reserve(kind, kind == BialaListaRequestKind.Search ? _options.SearchLimitPerDay : _options.CheckLimitPerDay);
        }

        var day = (date ?? WarsawTime.Today(_timeProvider)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var request = HttpHelpers.CreateGet(new Uri(_options.BaseAddress, path + "?date=" + day));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorAsync(response, cancellationToken).ConfigureAwait(false);
        }

        using var stream = await response.Content.ReadStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false) ?? throw Malformed();
    }

    private async Task ThrowForErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var body = await response.Content.ReadStringAsync(cancellationToken).ConfigureAwait(false);
        var (code, message) = ParseError(body);

        if (status == 429 || string.Equals(code, "WL-191", StringComparison.Ordinal))
        {
            _quotaTracker.MarkExhausted();
            var resetsAt = WarsawTime.NextMidnightUtc(_timeProvider);
            throw new QuotaExceededException(
                "Biała Lista daily request limit for this IP address is exhausted (" + (code ?? "HTTP 429") + "). " +
                "Requests from this IP, including the ministry's web search, are blocked until midnight Europe/Warsaw (" +
                resetsAt.ToString("u", CultureInfo.InvariantCulture) + ").",
                response.StatusCode,
                code,
                resetsAt,
                HttpHelpers.GetRetryAfter(response));
        }

        if (status >= 500)
        {
            _ = response.EnsureSuccessStatusCode();
        }

        var transient = string.Equals(code, "WL-195", StringComparison.Ordinal) || string.Equals(code, "WL-196", StringComparison.Ordinal);
        throw new PolishOpenDataApiException(
            "Biała Lista rejected the request: " + (code ?? "HTTP " + status.ToString(CultureInfo.InvariantCulture)) + (message is null ? string.Empty : " " + message),
            response.StatusCode,
            code,
            HttpHelpers.Truncate(body),
            transient);
    }

    private static (string? Code, string? Message) ParseError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            return (GetString(root, "code"), GetString(root, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private BialaListaResult<VatSubject?> MapEntity(WlEntityItem? item)
    {
        if (item is null)
        {
            throw Malformed();
        }

        var subject = item.Subject is null ? null : VatMapper.MapSubject(item.Subject);
        return new BialaListaResult<VatSubject?>(subject, item.RequestId ?? string.Empty, VatMapper.ParseRequestTime(item.RequestDateTime, _timeProvider), VatMapper.UnknownKeys(item.Extra));
    }

    private BialaListaResult<IReadOnlyList<VatBatchEntry>> MapEntries(WlEntryList? list)
    {
        if (list is null)
        {
            throw Malformed();
        }

        return new BialaListaResult<IReadOnlyList<VatBatchEntry>>(VatMapper.MapEntries(list.Entries), list.RequestId ?? string.Empty, VatMapper.ParseRequestTime(list.RequestDateTime, _timeProvider), VatMapper.UnknownKeys(list.Extra, list.Entries));
    }

    private BialaListaResult<bool> MapCheck(WlCheck? check)
    {
        var assigned = check?.AccountAssigned switch
        {
            "TAK" => true,
            "NIE" => false,
            _ => throw new PolishOpenDataApiException("Biała Lista returned an unexpected accountAssigned value '" + check?.AccountAssigned + "'."),
        };
        return new BialaListaResult<bool>(assigned, check!.RequestId ?? string.Empty, VatMapper.ParseRequestTime(check.RequestDateTime, _timeProvider), VatMapper.UnknownKeys(check.Extra));
    }

    private static string JoinBatch<T>(IReadOnlyCollection<T> items, Func<T, bool> isEmpty, Func<T, string> format, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(items, parameterName);
        if (items.Count == 0 || items.Count > MaxBatchSize)
        {
            throw new ArgumentException("Between 1 and 30 identifiers are allowed per request; use FindByNipsChunkedAsync for more.", parameterName);
        }

        var parts = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (isEmpty(item))
            {
                throw new ArgumentException("The batch contains an empty identifier.", parameterName);
            }

            parts.Add(format(item));
        }

        return string.Join(",", parts);
    }

    private static void RequireValue(bool isEmpty, string parameterName)
    {
        if (isEmpty)
        {
            throw new ArgumentException("The identifier is empty.", parameterName);
        }
    }

    private static PolishOpenDataApiException Malformed() => new("Biała Lista returned a response without a result.");
}
