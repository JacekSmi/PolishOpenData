using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using PolishOpenData.Bir.Tests;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Mcp.Tests;

/// <summary>lookup_regon through a real MCP session; GUS BIR answers from the recorded fixtures (FakeBirServer).</summary>
public sealed class RegonToolsTests : IAsyncDisposable
{
    private const string Key = "dummy-key-not-a-secret";
    private const string GusRegon = "000331501";

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private McpTestSession? _session;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    private async Task<CallToolResult> LookupAsync(FakeBirServer server, string key, params (string Name, object? Value)[] arguments)
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _session = await McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => server.Handler),
            new FakeTimeProvider(Now),
            TestContext.Current.CancellationToken,
            regonKey: key);
        return await _session.Client.CallToolAsync("lookup_regon", arguments.ToDictionary(a => a.Name, a => a.Value), cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static JsonElement Data(CallToolResult result)
    {
        Assert.True(result.IsError != true, Text(result));
        return result.StructuredContent!.Value;
    }

    private static FakeBirServer LegalPersonServer() => new FakeBirServer()
        .Search("Regon", GusRegon, "search-regon-gus")
        .Report(GusRegon, "BIR12OsPrawna", "synthetic-report-osprawna-gus")
        .Report(GusRegon, "BIR12OsPrawnaPkd", "synthetic-report-osprawna-pkd");

    [Fact]
    public async Task Lookup_by_regon_returns_the_search_row()
    {
        var data = Data(await LookupAsync(LegalPersonServer(), Key, ("regon", GusRegon)));

        Assert.True(data.GetProperty("found").GetBoolean());
        Assert.Equal("production", data.GetProperty("environment").GetString());
        var entity = Assert.Single(data.GetProperty("entities").EnumerateArray());
        Assert.Equal("legal_person", entity.GetProperty("type").GetString());
        Assert.Equal(GusRegon, entity.GetProperty("regon").GetString());
        Assert.Equal("Rejestr REGON, GUS (usługa BIR1.2)", Assert.Single(data.GetProperty("sources").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Not_found_is_not_an_error()
    {
        var server = new FakeBirServer().SearchNotFound("Regon", GusRegon);

        var data = Data(await LookupAsync(server, Key, ("regon", GusRegon)));

        Assert.False(data.GetProperty("found").GetBoolean());
        Assert.Empty(data.GetProperty("entities").EnumerateArray());
    }

    [Theory]
    [InlineData("nip", "123", "'123' is not a valid NIP (10 digits with a correct check digit).")]
    [InlineData("regon", "123", "'123' is not a valid REGON (9 or 14 digits with correct check digits).")]
    [InlineData("krs", "abc", "'abc' is not a KRS number (1 to 10 digits).")]
    public async Task Invalid_identifiers_use_the_lookup_company_texts(string name, string value, string expected)
    {
        var server = new FakeBirServer();

        var result = await LookupAsync(server, Key, (name, value));

        Assert.True(result.IsError);
        Assert.Equal(expected, Text(result));
        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task None_or_two_identifiers_are_rejected_before_any_call()
    {
        var server = new FakeBirServer();

        var none = await LookupAsync(server, Key);
        var two = await LookupAsync(server, Key, ("regon", GusRegon), ("nip", "5261040828"));

        Assert.True(none.IsError);
        Assert.Equal("Provide exactly one of nip, regon or krs.", Text(none));
        Assert.True(two.IsError);
        Assert.Equal("Provide exactly one of nip, regon or krs.", Text(two));
        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task Login_sends_the_configured_key_trimmed()
    {
        var server = LegalPersonServer();

        await LookupAsync(server, Key + "\n", ("regon", GusRegon));

        var login = Assert.Single(server.Calls, c => c.Operation == "Zaloguj");
        Assert.Equal(Key, login.Arg("pKluczUzytkownika"));
    }

    [Fact]
    public async Task Rejected_key_error_never_echoes_the_key()
    {
        var server = new FakeBirServer().RejectKey();

        var result = await LookupAsync(server, Key, ("regon", GusRegon));

        Assert.True(result.IsError);
        Assert.StartsWith("GUS did not accept the key in POLISHOPENDATA_REGON_KEY.", Text(result), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, Text(result), StringComparison.Ordinal);
    }
}
