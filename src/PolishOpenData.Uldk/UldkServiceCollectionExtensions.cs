using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace PolishOpenData.Uldk;

/// <summary>Dependency-injection registration for <see cref="IUldkClient"/>.</summary>
public static class UldkServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUldkClient"/> as a typed HttpClient limited to 4 parallel connections (the project's default cap; GUGiK publishes no request limit).
    /// Returns the builder so callers can add resilience (e.g. <c>.AddStandardResilienceHandler()</c>).
    /// </summary>
    public static IHttpClientBuilder AddUldkClient(this IServiceCollection services, Action<UldkClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new UldkClientOptions();
        configure?.Invoke(options);
        return services
            .AddHttpClient<IUldkClient, UldkClient>((httpClient, _) => new UldkClient(httpClient, options))
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler { MaxConnectionsPerServer = 4 });
    }
}
