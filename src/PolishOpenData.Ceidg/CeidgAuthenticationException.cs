using System;
using System.Net;

namespace PolishOpenData.Ceidg;

/// <summary>
/// CEIDG did not accept the token (HTTP 401 or 403): it is wrong, expired, revoked or not allowed to read the resource.
/// The client does not retry, so stop sending requests when you see this. The message never contains the token.
/// </summary>
public sealed class CeidgAuthenticationException : PolishOpenDataApiException
{
    /// <summary>Creates an exception.</summary>
    public CeidgAuthenticationException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public CeidgAuthenticationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public CeidgAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal CeidgAuthenticationException(string message, HttpStatusCode statusCode, string? responseSnippet)
        : base(message, statusCode, null, responseSnippet)
    {
    }
}
