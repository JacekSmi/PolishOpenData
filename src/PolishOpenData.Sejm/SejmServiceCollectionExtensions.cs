using System;
using Microsoft.Extensions.DependencyInjection;

namespace PolishOpenData.Sejm;

/// <summary>Dependency-injection registration for <see cref="ISejmClient"/>.</summary>
public static class SejmServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISejmClient"/> as a typed HttpClient. Returns the builder so callers can add resilience or a custom primary
    /// handler. No key is needed and no rate limit is published. Do not retry blindly: the client makes one request per call (a voting
    /// answer is 45 to 90 KB).
    /// </summary>
    public static IHttpClientBuilder AddSejmClient(this IServiceCollection services, Action<SejmClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new SejmClientOptions();
        configure?.Invoke(options);
        options.Validate();
        return services.AddHttpClient<ISejmClient, SejmClient>((httpClient, _) => new SejmClient(httpClient, options));
    }
}
