using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Mcp.Tests;

/// <summary>lookup_ceidg through a real MCP session; CEIDG answers from the synthetic fixtures.</summary>
public sealed class CeidgToolsTests : IAsyncDisposable
{
    private const string Token = "eyJ.synthetic-test-token.sig";

    private McpTestSession? _session;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    private async Task<McpTestSession> StartAsync(StubHttpMessageHandler handler, string token)
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _session = await McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler),
            null,
            TestContext.Current.CancellationToken,
            ceidgToken: token);
        return _session;
    }

    private async Task<CallToolResult> LookupAsync(StubHttpMessageHandler handler, string nip)
    {
        var session = await StartAsync(handler, Token);
        return await session.Client.CallToolAsync("lookup_ceidg", new System.Collections.Generic.Dictionary<string, object?> { ["nip"] = nip }, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static JsonElement Data(CallToolResult result)
    {
        Assert.True(result.IsError != true, Text(result));
        return result.StructuredContent!.Value;
    }

    private static StubHttpMessageHandler Active() =>
        new(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "ceidg/firma-nip-active.json"));

    [Fact]
    public async Task Tool_IsNotListed_WithoutAToken()
    {
        var session = await StartAsync(Active(), string.Empty);

        var tools = await session.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(tools, t => t.Name == "lookup_ceidg");
    }

    [Fact]
    public async Task Tool_IsListed_WithAToken_AndInstructionsMentionIt()
    {
        var session = await StartAsync(Active(), Token);

        var tools = await session.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "lookup_ceidg");
        Assert.Contains("lookup_ceidg", session.Client.ServerInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lookup_ReturnsTheBusiness_AndSendsTheToken()
    {
        var handler = Active();

        var data = Data(await LookupAsync(handler, "3563457932"));

        Assert.True(data.GetProperty("found").GetBoolean());
        Assert.Equal("NIP 3563457932", data.GetProperty("query").GetString());
        var b = Assert.Single(data.GetProperty("businesses").EnumerateArray());
        Assert.Equal("Jan Testowy Usługi Informatyczne", b.GetProperty("name").GetString());
        Assert.Equal("active", b.GetProperty("status").GetString());
        Assert.Equal("Jan Testowy", b.GetProperty("owner").GetString());
        Assert.Equal("3563457932", b.GetProperty("nip").GetString());
        Assert.Equal("2014-07-29", b.GetProperty("startDate").GetString());
        Assert.Equal("ul. Zwierzyniecka 1/4, 15-333 Białystok", b.GetProperty("address").GetString());
        Assert.Equal("6201Z", b.GetProperty("mainPkd").GetProperty("code").GetString());
        Assert.Contains("personal data", b.GetProperty("note").GetString(), StringComparison.Ordinal);
        Assert.Equal("Bearer " + Token, handler.Requests.Single(r => r.Uri.Host == "dane.biznes.gov.pl").Authorization);
    }

    [Fact]
    public async Task Lookup_NothingFound_ReturnsFoundFalse()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Empty(HttpStatusCode.NoContent));

        var data = Data(await LookupAsync(handler, "3563457932"));

        Assert.False(data.GetProperty("found").GetBoolean());
        Assert.Empty(data.GetProperty("businesses").EnumerateArray());
    }

    [Fact]
    public async Task Lookup_InvalidNip_IsAnErrorWithoutARequest()
    {
        var handler = Active();

        var result = await LookupAsync(handler, "123");

        Assert.True(result.IsError);
        Assert.Contains("not a valid NIP", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.Host == "dane.biznes.gov.pl");
    }

    [Fact]
    public async Task Lookup_RejectedToken_NamesTheVariableAndNotTheToken()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, """{"message":"Unauthorized","http_status_code":401}"""));

        var result = await LookupAsync(handler, "3563457932");

        Assert.True(result.IsError);
        Assert.Contains("POLISHOPENDATA_CEIDG_TOKEN", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, Text(result), StringComparison.Ordinal);
    }
}
