using System;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;

namespace PolishOpenData.Mcp;

internal static class McpServerSetup
{
    /// <summary>Registers the tools (AOT-safe JSON first in the chain) and maps exceptions to exact isError text.</summary>
    public static IMcpServerBuilder AddPolishOpenDataTools(this IMcpServerBuilder builder)
    {
        var json = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        json.TypeInfoResolverChain.Insert(0, McpJsonContext.Default);
        json.MakeReadOnly();

        return builder
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
