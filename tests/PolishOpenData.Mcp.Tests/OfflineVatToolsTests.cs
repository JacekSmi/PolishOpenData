using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Mcp.Tests;

/// <summary>The offline tool through a real MCP session; the flat file comes from the fixture archive, never from the network.</summary>
public sealed class OfflineVatToolsTests : IAsyncDisposable
{
    private const string ActiveNip = "5261040828";
    private const string ActiveAccount = "06160011271843983820000034";
    private const string ExemptNip = "7171642051";
    private const string ExemptAccount = "61109010140000071219812874";
    private const string VirtualAccount = "60102010260000042270201111";
    private const string UnknownAccount = "36124000011111222233334444";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly StubHttpMessageHandler _stub = new(request =>
        request.RequestUri!.Host == "plikplaski.mf.gov.pl" && request.RequestUri.AbsolutePath == "/pliki/20261013.7z"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Fixture.ReadBytes("bialalista-offline/20261013.7z")) }
            : StubHttpMessageHandler.Empty(HttpStatusCode.NotFound));

    private McpTestSession? _session;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<McpTestSession> SessionAsync() =>
        _session ??= await McpTestSession.StartAsync(
            http => http.ConfigurePrimaryHttpMessageHandler(() => _stub),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero)),
            TestContext.Current.CancellationToken,
            _dir);

    private async Task<CallToolResult> CallAsync(IEnumerable<(string Nip, string Account)> pairs, string? date = null, IProgress<ProgressNotificationValue>? progress = null)
    {
        var session = await SessionAsync();
        var arguments = new Dictionary<string, object?>
        {
            ["pairs"] = pairs.Select(p => new Dictionary<string, object?> { ["nip"] = p.Nip, ["bankAccount"] = p.Account }).ToArray(),
        };
        if (date is not null)
        {
            arguments["date"] = date;
        }

        return await session.Client.CallToolAsync("check_vat_bank_account_offline", arguments, progress, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static JsonElement Result(CallToolResult result, int index) => result.StructuredContent!.Value.GetProperty("results")[index];

    [Fact]
    public async Task Returns_statuses_for_fixture_pairs()
    {
        var result = await CallAsync([(ActiveNip, ActiveAccount), (ExemptNip, ExemptAccount), (ActiveNip, UnknownAccount)]);

        Assert.True(result.IsError != true, Text(result));
        var data = result.StructuredContent!.Value;
        Assert.Equal("2026-10-13", data.GetProperty("fileDate").GetString());
        Assert.Equal("2026-10-13", data.GetProperty("generatedOn").GetString());
        Assert.Equal(3, data.GetProperty("results").GetArrayLength());
        Assert.Equal("active", Result(result, 0).GetProperty("status").GetString());
        Assert.Equal(ActiveNip, Result(result, 0).GetProperty("nip").GetString());
        Assert.Equal(ActiveAccount, Result(result, 0).GetProperty("bankAccount").GetString());
        Assert.False(Result(result, 0).GetProperty("viaVirtualAccountMask").GetBoolean());
        Assert.Equal("exempt", Result(result, 1).GetProperty("status").GetString());
        Assert.Equal("not_found", Result(result, 2).GetProperty("status").GetString());
        Assert.Contains("plikplaski.mf.gov.pl", data.GetProperty("source").GetProperty("endpoint").GetString(), StringComparison.Ordinal);
        Assert.Single(_stub.RequestUris);
    }

    [Fact]
    public async Task Not_found_meaning_is_not_proof_of_absence()
    {
        var result = await CallAsync([(ActiveNip, UnknownAccount)]);

        var meaning = Result(result, 0).GetProperty("meaning").GetString()!;
        Assert.Contains("not found in the Ministry of Finance flat file for 2026-10-13; this is not proof that the account is absent from the whitelist", meaning, StringComparison.Ordinal);
        Assert.DoesNotContain("not on the", meaning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Accepts_iban_with_spaces()
    {
        var result = await CallAsync([("526-104-08-28", "PL 60 1020 1026 0000 0422 7020 1111")]);

        Assert.True(result.IsError != true, Text(result));
        Assert.Equal("active", Result(result, 0).GetProperty("status").GetString());
        Assert.True(Result(result, 0).GetProperty("viaVirtualAccountMask").GetBoolean());
        Assert.Equal(VirtualAccount, Result(result, 0).GetProperty("bankAccount").GetString());
    }

    [Fact]
    public async Task Invalid_nip_is_tool_error()
    {
        var result = await CallAsync([(ActiveNip, ActiveAccount), ("5261040829", ActiveAccount)]);

        Assert.True(result.IsError == true);
        Assert.Contains("5261040829", Text(result), StringComparison.Ordinal);
        Assert.Contains("NIP", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);   // validated before any I/O
    }

    [Fact]
    public async Task Invalid_account_is_tool_error()
    {
        var result = await CallAsync([(ActiveNip, "12345")]);

        Assert.True(result.IsError == true);
        Assert.Contains("bank account", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Pair_count_outside_1_to_50_is_tool_error(int count)
    {
        var result = await CallAsync(Enumerable.Repeat((ActiveNip, ActiveAccount), count));

        Assert.True(result.IsError == true);
        Assert.Contains("1 to 50", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Fifty_pairs_are_accepted()
    {
        var result = await CallAsync(Enumerable.Repeat((ActiveNip, ActiveAccount), 50));

        Assert.True(result.IsError != true, Text(result));
        Assert.Equal(50, result.StructuredContent!.Value.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task Bad_date_format_is_tool_error()
    {
        var result = await CallAsync([(ActiveNip, ActiveAccount)], "13.10.2026");

        Assert.True(result.IsError == true);
        Assert.Contains("YYYY-MM-DD", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Theory]
    [InlineData("2026-09-12")]   // today - 31
    [InlineData("2026-10-15")]   // today + 2
    public async Task Date_outside_window_is_tool_error_with_reason_text(string date)
    {
        var result = await CallAsync([(ActiveNip, ActiveAccount)], date);

        Assert.True(result.IsError == true);
        Assert.Contains("only the last 30 days and tomorrow", Text(result), StringComparison.Ordinal);
        Assert.Empty(_stub.RequestUris);
    }

    [Fact]
    public async Task Reports_progress_during_first_call()
    {
        var progress = new Collector();

        var result = await CallAsync([(ActiveNip, ActiveAccount)], progress: progress);

        Assert.True(result.IsError != true, Text(result));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!progress.Values.Any(v => v.Message == "Building the index") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        var values = progress.Values.ToArray();
        var download = values.First(v => v.Message == "Downloading the MF flat file");
        Assert.True(download.Progress > 0);
        Assert.Equal(download.Progress, download.Total);   // the stub announces the size, and the last download report is complete
        Assert.Contains(values, v => v.Message == "Building the index");
    }

    private sealed class Collector : IProgress<ProgressNotificationValue>
    {
        private readonly List<ProgressNotificationValue> _values = [];

        public IReadOnlyList<ProgressNotificationValue> Values
        {
            get
            {
                lock (_values)
                {
                    return _values.ToArray();
                }
            }
        }

        public void Report(ProgressNotificationValue value)
        {
            lock (_values)
            {
                _values.Add(value);
            }
        }
    }
}
