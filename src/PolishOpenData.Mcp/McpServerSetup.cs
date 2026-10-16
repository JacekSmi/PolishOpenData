using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PolishOpenData.BialaLista;
using PolishOpenData.BialaLista.Offline;
using PolishOpenData.Bir;
using PolishOpenData.Krs;
using PolishOpenData.Rcn;
using PolishOpenData.Uldk;
using PolishOpenData.Uug;

namespace PolishOpenData.Mcp;

internal static class McpServerSetup
{
    /// <summary>Environment variable that overrides where the offline flat-file indexes are kept (default: the library's per-user folder).</summary>
    public const string OfflineDirectoryVariable = "POLISHOPENDATA_OFFLINE_DIR";

    /// <summary>Environment variable with the user's own GUS BIR key. The REGON tool exists only when it is set.</summary>
    public const string RegonKeyVariable = "POLISHOPENDATA_REGON_KEY";

    /// <summary>Registers the registry clients (with resilience and the Biała Lista quota guard) and the cache.</summary>
    public static IServiceCollection AddPolishOpenDataServices(this IServiceCollection services, Action<IHttpClientBuilder>? configureHttp = null, string? offlineDataDirectory = null, string? regonKey = null)
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
        var rcn = services.AddRcnClient();
        uldk.AddStandardResilienceHandler(RetryGugikOnce);
        uug.AddStandardResilienceHandler(RetryGugikOnce);
        rcn.AddStandardResilienceHandler(RetryGugikOnce);
        // No resilience handler: its timeouts would cut the 250 MB flat-file download (the library sets an infinite HttpClient timeout).
        var offlineDirectory = offlineDataDirectory ?? Environment.GetEnvironmentVariable(OfflineDirectoryVariable);
        var offline = services.AddVatWhitelistOffline(o =>
        {
            if (!string.IsNullOrWhiteSpace(offlineDirectory))
            {
                o.DataDirectory = offlineDirectory;
            }
        });
        // Opt-in: without the user's own GUS key there is no BIR client and no BIR traffic. Production only, never retried
        // (every request counts against GUS's limits and GUS logs its content).
        var key = (regonKey ?? Environment.GetEnvironmentVariable(RegonKeyVariable))?.Trim();
        if (!string.IsNullOrEmpty(key))
        {
            var regon = services.AddRegonClient(o => o.UserKey = key);
            regon.AddStandardResilienceHandler(o => o.Retry.ShouldHandle = static _ => ValueTask.FromResult(false));
            configureHttp?.Invoke(regon);
        }

        configureHttp?.Invoke(krs);
        configureHttp?.Invoke(vat);
        configureHttp?.Invoke(offline);
        configureHttp?.Invoke(uldk);
        configureHttp?.Invoke(uug);
        configureHttp?.Invoke(rcn);

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

        // The tool is registered exactly when AddPolishOpenDataServices found a key; the instructions follow it.
        var regonEnabled = builder.Services.Any(d => d.ServiceType == typeof(IRegonClient));
        builder.Services.Configure<McpServerOptions>(o => o.ServerInstructions = ServerInfo.InstructionsFor(regonEnabled));
        if (regonEnabled)
        {
            builder.WithTools<RegonTools>(json);
        }

        return builder
            .WithTools<CompanyTools>(json)
            .WithTools<OfflineVatTools>(json)
            .WithTools<IdentifierTools>(json)
            .WithTools<GeoTools>(json)
            .WithTools<PropertyTools>(json)
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
