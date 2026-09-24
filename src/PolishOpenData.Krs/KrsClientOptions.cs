using System;

namespace PolishOpenData.Krs;

/// <summary>Options for <see cref="KrsClient"/>.</summary>
public sealed class KrsClientOptions
{
    /// <summary>API root. Default <c>https://api-krs.ms.gov.pl/</c>.</summary>
    public Uri BaseAddress { get; set; } = new("https://api-krs.ms.gov.pl/");
}
