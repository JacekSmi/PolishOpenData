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
    /// <remarks>
    /// Do not enable retries on the returned builder: each retry the resilience handler makes is another upstream
    /// request that the <see cref="BialaListaQuotaTracker"/> above cannot count, and once the daily limit is
    /// reached Biała Lista blocks the whole IP address until midnight. Disable retries explicitly:
    /// <code>services.AddBialaListaClient(...).AddStandardResilienceHandler(o =&gt; o.Retry.ShouldHandle = static _ =&gt; ValueTask.FromResult(false));</code>
    /// </remarks>
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
