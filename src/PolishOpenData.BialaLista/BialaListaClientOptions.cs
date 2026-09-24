using System;

namespace PolishOpenData.BialaLista;

/// <summary>Options for <see cref="BialaListaClient"/>.</summary>
public sealed class BialaListaClientOptions
{
    /// <summary>API root. Default <c>https://wl-api.mf.gov.pl/</c> (test environment: <c>https://wl-test.mf.gov.pl/</c>).</summary>
    public Uri BaseAddress { get; set; } = new("https://wl-api.mf.gov.pl/");

    /// <summary>Count requests per Warsaw day and refuse locally before the upstream limit would block the IP. Default false.</summary>
    public bool TrackQuota { get; set; }

    /// <summary>Daily search limit used by the local guard. Default 100.</summary>
    public int SearchLimitPerDay { get; set; } = 100;

    /// <summary>Daily check limit used by the local guard. Default 5000.</summary>
    public int CheckLimitPerDay { get; set; } = 5000;
}
