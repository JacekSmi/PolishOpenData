using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// The package users install: starts <c>dotnet dnx PolishOpenData.Mcp --yes</c> from nuget.org as an MCP client would
/// and talks to it over stdio. <c>POLISHOPENDATA_SMOKE_MCP_VERSION</c> pins a version (default: the latest).
/// </summary>
public sealed class PublishedMcpPackageTests
{
    private const string VersionVariable = "POLISHOPENDATA_SMOKE_MCP_VERSION";

    // Room for the first run, which downloads the package and its dependencies.
    private const int PackageTestTimeout = 300_000;

    private static readonly string[] ExpectedTools = ["lookup_company", "check_vat_bank_account", "get_krs_extract", "validate_identifier"];

    [Fact(Timeout = PackageTestTimeout)]
    public async Task Published_package_lists_the_tools_and_answers_a_live_lookup()
    {
        Live.SkipUnlessEnabled();   // before anything starts dnx
        var version = Environment.GetEnvironmentVariable(VersionVariable)?.Trim();
        var package = string.IsNullOrEmpty(version) ? "PolishOpenData.Mcp" : "PolishOpenData.Mcp@" + version;

        var stderr = new ConcurrentQueue<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = package + " (dnx)",
            Command = "dotnet",
            Arguments = ["dnx", package, "--yes"],

            // Outside the repository, so its global.json and build files play no part, as for a user's MCP client.
            WorkingDirectory = Path.GetTempPath(),
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["DOTNET_NOLOGO"] = "1",
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            },
            StandardErrorLines = stderr.Enqueue,
        });
        var options = new McpClientOptions
        {
            // The initialize handshake (as in build/mcp-stdio-check.py). Left unset, the client first probes
            // server/discover with a 5-second timeout, which can run out while dnx is still downloading.
            ProtocolVersion = "2025-11-25",
            InitializationTimeout = TimeSpan.FromMinutes(3),
        };

        try
        {
            await using var client = await McpClient.CreateAsync(transport, options, cancellationToken: TestContext.Current.CancellationToken);
            if (!string.IsNullOrEmpty(version))
            {
                Assert.Equal(version, client.ServerInfo.Version);
            }

            var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            foreach (var name in ExpectedTools)
            {
                Assert.Contains(tools, t => string.Equals(t.Name, name, StringComparison.Ordinal));
            }

            var result = await client.CallToolAsync(
                "lookup_company",
                new Dictionary<string, object?> { ["nip"] = Live.OrlenNip },
                cancellationToken: TestContext.Current.CancellationToken);
            Live.SkipIfQuotaError(result);
            Assert.False(result.IsError == true, "lookup_company returned isError: " + Live.Text(result));
            using var company = JsonDocument.Parse(Live.Text(result));
            Assert.True(company.RootElement.GetProperty("found").GetBoolean());
            Assert.Contains("ORLEN", company.RootElement.GetProperty("name").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            // The server's stderr (dnx download messages, warnings) goes into the test output for diagnosis.
            foreach (var line in stderr)
            {
                TestContext.Current.TestOutputHelper?.WriteLine("[server stderr] " + line);
            }
        }
    }
}
