using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PolishOpenData.BialaLista;

/// <summary>Dependency-injection registration for <see cref="IBialaListaClient"/>.</summary>
public static class BialaListaServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IBialaListaClient"/> as a typed HttpClient, a singleton <see cref="BialaListaQuotaTracker"/>
    /// and <see cref="TimeProvider.System"/> (unless already registered). Returns the builder so callers can add
    /// resilience or a custom handler.
    /// </summary>
    public static IHttpClientBuilder AddBialaListaClient(this IServiceCollection services, Action<BialaListaClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new BialaListaClientOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => new BialaListaQuotaTracker(sp.GetRequiredService<TimeProvider>()));
        return services.AddHttpClient<IBialaListaClient, BialaListaClient>((httpClient, sp) =>
            new BialaListaClient(httpClient, options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<BialaListaQuotaTracker>()));
    }
}
