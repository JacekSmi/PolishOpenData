using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.Krs;

/// <summary>HttpClient-based implementation of <see cref="IKrsClient"/>.</summary>
public sealed class KrsClient : IKrsClient
{
    private readonly HttpClient _httpClient;
    private readonly KrsClientOptions _options;

    /// <summary>Creates a client. The <paramref name="httpClient"/> is not disposed by this class.</summary>
    public KrsClient(HttpClient httpClient, KrsClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _options = options ?? new KrsClientOptions();
    }

    /// <inheritdoc/>
    public Task<KrsResult<KrsCurrentExtract>> GetCurrentExtractAsync(KrsNumber krs, KrsRegister? register = null, CancellationToken cancellationToken = default) =>
        GetExtractAsync(krs, register, "OdpisAktualny", KrsJson.Current, static envelope => envelope.Odpis, cancellationToken);

    /// <inheritdoc/>
    public Task<KrsResult<KrsFullExtract>> GetFullExtractAsync(KrsNumber krs, KrsRegister? register = null, CancellationToken cancellationToken = default) =>
        GetExtractAsync(krs, register, "OdpisPelny", KrsJson.Full, static envelope => envelope.Odpis, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<KrsNumber> GetChangedAsync(DateOnly day, CancellationToken cancellationToken = default) =>
        GetChangedCoreAsync("api/krs/Biuletyn/" + FormatDay(day), cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<KrsNumber> GetChangedAsync(DateOnly day, int fromHour, int toHour, CancellationToken cancellationToken = default)
    {
        if (fromHour < 0 || fromHour > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(fromHour), fromHour, "The hour must be between 0 and 23.");
        }

        if (toHour < fromHour || toHour > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(toHour), toHour, "The hour must be between fromHour and 23.");
        }

        var path = "api/krs/BiuletynGodzinowy/" + FormatDay(day) +
            "?godzinaOd=" + fromHour.ToString(CultureInfo.InvariantCulture) +
            "&godzinaDo=" + toHour.ToString(CultureInfo.InvariantCulture);
        return GetChangedCoreAsync(path, cancellationToken);
    }

    private async Task<KrsResult<T>> GetExtractAsync<TEnvelope, T>(
        KrsNumber krs,
        KrsRegister? register,
        string kind,
        JsonTypeInfo<TEnvelope> envelopeType,
        Func<TEnvelope, T?> extract,
        CancellationToken cancellationToken)
        where TEnvelope : class
        where T : class
    {
        if (krs.IsEmpty)
        {
            throw new ArgumentException("The KRS number is empty.", nameof(krs));
        }

        KrsRegister[] registers = register is { } only ? [only] : [KrsRegister.Entrepreneurs, KrsRegister.Associations];
        foreach (var candidate in registers)
        {
            var uri = new Uri(_options.BaseAddress, "api/krs/" + kind + "/" + krs.ToString() + "?rejestr=" + RegisterCode(candidate) + "&format=json");
            using var request = HttpHelpers.CreateGet(uri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return new KrsResult<T>(krs, KrsLookupStatus.Removed, candidate, null);
            }

            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var (envelope, body) = await response.ReadJsonAsync(envelopeType, "KRS", cancellationToken).ConfigureAwait(false);
            var value = (envelope is null ? null : extract(envelope))
                ?? throw HttpHelpers.UnreadableResponse("KRS returned a response without an extract.", response, body);
            return new KrsResult<T>(krs, KrsLookupStatus.Found, candidate, value);
        }

        return new KrsResult<T>(krs, KrsLookupStatus.NotFound, null, null);
    }

    private async IAsyncEnumerable<KrsNumber> GetChangedCoreAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string[]? numbers;
        using (var request = HttpHelpers.CreateGet(new Uri(_options.BaseAddress, path)))
        using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.NoContent)
            {
                yield break;
            }

            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            (numbers, _) = await response.ReadJsonAsync(KrsJson.StringArray, "KRS", cancellationToken).ConfigureAwait(false);
        }

        var seen = new HashSet<KrsNumber>();
        foreach (var raw in numbers ?? [])
        {
            if (KrsNumber.TryParse(raw, out var krs) && seen.Add(krs))
            {
                yield return krs;
            }
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        if (status == 429)
        {
            throw new QuotaExceededException("KRS rate limit reached (HTTP 429).", response.StatusCode, null, null, HttpHelpers.GetRetryAfter(response));
        }

        if (status >= 500)
        {
            _ = response.EnsureSuccessStatusCode();
        }

        var body = await response.Content.ReadStringAsync(cancellationToken).ConfigureAwait(false);
        throw new PolishOpenDataApiException(
            "KRS request failed with HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".",
            response.StatusCode,
            null,
            HttpHelpers.Truncate(body));
    }

    private static string RegisterCode(KrsRegister register) => register == KrsRegister.Associations ? "S" : "P";

    private static string FormatDay(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
