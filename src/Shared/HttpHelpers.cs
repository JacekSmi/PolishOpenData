using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Internal;

/// <summary>HttpClient helpers shared by the registry clients.</summary>
internal static class HttpHelpers
{
    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>Creates a GET request with the project User-Agent and <c>Accept: application/json</c>.</summary>
    public static HttpRequestMessage CreateGet(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent.Value);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    /// <summary>Reads the body as a string (cancellable on .NET, best effort on netstandard2.0).</summary>
    public static Task<string> ReadStringAsync(this HttpContent content, CancellationToken cancellationToken)
    {
#if NET
        return content.ReadAsStringAsync(cancellationToken);
#else
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStringAsync();
#endif
    }

    /// <summary>Reads the whole body; the token is checked between chunks, so a slow body can be cancelled.</summary>
    public static async Task<byte[]> ReadBytesAsync(this HttpContent content, CancellationToken cancellationToken)
    {
#if NET
        return await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
        // ReadAsByteArrayAsync takes no token here. Requests use ResponseHeadersRead, so on .NET Framework no
        // HttpClient or resilience timeout covers the body: copy the stream, which passes the token to every read.
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
#endif
    }

    /// <summary>
    /// Reads the body but stops once it is known to be longer than <paramref name="maxBytes"/>: the result then holds the
    /// first <c>maxBytes + 1</c> bytes (so <c>Length &gt; maxBytes</c> tells the caller the cap was hit) and the rest of the
    /// stream is never read. Unlike <see cref="ReadBytesAsync(HttpContent, CancellationToken)"/> this bounds memory for a
    /// body with no Content-Length. The token is checked between chunks.
    /// </summary>
    public static async Task<byte[]> ReadBytesAsync(this HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if NET
        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        var limit = (long)maxBytes + 1;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (buffer.Length < limit)
        {
            var wanted = (int)Math.Min(chunk.Length, limit - buffer.Length);
#if NET
            var read = await stream.ReadAsync(chunk.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
#else
            var read = await stream.ReadAsync(chunk, 0, wanted, cancellationToken).ConfigureAwait(false);
#endif
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Deserialises a success response. The body is buffered so that, when it is not the expected JSON, the
    /// <see cref="JsonException"/> becomes a <see cref="PolishOpenDataApiException"/> carrying the status code and the
    /// start of the body. Returns the body too, for callers that reject a well-formed but empty answer.
    /// </summary>
    public static async Task<(T? Value, byte[] Body)> ReadJsonAsync<T>(this HttpResponseMessage response, JsonTypeInfo<T> typeInfo, string registry, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadBytesAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // a stream (not a span) so that a UTF-8 byte order mark is skipped, as before buffering
            using var stream = new MemoryStream(body, writable: false);
            return (JsonSerializer.Deserialize(stream, typeInfo), body);
        }
        catch (JsonException exception)
        {
            var message = registry + " returned a response that could not be read (HTTP " +
                ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + "): " + exception.Message;
            if (exception.Path is { } path && exception.Message.IndexOf("Path:", StringComparison.Ordinal) < 0)
            {
                message += " Path: " + path + ".";
            }

            throw UnreadableResponse(message, response, body, exception);
        }
    }

    /// <summary>
    /// Reads a success response's body and rejects the F5 block page, which answers 200 with HTML (a <c>text/html</c>
    /// content type or a body starting with <c>&lt;</c>): <see cref="PolishOpenDataApiException"/> with code <c>blocked</c>, transient.
    /// </summary>
    public static async Task<byte[]> ReadBodyRejectingBlockPageAsync(this HttpResponseMessage response, string service, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadBytesAsync(cancellationToken).ConfigureAwait(false);
        var text = Snippet(body);
        if (response.Content.Headers.ContentType?.MediaType == "text/html" || text.TrimStart().StartsWith('<'))
        {
            throw new PolishOpenDataApiException(service + " blocked the request (HTML block page).", response.StatusCode, "blocked", text, isTransient: true);
        }

        return body;
    }

    /// <summary>
    /// Throws for a non-success response: 429 is <see cref="QuotaExceededException"/>, 5xx an <see cref="HttpRequestException"/>
    /// (transient), anything else a <see cref="PolishOpenDataApiException"/> carrying the start of the body.
    /// </summary>
    public static async Task ThrowForStatusAsync(this HttpResponseMessage response, string service, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (status == 429)
        {
            throw new QuotaExceededException(service + " rate limit reached (HTTP 429).", response.StatusCode, null, null, GetRetryAfter(response));
        }

        if (status >= 500)
        {
            _ = response.EnsureSuccessStatusCode();
        }

        var body = await response.Content.ReadStringAsync(cancellationToken).ConfigureAwait(false);
        throw new PolishOpenDataApiException(
            service + " request failed with HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".",
            response.StatusCode,
            null,
            Truncate(body));
    }

    /// <summary>An error for a success response whose body the library cannot use.</summary>
    public static PolishOpenDataApiException UnreadableResponse(string message, HttpResponseMessage response, byte[] body, Exception? innerException = null) =>
        new(message, response.StatusCode, null, Snippet(body), innerException);

    /// <summary>Cuts text to at most <paramref name="max"/> characters.</summary>
    public static string? Truncate(string? text, int max = 512) =>
        text is null || text.Length <= max ? text : text.Substring(0, max);

    /// <summary>The start of a UTF-8 body as text, at most <paramref name="max"/> characters, without a byte order mark.</summary>
    public static string Snippet(byte[] body, int max = 512)
    {
        // at most 3 bytes per UTF-16 character, so 4 * max bytes always decode to more than max whole characters
        var text = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 4 * max));
        if (text.Length > 0 && text[0] == ByteOrderMark)
        {
            text = text.Substring(1);
        }

        return Truncate(text, max)!;
    }

    /// <summary>The Retry-After delay, if the response carries a delta value.</summary>
    public static TimeSpan? GetRetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter?.Delta;
}
