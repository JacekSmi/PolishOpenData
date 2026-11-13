using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.Bdl;

/// <summary>HttpClient-based implementation of <see cref="IBdlClient"/>.</summary>
public sealed class BdlClient : IBdlClient
{
    private const string Service = "BDL";
    private const int PageSize = 100;

    private readonly HttpClient _httpClient;
    private readonly BdlClientOptions _options;

    /// <summary>Creates a client. The <paramref name="httpClient"/> is not disposed by this class.</summary>
    /// <exception cref="ArgumentException"><see cref="BdlClientOptions.BaseAddress"/> is not an absolute https URI.</exception>
    public BdlClient(HttpClient httpClient, BdlClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _options = options ?? new BdlClientOptions();
        _options.Validate();
    }

    /// <inheritdoc/>
    public async Task<BdlPage<BdlUnit>> SearchUnitsAsync(string name, BdlLevel? level = null, CancellationToken cancellationToken = default)
    {
        RequireText(name, nameof(name), 2);
        RejectLocality(level);
        var query = new Query().Add("name", name.Trim());
        if (level is { } l)
        {
            query.Add("level", Number((int)l));
        }

        var result = await SendAsync("units/search", query.Add("page-size", "100"), BdlJson.Units, notFoundIsNull: false, ToUnits, cancellationToken).ConfigureAwait(false);
        return result ?? new BdlPage<BdlUnit>([], 0);
    }

    /// <inheritdoc/>
    public Task<BdlUnit?> GetUnitAsync(string unitId, CancellationToken cancellationToken = default)
    {
        RequireText(unitId, nameof(unitId), 1);
        return SendAsync("units/" + Uri.EscapeDataString(unitId.Trim()), new Query(), BdlJson.Unit, notFoundIsNull: true,
            static dto => string.IsNullOrEmpty(dto.Id) ? null : BdlMapper.ToUnit(dto), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BdlPage<BdlVariable>> SearchVariablesAsync(string text, string? subjectId = null, CancellationToken cancellationToken = default)
    {
        RequireText(text, nameof(text), 2);
        var query = new Query().Add("name", text.Trim());
        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query.Add("subject-id", subjectId!.Trim());
        }

        var page = await SendAsync("variables/search", query.Add("page-size", "100"), BdlJson.Variables, notFoundIsNull: false,
            static dto => new BdlPage<BdlVariable>(dto.Results is null ? [] : dto.Results.ConvertAll(BdlMapper.ToVariable), dto.TotalRecords ?? dto.Results?.Count ?? 0), cancellationToken).ConfigureAwait(false);
        return page ?? new BdlPage<BdlVariable>([], 0);
    }

    /// <inheritdoc/>
    public Task<BdlVariable?> GetVariableAsync(int variableId, CancellationToken cancellationToken = default) =>
        SendAsync("variables/" + Number(variableId), new Query(), BdlJson.Variable, notFoundIsNull: true,
            static dto => dto.Id == 0 ? null : BdlMapper.ToVariable(dto), cancellationToken);

    /// <inheritdoc/>
    public Task<BdlSubject?> GetSubjectAsync(string subjectId, CancellationToken cancellationToken = default)
    {
        RequireText(subjectId, nameof(subjectId), 1);
        return SendAsync("subjects/" + Uri.EscapeDataString(subjectId.Trim()), new Query(), BdlJson.Subject, notFoundIsNull: true,
            static dto => string.IsNullOrEmpty(dto.Id) ? null : BdlMapper.ToSubject(dto), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BdlUnitData> GetDataByUnitAsync(string unitId, IReadOnlyCollection<int> variableIds, IReadOnlyCollection<int>? years = null, CancellationToken cancellationToken = default)
    {
        RequireText(unitId, nameof(unitId), 1);
        ArgumentNullException.ThrowIfNull(variableIds);
        var ids = variableIds.Distinct().ToList();
        if (ids.Count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(variableIds), "Pass 1 to 100 distinct variable ids.");
        }

        var query = new Query();
        foreach (var id in ids)
        {
            query.Add("var-id", Number(id));
        }

        AddYears(query, years);
        var data = await SendAsync("data/by-unit/" + Uri.EscapeDataString(unitId.Trim()), query.Add("page-size", "100"), BdlJson.UnitData, notFoundIsNull: false, BdlMapper.ToUnitData, cancellationToken).ConfigureAwait(false);
        return data ?? new BdlUnitData(unitId.Trim(), string.Empty, []);
    }

    /// <inheritdoc/>
    public async Task<BdlVariableData> GetDataByVariableAsync(int variableId, BdlLevel level, string? parentUnitId = null, IReadOnlyCollection<int>? years = null, int maxPages = 1, int startPage = 0, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPages, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(startPage, 0);
        RejectLocality(level);
        BdlVariableDataDto? merged = null;
        int? nextPage = null;
        var rateLimited = false;
        for (var page = startPage; page < startPage + maxPages; page++)
        {
            var query = new Query().Add("unit-level", Number((int)level));
            if (!string.IsNullOrWhiteSpace(parentUnitId))
            {
                query.Add("unit-parent-id", parentUnitId!.Trim());
            }

            AddYears(query, years);
            query.Add("page-size", "100").Add("page", Number(page));
            BdlVariableDataDto? dto;
            try
            {
                dto = await SendAsync("data/by-variable/" + Number(variableId), query, BdlJson.VariableData, notFoundIsNull: false, static d => d, cancellationToken).ConfigureAwait(false);
            }
            catch (QuotaExceededException) when (merged is not null)
            {
                // keep the pages already read (each cost a request) and say where to continue
                rateLimited = true;
                nextPage = page;
                break;
            }

            if (dto is null)
            {
                break;
            }

            if (merged is null)
            {
                merged = dto;
            }
            else
            {
                (merged.Results ??= []).AddRange(dto.Results ?? []);
            }

            // more pages exist when the total says so, or, with no total, while pages come back full
            var count = dto.Results?.Count ?? 0;
            var more = count > 0 && (dto.TotalRecords is { } total ? (page + 1) * PageSize < total : count >= PageSize);
            if (!more)
            {
                break;
            }

            if (page + 1 == startPage + maxPages)
            {
                nextPage = page + 1;
            }
        }

        merged ??= new BdlVariableDataDto { VariableId = variableId };
        try
        {
            return BdlMapper.ToVariableData(merged, nextPage, rateLimited);
        }
        catch (FormatException ex)
        {
            throw new PolishOpenDataApiException(Service + " returned a value this library cannot read: " + ex.Message, HttpStatusCode.OK, null, null, ex);
        }
    }

    private static void AddYears(Query query, IReadOnlyCollection<int>? years)
    {
        foreach (var y in years ?? [])
        {
            query.Add("year", Number(y));
        }
    }

    private static BdlPage<BdlUnit> ToUnits(BdlPagedDto<BdlUnitDto> page) =>
        new(page.Results is null ? [] : page.Results.ConvertAll(BdlMapper.ToUnit), page.TotalRecords ?? page.Results?.Count ?? 0);

    private static void RejectLocality(BdlLevel? level)
    {
        if (level == BdlLevel.Locality)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Statistical localities (level 7) are not supported.");
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void RequireText(string? value, string parameter, int minLength)
    {
        if (value is null || value.Trim().Length < minLength)
        {
            throw new ArgumentException("Pass at least " + Number(minLength) + " non-blank character(s).", parameter);
        }
    }

    // Sends one GET, maps the body, and turns a mapper FormatException into an "unreadable response" error.
    private async Task<TResult?> SendAsync<TDto, TResult>(string path, Query query, JsonTypeInfo<TDto> typeInfo, bool notFoundIsNull, Func<TDto, TResult?> map, CancellationToken cancellationToken)
        where TDto : class
    {
        _options.Validate();   // the options are mutable: BaseAddress may have changed since construction
        var clientId = _options.ResolveClientId();
        query.Add("format", "json").Add("lang", _options.Language == BdlLanguage.En ? "en" : "pl");
        using var request = HttpHelpers.CreateGet(new Uri(_options.ResolveBaseAddress(), path + query));
        if (clientId is not null)
        {
            request.Headers.TryAddWithoutValidation("X-ClientId", clientId);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound && notFoundIsNull)
        {
            return default;
        }

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.PreconditionFailed)
        {
            throw await RejectedAsync(response, clientId, cancellationToken).ConfigureAwait(false);
        }

        if ((int)response.StatusCode == 429)
        {
            // the body is never read; GUS reports when the window resets in X-Rate-Limit-Reset
            throw new QuotaExceededException(Service + " rate limit reached (HTTP 429).", response.StatusCode, null, ResetTime(response), HttpHelpers.GetRetryAfter(response));
        }

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode >= 500)
            {
                await response.ThrowForStatusAsync(Service, cancellationToken).ConfigureAwait(false);
            }

            // any other 4xx: a gateway could echo the X-ClientId header into its page, so the body goes through the same scrubbing
            throw await RejectedAsync(response, clientId, cancellationToken).ConfigureAwait(false);
        }

        var (value, body) = await response.ReadJsonAsync(typeInfo, Service, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return default;
        }

        try
        {
            return map(value);
        }
        catch (FormatException ex)
        {
            throw HttpHelpers.UnreadableResponse(Service + " returned a value this library cannot read: " + ex.Message, response, body, ex);
        }
    }

