using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PolishOpenData.Mcp;

namespace PolishOpenData.Tests.Shared;

/// <summary>
/// A host running the real MCP server wiring (<c>AddPolishOpenDataServices</c> + <c>AddMcpServer</c> +
/// <c>AddPolishOpenDataTools</c>), connected to a client over an in-memory pipe pair. Compiled into
/// PolishOpenData.Mcp.Tests (stubbed HTTP) and linked into PolishOpenData.SmokeTests (real HTTP).
/// </summary>
internal sealed class McpTestSession : IAsyncDisposable
{
    private readonly IHost _host;

    private McpTestSession(IHost host, McpClient client)
    {
        _host = host;
        Client = client;
    }

    public McpClient Client { get; }

    /// <summary>Starts a session.</summary>
    /// <param name="configureHttp">
    /// Applied to both registry HTTP clients: a stub primary handler in the unit tests, a timeout in the live tests.
    /// Required, so that real registry calls happen only where a test asks for them (the gated live tests).
    /// </param>
    /// <param name="clock">Registered as the server's <see cref="TimeProvider"/>; null keeps the production default (system clock).</param>
    /// <param name="cancellationToken">Cancels start-up and the MCP handshake.</param>
    public static async Task<McpTestSession> StartAsync(Action<IHttpClientBuilder> configureHttp, TimeProvider? clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configureHttp);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (clock is not null)
        {
            builder.Services.AddSingleton(clock);
        }

        builder.Services.AddPolishOpenDataServices(configureHttp);

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        builder.Services
            .AddMcpServer(o => o.ServerInfo = new Implementation { Name = "polish-open-data-tests", Version = "0.0.0" })
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .AddPolishOpenDataTools();

        var host = builder.Build();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        var clientTransport = new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream());
        var client = await McpClient.CreateAsync(clientTransport, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new McpTestSession(host, client);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync().ConfigureAwait(false);
        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();
    }
}
