using System;
using Microsoft.Extensions.DependencyInjection;

namespace PolishOpenData.Bdl;

/// <summary>Dependency-injection registration for <see cref="IBdlClient"/>.</summary>
public static class BdlServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IBdlClient"/> as a typed HttpClient. Returns the builder so callers can add resilience or a
    /// custom primary handler. Do not enable retries: anonymous use allows 100 requests per 15 minutes and a retry is one more
    /// request against that limit. Set <see cref="BdlClientOptions.ClientId"/> in <paramref name="configure"/> to use your own
    /// free key (higher limits); no key is needed.
    /// </summary>
    public static IHttpClientBuilder AddBdlClient(this IServiceCollection services, Action<BdlClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new BdlClientOptions();
        configure?.Invoke(options);
        options.Validate();
        return services.AddHttpClient<IBdlClient, BdlClient>((httpClient, _) => new BdlClient(httpClient, options));
    }
}
