using System;

namespace PolishOpenData.Rcn;

/// <summary>Options for <see cref="RcnClient"/>.</summary>
public sealed class RcnClientOptions
{
    /// <summary>Service endpoint. Default <c>https://mapy.geoportal.gov.pl/wss/service/rcn</c>.</summary>
    public Uri BaseAddress { get; set; } = new("https://mapy.geoportal.gov.pl/wss/service/rcn");

    /// <summary>Rows asked for per request. Default 500; must be at least 1.</summary>
    public int PageSize { get; set; } = 500;
}
