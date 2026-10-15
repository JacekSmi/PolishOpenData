using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using ModelContextProtocol;
using PolishOpenData.BialaLista;
using PolishOpenData.BialaLista.Offline;
using PolishOpenData.Krs;
using PolishOpenData.Uldk;
using PolishOpenData.Uug;

namespace PolishOpenData.Mcp;

internal static class McpServerSetup
{
    /// <summary>Environment variable that overrides where the offline flat-file indexes are kept (default: the library's per-user folder).</summary>
    public const string OfflineDirectoryVariable = "POLISHOPENDATA_OFFLINE_DIR";

    /// <summary>Registers the registry clients (with resilience and the Biała Lista quota guard) and the cache.</summary>
    public static IServiceCollection AddPolishOpenDataServices(this IServiceCollection services, Action<IHttpClientBuilder>? configureHttp = null, string? offlineDataDirectory = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.TryAddSingleton<InFlightCalls>();

        var krs = services.AddKrsClient();
        var vat = services.AddBialaListaClient(o => o.TrackQuota = true);
        krs.AddStandardResilienceHandler();
        vat.AddStandardResilienceHandler(o =>
        {
            // A retry is another upstream request the quota guard never counts, and 429 (WL-191) means the IP is
            // blocked until midnight: keep timeouts and the circuit breaker, but never retry Biała Lista.
            o.Retry.ShouldHandle = static _ => ValueTask.FromResult(false);
        });
        // One retry for the GUGiK services (plain GETs with no request counter to protect); the libraries themselves never retry.
        // A transient failure (5xx, 408, timeout) is retried, but never 429: GUGiK asked us to slow down.
        var uldk = services.AddUldkClient();
        var uug = services.AddUugClient();
        uldk.AddStandardResilienceHandler(RetryGugikOnce);
        uug.AddStandardResilienceHandler(RetryGugikOnce);
        // No resilience handler: its timeouts would cut the 250 MB flat-file download (the library sets an infinite HttpClient timeout).
        var offlineDirectory = offlineDataDirectory ?? Environment.GetEnvironmentVariable(OfflineDirectoryVariable);
        var offline = services.AddVatWhitelistOffline(o =>
        {
            if (!string.IsNullOrWhiteSpace(offlineDirectory))
            {
                o.DataDirectory = offlineDirectory;
            }
        });
        configureHttp?.Invoke(krs);
        configureHttp?.Invoke(vat);
        configureHttp?.Invoke(offline);
        configureHttp?.Invoke(uldk);
        configureHttp?.Invoke(uug);

        services.AddTransient<CachedRegistries>();
        return services;
    }

    private static void RetryGugikOnce(HttpStandardResilienceOptions options)
    {
        options.Retry.MaxRetryAttempts = 1;
#pragma warning disable EXTEXP0001 // the overload that takes the call's cancellation token is experimental
        options.Retry.ShouldHandle = static args => ValueTask.FromResult(
            HttpClientResiliencePredicates.IsTransient(args.Outcome, args.Context.CancellationToken) && args.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests);
#pragma warning restore EXTEXP0001
    }

    /// <summary>Registers the tools (AOT-safe JSON first in the chain) and maps exceptions to exact isError text.</summary>
    public static IMcpServerBuilder AddPolishOpenDataTools(this IMcpServerBuilder builder)
    {
        var json = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        json.TypeInfoResolverChain.Insert(0, McpJsonContext.Default);
        json.MakeReadOnly();

        return builder
            .WithTools<CompanyTools>(json)
            .WithTools<OfflineVatTools>(json)
            .WithTools<IdentifierTools>(json)
            .WithTools<GeoTools>(json)
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                try
                {
                    return await next(context, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not McpProtocolException && ToolErrors.Describe(ex, context.Params?.Name) is { } message)
                {
                    return ToolResults.Error(message);
                }
            }));
    }
}
