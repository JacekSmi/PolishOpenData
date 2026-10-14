using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace PolishOpenData.Uug;

/// <summary>Dependency-injection registration for <see cref="IUugClient"/>.</summary>
public static class UugServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IUugClient"/> as a typed HttpClient limited to 4 parallel connections.
    /// Returns the builder so callers can add resilience (e.g. <c>.AddStandardResilienceHandler()</c>).
    /// </summary>
    public static IHttpClientBuilder AddUugClient(this IServiceCollection services, Action<UugClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new UugClientOptions();
        configure?.Invoke(options);
        return services
            .AddHttpClient<IUugClient, UugClient>((httpClient, _) => new UugClient(httpClient, options))
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler { MaxConnectionsPerServer = 4 });
    }
}
