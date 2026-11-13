using System;
using Microsoft.Extensions.DependencyInjection;

namespace PolishOpenData.Ceidg;

/// <summary>Dependency-injection registration for <see cref="ICeidgClient"/>.</summary>
public static class CeidgServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICeidgClient"/> as a typed HttpClient. Returns the builder so callers can add resilience or a
    /// custom primary handler. Do not enable retries: the API allows 1,000 requests per 60 minutes per token, and a retry
    /// is one more request against that limit. Set <see cref="CeidgClientOptions.Token"/> in <paramref name="configure"/>.
    /// </summary>
    public static IHttpClientBuilder AddCeidgClient(this IServiceCollection services, Action<CeidgClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new CeidgClientOptions();
        configure?.Invoke(options);
        options.Validate();
        return services.AddHttpClient<ICeidgClient, CeidgClient>((httpClient, _) => new CeidgClient(httpClient, options));
    }
}
