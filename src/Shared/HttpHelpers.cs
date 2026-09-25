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

    /// <summary>Reads the whole body (cancellable on .NET, best effort on netstandard2.0).</summary>
    public static Task<byte[]> ReadBytesAsync(this HttpContent content, CancellationToken cancellationToken)
    {
#if NET
        return content.ReadAsByteArrayAsync(cancellationToken);
#else
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsByteArrayAsync();
#endif
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

            throw UnreadableResponse(message, response, body);
        }
    }

    /// <summary>An error for a success response whose body the library cannot use.</summary>
    public static PolishOpenDataApiException UnreadableResponse(string message, HttpResponseMessage response, byte[] body) =>
        new(message, response.StatusCode, null, Snippet(body));

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
