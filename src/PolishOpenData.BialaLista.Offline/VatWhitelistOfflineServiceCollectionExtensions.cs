using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>Dependency-injection registration for <see cref="IVatWhitelistOffline"/>.</summary>
public static class VatWhitelistOfflineServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IVatWhitelistOffline"/> as a typed HttpClient, a singleton store of the prepared indexes for
    /// <see cref="VatWhitelistOfflineOptions.DataDirectory"/> and <see cref="TimeProvider.System"/> (unless already
    /// registered). Returns the builder so callers can add a custom handler.
    /// </summary>
    /// <remarks>
    /// The typed client is transient, so the store lives in a singleton: every resolved client shares one index per date and
    /// one download. The HttpClient's timeout is set to <see cref="Timeout.InfiniteTimeSpan"/> because the flat file is
    /// about 250 MB and the server can be slow; cancel through the call's <see cref="CancellationToken"/>. Retries on the
    /// builder are fine (this server has no request quota). Call this once per process; the options are read when the
    /// registration is made, and a second call keeps the store of the first.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IHttpClientBuilder AddVatWhitelistOffline(this IServiceCollection services, Action<VatWhitelistOfflineOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new VatWhitelistOfflineOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => new OfflineIndexStore(options.ResolveDataDirectory(), options.DatesToKeep, options.LockTimeout, sp.GetRequiredService<TimeProvider>()));
        return services.AddHttpClient<IVatWhitelistOffline, VatWhitelistOffline>((httpClient, sp) =>
                new VatWhitelistOffline(httpClient, options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<OfflineIndexStore>()))
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
    }
}
