using System;

namespace PolishOpenData.Eli;

/// <summary>Options for <c>EliClient</c>.</summary>
public sealed class EliClientOptions
{
    private static readonly Uri ProductionAddress = new("https://api.sejm.gov.pl/eli/");

    /// <summary>API root. Default <c>https://api.sejm.gov.pl/eli/</c>. Must be an absolute <c>https</c> URI. No key is needed.</summary>
    public Uri BaseAddress { get; set; } = ProductionAddress;

    internal void Validate()
    {
        if (BaseAddress is null || !BaseAddress.IsAbsoluteUri || BaseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("EliClientOptions.BaseAddress must be an absolute https URI.", nameof(BaseAddress));
        }
    }

    internal Uri ResolveBaseAddress()
    {
        var text = BaseAddress.AbsoluteUri;
        return text.EndsWith('/') ? BaseAddress : new Uri(text + "/");
    }
}
