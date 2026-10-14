using System;

namespace PolishOpenData.Uug;

/// <summary>Options for <see cref="UugClient"/>.</summary>
public sealed class UugClientOptions
{
    /// <summary>Service root. Default <c>https://services.gugik.gov.pl/uug/</c>.</summary>
    public Uri BaseAddress { get; set; } = new("https://services.gugik.gov.pl/uug/");
}
