using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace PolishOpenData.Rcn;

/// <summary>Dependency-injection registration for <see cref="IRcnClient"/>.</summary>
public static class RcnServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRcnClient"/> as a typed HttpClient limited to 4 parallel connections (the project default cap; GUGiK publishes no request limit).
    /// Returns the builder so callers can add resilience (e.g. <c>.AddStandardResilienceHandler()</c>).
    /// </summary>
    public static IHttpClientBuilder AddRcnClient(this IServiceCollection services, Action<RcnClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new RcnClientOptions();
        configure?.Invoke(options);
        return services
            .AddHttpClient<IRcnClient, RcnClient>((httpClient, _) => new RcnClient(httpClient, options))
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler { MaxConnectionsPerServer = 4 });
    }
}
