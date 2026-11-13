using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.Ceidg;

/// <summary>HttpClient-based implementation of <see cref="ICeidgClient"/>.</summary>
public sealed class CeidgClient : ICeidgClient
{
    private const string Service = "CEIDG";

    private readonly HttpClient _httpClient;
    private readonly CeidgClientOptions _options;

    /// <summary>Creates a client. The <paramref name="httpClient"/> is not disposed by this class.</summary>
    /// <exception cref="ArgumentException"><see cref="CeidgClientOptions.BaseAddress"/> is not an absolute https URI.</exception>
    public CeidgClient(HttpClient httpClient, CeidgClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _options = options ?? new CeidgClientOptions();
        _options.Validate();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CeidgBusiness>> FindByNipAsync(Nip nip, CancellationToken cancellationToken = default)
    {
        if (nip.IsEmpty)
        {
            throw new ArgumentException("The NIP is empty.", nameof(nip));
        }

        return await GetAsync("firma?nip=" + nip, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CeidgBusiness>> FindByRegonAsync(Regon regon, CancellationToken cancellationToken = default)
    {
        if (regon.IsEmpty)
        {
            throw new ArgumentException("The REGON is empty.", nameof(regon));
        }

        return await GetAsync("firma?regon=" + regon, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<CeidgBusiness?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The id is empty.", nameof(id));
        }

        // The API writes ids in upper case and says parameter values are case-sensitive.
        var found = await GetAsync("firma/" + id.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant(), cancellationToken, notFoundIsEmpty: true).ConfigureAwait(false);
        return found.Count == 0 ? null : found[0];
    }

    private async Task<IReadOnlyList<CeidgBusiness>> GetAsync(string relative, CancellationToken cancellationToken, bool notFoundIsEmpty = false)
    {
        _options.Validate();   // the options are mutable: BaseAddress may have changed since construction
        var token = _options.ResolveToken();
        using var request = HttpHelpers.CreateGet(new Uri(_options.ResolveBaseAddress(), relative));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NoContent:
                return [];
            case HttpStatusCode.NotFound when notFoundIsEmpty:
                return [];
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                // No body is kept: a gateway could echo the Authorization header into it.
                throw new CeidgAuthenticationException(
                    "CEIDG did not accept the token (HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ").",
                    response.StatusCode,
                    null);
            case HttpStatusCode.BadRequest:
                throw await RejectedAsync(response, token, withCode: true, cancellationToken).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                await response.ThrowForStatusAsync(Service, cancellationToken).ConfigureAwait(false);
            }

            // other 4xx, for example 404 from a query endpoint: an error, so that a wrong BaseAddress is not read as "no result"
            throw await RejectedAsync(response, token, withCode: false, cancellationToken).ConfigureAwait(false);
        }

        var (value, body) = await response.ReadJsonAsync(CeidgJson.Response, Service, cancellationToken).ConfigureAwait(false);
        var result = new List<CeidgBusiness>();
        if (value?.Firma is { } list)
        {
            try
            {
                foreach (var dto in list)
                {
                    result.Add(CeidgMapper.Map(dto));
                }
            }
            catch (FormatException ex)
            {
                throw HttpHelpers.UnreadableResponse(Service + " returned a value this library cannot read: " + ex.Message, response, body, ex);
            }
        }

        return result;
    }

    private static async Task<PolishOpenDataApiException> RejectedAsync(HttpResponseMessage response, string token, bool withCode, CancellationToken cancellationToken)
    {
        // HttpResponseMessage.Content is never null from a real HttpClient, but a custom handler on .NET Framework may leave it so.
        var body = response.Content is null ? string.Empty : await response.Content.ReadStringAsync(cancellationToken).ConfigureAwait(false);
        string? code = null;
        if (withCode)
        {
            try
            {
                code = JsonSerializer.Deserialize(body, CeidgJson.Error)?.Code;
            }
            catch (JsonException)
            {
                // not the documented error body: keep the snippet, no code
            }

            // the documented codes are upper-case words (NIEPOPRAWNY_NUMER_NIP); anything else is not passed on
            if (code is null || code.Length > 64 || !code.All(static c => (c >= 'A' && c <= 'Z') || c == '_'))
            {
                code = null;
            }
        }

        var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        return new PolishOpenDataApiException(
            Service + " rejected the request (HTTP " + status + (code is null ? string.Empty : ", " + code) + ").",
            response.StatusCode,
            code,
            HttpHelpers.Truncate(body.Replace(token, "***")));
    }
}