    private static DateTimeOffset? ResetTime(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Rate-Limit-Reset", out var values)
        && DateTimeOffset.TryParse(values.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.RoundtripKind, out var reset)
            ? reset
            : null;

    private static async Task<PolishOpenDataApiException> RejectedAsync(HttpResponseMessage response, string? clientId, CancellationToken cancellationToken)
    {
        // HttpResponseMessage.Content is never null from a real HttpClient, but a custom handler on .NET Framework may leave it so.
        var body = response.Content is null ? string.Empty : await response.Content.ReadStringAsync(cancellationToken).ConfigureAwait(false);
        int? code = null;
        string? reason = null;
        try
        {
            var first = JsonSerializer.Deserialize(body, BdlJson.Errors)?.Errors?.FirstOrDefault();
            code = first?.ErrorCode;
            reason = first?.ErrorReason;
        }
        catch (JsonException)
        {
            // not the documented error body: keep the snippet, no code
        }

        var scrubbed = Scrub(body, clientId);
        var status = Number((int)response.StatusCode);
        var codeText = code is { } c ? Number(c) : null;
        return new PolishOpenDataApiException(
            Service + " rejected the request (HTTP " + status + (codeText is null ? string.Empty : ", code " + codeText) + ")" + ReasonSuffix(reason, clientId) + ".",
            response.StatusCode,
            codeText,
            HttpHelpers.Truncate(scrubbed));
    }

    private static string Scrub(string text, string? clientId) =>
        clientId is null ? text : Regex.Replace(text, Regex.Escape(clientId), "***", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // GUS explains a rejected request in errorReason (for example which parameter is missing): pass it on, scrubbed and bounded.
    private static string ReasonSuffix(string? reason, string? clientId)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        var clean = new string(Scrub(reason!, clientId).Where(static c => !char.IsControl(c)).ToArray()).Trim().TrimEnd('.');
        return clean.Length == 0 ? string.Empty : ": " + (clean.Length > 200 ? clean.Substring(0, 200) : clean);
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
