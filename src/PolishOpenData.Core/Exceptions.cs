using System;
using System.Net;

namespace PolishOpenData;

/// <summary>Base type of every exception thrown by PolishOpenData libraries.</summary>
public class PolishOpenDataException : Exception
{
    /// <summary>Creates an exception.</summary>
    public PolishOpenDataException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public PolishOpenDataException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public PolishOpenDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// An upstream registry answered with a semantic error (a 4xx response or an error body). Transport failures and 5xx
/// responses are reported as <see cref="System.Net.Http.HttpRequestException"/> instead, so standard resilience
/// handlers keep working.
/// </summary>
public class PolishOpenDataApiException : PolishOpenDataException
{
    /// <summary>Creates an exception.</summary>
    public PolishOpenDataApiException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public PolishOpenDataApiException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public PolishOpenDataApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception describing an upstream error.</summary>
    /// <param name="message">Human-readable description.</param>
    /// <param name="statusCode">HTTP status of the response; <c>null</c> when the error was detected locally.</param>
    /// <param name="errorCode">Upstream error code such as <c>WL-115</c>, when the body carried one.</param>
    /// <param name="responseSnippet">The start of the response body, for diagnostics.</param>
    /// <param name="isTransient">True when the upstream said the request may succeed if repeated shortly.</param>
    public PolishOpenDataApiException(string message, HttpStatusCode? statusCode, string? errorCode, string? responseSnippet, bool isTransient = false)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseSnippet = responseSnippet;
        IsTransient = isTransient;
    }

    /// <summary>HTTP status of the response; <c>null</c> when the error was detected locally.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Upstream error code such as <c>WL-115</c>, when the body carried one.</summary>
    public string? ErrorCode { get; }

    /// <summary>The start of the response body (at most 512 characters).</summary>
    public string? ResponseSnippet { get; }

    /// <summary>True when the upstream said the request may succeed if repeated shortly (e.g. Biała Lista WL-195/WL-196).</summary>
    public bool IsTransient { get; }
}

/// <summary>A request limit was reached, either reported by the upstream or predicted by a local quota guard.</summary>
public sealed class QuotaExceededException : PolishOpenDataApiException
{
    /// <summary>Creates an exception.</summary>
    public QuotaExceededException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public QuotaExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public QuotaExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception describing a reached limit.</summary>
    /// <param name="message">Human-readable description.</param>
    /// <param name="statusCode">HTTP status (usually 429); <c>null</c> when a local guard stopped the request.</param>
    /// <param name="errorCode">Upstream error code such as <c>WL-191</c>.</param>
    /// <param name="resetsAt">When the limit resets (UTC), if known.</param>
    /// <param name="retryAfter">The upstream's Retry-After delay, if sent.</param>
    public QuotaExceededException(string message, HttpStatusCode? statusCode, string? errorCode, DateTimeOffset? resetsAt, TimeSpan? retryAfter)
        : base(message, statusCode, errorCode, null)
    {
        ResetsAt = resetsAt;
        RetryAfter = retryAfter;
    }

    /// <summary>When the limit resets (UTC), if known. For Biała Lista this is the next midnight in Europe/Warsaw.</summary>
    public DateTimeOffset? ResetsAt { get; }

    /// <summary>The upstream's Retry-After delay, if sent.</summary>
    public TimeSpan? RetryAfter { get; }
}
