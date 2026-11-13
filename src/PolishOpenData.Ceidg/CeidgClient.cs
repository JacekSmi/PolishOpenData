using System;
using System.Collections.Generic;
using System.Globalization;
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
        var found = await GetAsync("firma/" + id.ToString("D", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        return found.Count == 0 ? null : found[0];
    }

    private async Task<IReadOnlyList<CeidgBusiness>> GetAsync(string relative, CancellationToken cancellationToken)
    {
        var token = _options.ResolveToken();
        using var request = HttpHelpers.CreateGet(new Uri(_options.ResolveBaseAddress(), relative));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NoContent:
            case HttpStatusCode.NotFound:
                return [];
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                var denied = await response.Content.ReadStringAsync(cancellationToken).ConfigureAwait(false);
                throw new CeidgAuthenticationException(
                    "CEIDG did not accept the token (HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ").",
                    response.StatusCode,
                    HttpHelpers.Truncate(denied));
            case HttpStatusCode.BadRequest:
                throw await BadRequestAsync(response, cancellationToken).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            await response.ThrowForStatusAsync(Service, cancellationToken).ConfigureAwait(false);
        }

        var (value, _) = await response.ReadJsonAsync(CeidgJson.Response, Service, cancellationToken).ConfigureAwait(false);
        var result = new List<CeidgBusiness>();
        if (value?.Firma is { } list)
        {
            foreach (var dto in list)
            {
                result.Add(CeidgMapper.Map(dto));
            }
        }

        return result;
    }

    private static async Task<PolishOpenDataApiException> BadRequestAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadStringAsync(cancellationToken).ConfigureAwait(false);
        string? code = null;
        try
        {
            code = JsonSerializer.Deserialize(body, CeidgJson.Error)?.Code;
        }
        catch (JsonException)
        {
            // not the documented error body: keep the snippet, no code
        }

        return new PolishOpenDataApiException(
            Service + " rejected the request (HTTP 400" + (code is null ? string.Empty : ", " + code) + ").",
            response.StatusCode,
            code,
            HttpHelpers.Truncate(body));
    }
}
