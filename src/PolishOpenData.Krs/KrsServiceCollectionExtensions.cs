using System;
using Microsoft.Extensions.DependencyInjection;

namespace PolishOpenData.Krs;

/// <summary>Dependency-injection registration for <see cref="IKrsClient"/>.</summary>
public static class KrsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IKrsClient"/> as a typed HttpClient. Returns the builder so callers can add resilience
    /// (e.g. <c>.AddStandardResilienceHandler()</c>) or a custom primary handler.
    /// </summary>
    public static IHttpClientBuilder AddKrsClient(this IServiceCollection services, Action<KrsClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new KrsClientOptions();
        configure?.Invoke(options);
        return services.AddHttpClient<IKrsClient, KrsClient>((httpClient, _) => new KrsClient(httpClient, options));
    }
}
