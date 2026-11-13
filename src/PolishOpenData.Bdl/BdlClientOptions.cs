using System;

namespace PolishOpenData.Bdl;

/// <summary>Language of the names the API returns.</summary>
public enum BdlLanguage
{
    /// <summary>Polish (default).</summary>
    Pl,

    /// <summary>English, where GUS has translated the name.</summary>
    En,
}

/// <summary>Options for <c>BdlClient</c>.</summary>
public sealed class BdlClientOptions
{
    private static readonly Uri ProductionAddress = new("https://bdl.stat.gov.pl/api/v1/");

    /// <summary>
    /// API root. Default <c>https://bdl.stat.gov.pl/api/v1/</c>. Must be an absolute <c>https</c> URI.
    /// </summary>
    public Uri BaseAddress { get; set; } = ProductionAddress;

    /// <summary>
    /// Optional personal API key from the free BDL registration, sent as the <c>X-ClientId</c> header; it raises the rate limits
    /// (anonymous: 5 per second, 100 per 15 minutes, 1,000 per 12 hours, 10,000 per week). The library never reads environment
    /// variables. A wrong key is not rejected by the API: you silently get anonymous limits. The key is never logged, never put in
    /// a URL and never part of an exception message. Whitespace around it is removed.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>Language of returned names. Default Polish.</summary>
    public BdlLanguage Language { get; set; } = BdlLanguage.Pl;

    internal void Validate()
    {
        if (BaseAddress is null || !BaseAddress.IsAbsoluteUri || BaseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("BdlClientOptions.BaseAddress must be an absolute https URI.", nameof(BaseAddress));
        }
    }

    internal Uri ResolveBaseAddress()
    {
        var text = BaseAddress.AbsoluteUri;
        return text.EndsWith('/') ? BaseAddress : new Uri(text + "/");
    }

    internal string? ResolveClientId()
    {
        var id = ClientId?.Trim();
        if (id is null || id.Length == 0)
        {
            return null;
        }

        foreach (var c in id)
        {
            if (c < '!' || c > '~')
            {
                throw new InvalidOperationException("BdlClientOptions.ClientId must be printable ASCII without whitespace.");
            }
        }

        return id;
    }
}
