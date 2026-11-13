using System;

namespace PolishOpenData.Ceidg;

/// <summary>Options for <see cref="CeidgClient"/>.</summary>
public sealed class CeidgClientOptions
{
    private static readonly Uri ProductionAddress = new("https://dane.biznes.gov.pl/api/ceidg/v3/");

    /// <summary>
    /// Your own CEIDG API token (a JWT). There is no default, and the library never reads environment variables: pass the
    /// token from user secrets, a vault or a variable of your choice. It is sent only as the <c>Authorization: Bearer</c>
    /// header; it is never logged, never put in a URL, never part of an exception message and never printed by
    /// <c>ToString()</c>. Required: a call without it throws <see cref="InvalidOperationException"/> that names this
    /// property. Whitespace around the token (a trailing line break from a secret store, for example) is removed.
    /// The token is personal (it identifies the person who applied for it): keep it on a server, never in a browser or an app you distribute.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// API root. Default <c>https://dane.biznes.gov.pl/api/ceidg/v3/</c>; the Ministry's test service is
    /// <c>https://test-dane.biznes.gov.pl/api/ceidg/v3/</c>. Must be an absolute <c>https</c> URI, because the token travels in
    /// every request. The API allows 1,000 requests per 60 minutes per token; the client does not limit itself and does not retry.
    /// </summary>
    public Uri BaseAddress { get; set; } = ProductionAddress;

    internal void Validate()
    {
        if (BaseAddress is null || !BaseAddress.IsAbsoluteUri || BaseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("CeidgClientOptions.BaseAddress must be an absolute https URI.", nameof(BaseAddress));
        }
    }

    internal Uri ResolveBaseAddress()
    {
        var text = BaseAddress.AbsoluteUri;
        return text.EndsWith('/') ? BaseAddress : new Uri(text + "/");
    }

    internal string ResolveToken()
    {
        var token = Token?.Trim();
        if (token is null || token.Length == 0)
        {
            throw new InvalidOperationException("CeidgClientOptions.Token is required: pass your CEIDG API token.");
        }

        foreach (var c in token)
        {
            if (c < '!' || c > '~')
            {
                throw new InvalidOperationException("CeidgClientOptions.Token must be printable ASCII without whitespace.");
            }
        }

        return token;
    }
}
