using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Mcp.Tests;

/// <summary>
/// Runs the production DI wiring (<c>AddPolishOpenDataServices</c> + <c>AddMcpServer</c> + <c>AddPolishOpenDataTools</c>)
/// behind a real MCP session, connected over an in-memory pipe transport. Every other test in this project calls tool
/// methods directly, which never exercises <c>AddCallToolFilter</c> — a Dependabot SDK bump could silently change how
/// (or whether) it reports tool errors without any of those tests noticing.
/// </summary>
public sealed class McpServerEndToEndTests
{
    [Fact]
    public async Task Protocol_error_text_is_the_tool_error_not_the_generic_sdk_message()
    {
        var stub = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == "/api/search/nip/7740001454"
                ? StubHttpMessageHandler.FromFixture((HttpStatusCode)429, "bialalista/synthetic-error-429-wl191-limit.json")
                : StubHttpMessageHandler.Empty(HttpStatusCode.NotFound));

        await using var session = await StartAsync(stub);
        var result = await session.Client.CallToolAsync(
            "lookup_company",
            new Dictionary<string, object?> { ["nip"] = "7740001454" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError == true);
        var text = ((TextContentBlock)result.Content[0]).Text;
        Assert.StartsWith("Request limit reached:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred invoking", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_upstream_call_is_not_cached()
    {
        // A KRS call: Biała Lista's quota guard is untouched by the 429 test above (a fresh session per test), but
        // using KRS here keeps this test's own upstream failure independent of the quota guard entirely.
        var stub = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{}"));
        await using var session = await StartAsync(stub);
        var arguments = new Dictionary<string, object?> { ["krs"] = "555001" };

        var first = await session.Client.CallToolAsync("get_krs_extract", arguments, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(first.IsError == true);
        Assert.Single(stub.RequestUris);

        var second = await session.Client.CallToolAsync("get_krs_extract", arguments, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(second.IsError == true);
        Assert.Equal(2, stub.RequestUris.Count);   // asked the stub again: the failed first call was not cached
    }

    private static Task<McpTestSession> StartAsync(HttpMessageHandler handler) =>
        McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler),
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero)),
            TestContext.Current.CancellationToken);
}
