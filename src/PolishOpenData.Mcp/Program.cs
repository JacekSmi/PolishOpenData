using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using PolishOpenData.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries JSON-RPC; everything else goes to stderr, and only warnings and errors.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddPolishOpenDataServices();

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new Implementation { Name = "polish-open-data", Title = "Polish Open Data (unofficial)", Version = ServerInfo.Version };
        o.ServerInstructions = ServerInfo.Instructions;
    })
    .WithStdioServerTransport()
    .AddPolishOpenDataTools();

await builder.Build().RunAsync();
