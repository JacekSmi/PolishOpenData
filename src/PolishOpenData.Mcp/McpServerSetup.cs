using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol;
using PolishOpenData.BialaLista;
using PolishOpenData.Krs;

namespace PolishOpenData.Mcp;

internal static class McpServerSetup
{
    /// <summary>Registers the registry clients (with resilience and the Biała Lista quota guard) and the cache.</summary>
    public static IServiceCollection AddPolishOpenDataServices(this IServiceCollection services, Action<IHttpClientBuilder>? configureHttp = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        var krs = services.AddKrsClient();
        var vat = services.AddBialaListaClient(o => o.TrackQuota = true);
        krs.AddStandardResilienceHandler();
        vat.AddStandardResilienceHandler(o =>
        {
            // A retry is another upstream request the quota guard never counts, and 429 (WL-191) means the IP is
            // blocked until midnight: keep timeouts and the circuit breaker, but never retry Biała Lista.
            o.Retry.ShouldHandle = static _ => ValueTask.FromResult(false);
        });
        configureHttp?.Invoke(krs);
        configureHttp?.Invoke(vat);

        services.AddTransient<CachedRegistries>();
        return services;
    }

    /// <summary>Registers the tools (AOT-safe JSON first in the chain) and maps exceptions to exact isError text.</summary>
    public static IMcpServerBuilder AddPolishOpenDataTools(this IMcpServerBuilder builder)
    {
        var json = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        json.TypeInfoResolverChain.Insert(0, McpJsonContext.Default);
        json.MakeReadOnly();

        return builder
            .WithTools<CompanyTools>(json)
            .WithTools<IdentifierTools>(json)
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                try
                {
                    return await next(context, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not McpProtocolException && ToolErrors.Describe(ex) is { } message)
                {
                    return ToolResults.Error(message);
                }
            }));
    }
}
