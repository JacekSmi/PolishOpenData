using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PolishOpenData.Bir;

/// <summary>Dependency-injection registration for <see cref="IRegonClient"/> and <see cref="IRegonSummaryReports"/>.</summary>
public static class RegonServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRegonClient"/> as a typed HttpClient (also resolvable as <see cref="IRegonSummaryReports"/>), a singleton GUS session, a singleton request limiter and
    /// <see cref="TimeProvider.System"/> (unless already registered). Returns the builder so callers can add a custom handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The typed client is transient, so the session and the limiter live in singletons: every resolved client shares one
    /// login and one request budget. Call this once per process. If it is called again, the singletons keep the settings of
    /// the first call (the session with its <see cref="RegonClientOptions.SessionLifetime"/>, the limiter with its
    /// <see cref="RegonClientOptions.RateLimit"/> and <see cref="RegonClientOptions.MaxRateLimitWait"/>), while the typed client
    /// is built with the options of the last call (the last registration wins). <paramref name="configure"/> runs immediately, and the options are read once when a client is
    /// resolved: a production client without <see cref="RegonClientOptions.UserKey"/> throws
    /// <see cref="InvalidOperationException"/> on the first resolve. The key comes from you (user secrets, a vault, an
    /// environment variable you choose); this library never reads the environment.
    /// </para>
    /// <para>
    /// Do not enable retries on the returned builder: every retry a resilience handler makes is another GUS request that the
    /// request limiter cannot count, and a SOAP fault, which means your request was wrong, arrives as HTTP 500 and would be
    /// retried too. If you add the standard resilience handler, disable retries explicitly:
    /// <code>services.AddRegonClient(...).AddStandardResilienceHandler(o =&gt; o.Retry.ShouldHandle = _ =&gt; new ValueTask&lt;bool&gt;(false));</code>
    /// The standard handler's default timeouts (10 seconds per attempt, 30 in total) also cut a summary report short
    /// (<see cref="IRegonSummaryReports"/>): answers of 8 seconds and a hang of 35 seconds were seen.
    /// </para>
    /// <para>
    /// The client never logs out by itself; call <see cref="IRegonClient.LogoutAsync"/> when the process stops.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the options, for example <see cref="RegonClientOptions.UserKey"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IHttpClientBuilder AddRegonClient(this IServiceCollection services, Action<RegonClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        // The typed client is transient, so the session and the limiter are singletons.
        var options = new RegonClientOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => new BirSession(sp.GetRequiredService<TimeProvider>(), options.SessionLifetime));
        services.TryAddSingleton(sp => new RegonRateLimiter(sp.GetRequiredService<TimeProvider>(), options.RateLimit, options.MaxRateLimitWait));
        var builder = services.AddHttpClient<IRegonClient, RegonClient>((httpClient, sp) =>
            new RegonClient(httpClient, options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<BirSession>(), sp.GetRequiredService<RegonRateLimiter>()));
        services.TryAddTransient<IRegonSummaryReports>(static sp => sp.GetRequiredService<IRegonClient>() as IRegonSummaryReports
            ?? throw new InvalidOperationException("IRegonClient was replaced or decorated; register IRegonSummaryReports yourself."));
        return builder;
    }
}
