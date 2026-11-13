using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.Bdl;

/// <summary>HttpClient-based implementation of <see cref="IBdlClient"/>.</summary>
public sealed class BdlClient : IBdlClient
{
    private const string Service = "BDL";

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
    public async Task<IReadOnlyList<BdlUnit>> SearchUnitsAsync(string name, BdlLevel? level = null, CancellationToken cancellationToken = default)
    {
        RequireText(name, nameof(name), 2);
        var query = new Query().Add("name", name.Trim());
        if (level is { } l)
        {
            query.Add("level", Number((int)l));
        }

        var result = await SendAsync("units/search", query.Add("page-size", "100"), BdlJson.Units, notFoundIsNull: false, ToUnits, cancellationToken).ConfigureAwait(false);
        return result ?? [];
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
            static dto => new BdlPage<BdlVariable>(dto.Results is null ? [] : dto.Results.ConvertAll(BdlMapper.ToVariable), dto.TotalRecords), cancellationToken).ConfigureAwait(false);
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

    private static IReadOnlyList<BdlUnit> ToUnits(BdlPagedDto<BdlUnitDto> page) =>
        page.Results is null ? [] : page.Results.ConvertAll(BdlMapper.ToUnit);

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

        if (!response.IsSuccessStatusCode)
        {
            await response.ThrowForStatusAsync(Service, cancellationToken).ConfigureAwait(false);
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

    private static async Task<PolishOpenDataApiException> RejectedAsync(HttpResponseMessage response, string? clientId, CancellationToken cancellationToken)
    {
        // HttpResponseMessage.Content is never null from a real HttpClient, but a custom handler on .NET Framework may leave it so.
        var body = response.Content is null ? string.Empty : await response.Content.ReadStringAsync(cancellationToken).ConfigureAwait(false);
        int? code = null;
        try
        {
            code = JsonSerializer.Deserialize(body, BdlJson.Errors)?.Errors?.FirstOrDefault()?.ErrorCode;
        }
        catch (JsonException)
        {
            // not the documented error body: keep the snippet, no code
        }

        var scrubbed = clientId is null ? body : body.Replace(clientId, "***");
        var status = Number((int)response.StatusCode);
        var codeText = code is { } c ? Number(c) : null;
        return new PolishOpenDataApiException(
            Service + " rejected the request (HTTP " + status + (codeText is null ? string.Empty : ", code " + codeText) + ").",
            response.StatusCode,
            codeText,
            HttpHelpers.Truncate(scrubbed));
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
