using System;

namespace PolishOpenData.Sejm;

/// <summary>Options for <c>SejmClient</c>.</summary>
public sealed class SejmClientOptions
{
    private static readonly Uri ProductionAddress = new("https://api.sejm.gov.pl/sejm/");

    /// <summary>API root. Default <c>https://api.sejm.gov.pl/sejm/</c>. Must be an absolute <c>https</c> URI. No key is needed.</summary>
    public Uri BaseAddress { get; set; } = ProductionAddress;

    internal void Validate()
    {
        if (BaseAddress is null || !BaseAddress.IsAbsoluteUri || BaseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("SejmClientOptions.BaseAddress must be an absolute https URI.", nameof(BaseAddress));
        }
    }

    internal Uri ResolveBaseAddress()
    {
        var text = BaseAddress.AbsoluteUri;
        return text.EndsWith('/') ? BaseAddress : new Uri(text + "/");
    }
}
