using System;
using Microsoft.Extensions.DependencyInjection;

namespace PolishOpenData.Eli;

/// <summary>Dependency-injection registration for <see cref="IEliClient"/>.</summary>
public static class EliServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IEliClient"/> as a typed HttpClient. Returns the builder so callers can add resilience or a custom primary
    /// handler. No key is needed and no rate limit is published. Do not retry blindly: the client makes one request per call (an
    /// article costs two, the current-text check up to six).
    /// </summary>
    public static IHttpClientBuilder AddEliClient(this IServiceCollection services, Action<EliClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new EliClientOptions();
        configure?.Invoke(options);
        options.Validate();
        return services.AddHttpClient<IEliClient, EliClient>((httpClient, serviceProvider) =>
            new EliClient(httpClient, options, (TimeProvider?)serviceProvider.GetService(typeof(TimeProvider))));
    }
}
