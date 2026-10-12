using System;

namespace PolishOpenData.Bir;

/// <summary>
/// GUS did not accept the configured user key: the login answered with an empty session id. The key is wrong,
/// revoked or not meant for this environment. The client does not retry, so stop sending requests when you see this.
/// The message never contains the key.
/// </summary>
public sealed class RegonAuthenticationException : PolishOpenDataApiException
{
    /// <summary>Creates an exception.</summary>
    public RegonAuthenticationException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public RegonAuthenticationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public RegonAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
