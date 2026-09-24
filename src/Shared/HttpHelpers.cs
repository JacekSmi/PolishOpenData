using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Internal;

/// <summary>HttpClient helpers shared by the registry clients.</summary>
internal static class HttpHelpers
{
    /// <summary>Creates a GET request with the project User-Agent and <c>Accept: application/json</c>.</summary>
    public static HttpRequestMessage CreateGet(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent.Value);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    /// <summary>Reads the body as a stream (cancellable on .NET, best effort on netstandard2.0).</summary>
    public static Task<Stream> ReadStreamAsync(this HttpContent content, CancellationToken cancellationToken)
    {
#if NET
        return content.ReadAsStreamAsync(cancellationToken);
#else
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStreamAsync();
#endif
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

    /// <summary>Cuts text to at most <paramref name="max"/> characters.</summary>
    public static string? Truncate(string? text, int max = 512) =>
        text is null || text.Length <= max ? text : text.Substring(0, max);

    /// <summary>The Retry-After delay, if the response carries a delta value.</summary>
    public static TimeSpan? GetRetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter?.Delta;
}
