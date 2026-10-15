using System;

namespace PolishOpenData.Uldk;

/// <summary>Options for <see cref="UldkClient"/>.</summary>
public sealed class UldkClientOptions
{
    /// <summary>Service root. Default <c>https://uldk.gugik.gov.pl/</c>.</summary>
    public Uri BaseAddress { get; set; } = new("https://uldk.gugik.gov.pl/");
}
