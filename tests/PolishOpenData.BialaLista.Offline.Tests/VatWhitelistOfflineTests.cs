using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.BialaLista.Offline.Tests;

public sealed class VatWhitelistOfflineTests : IDisposable
{
    private static readonly Nip ActiveNip = Nip.Parse("5261040828");
    private static readonly Nrb ActiveAccount = Nrb.Parse("06160011271843983820000034");
    private static readonly Nip ExemptNip = Nip.Parse("7171642051");
    private static readonly Nrb ExemptAccount = Nrb.Parse("61109010140000071219812874");
    private static readonly Nrb VirtualAccount = Nrb.Parse("60102010260000042270201111");
    private static readonly Nrb UnknownAccount = Nrb.Parse("36124000011111222233334444");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));
    private readonly List<VatWhitelistOffline> _clients = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private static StubHttpMessageHandler Server(string fixture = "20261013.7z") =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Fixture.ReadBytes("bialalista-offline/" + fixture)) });

    private VatWhitelistOffline Client(StubHttpMessageHandler handler)
    {
        var client = new VatWhitelistOffline(new HttpClient(handler), new VatWhitelistOfflineOptions { DataDirectory = _dir }, _time);
        _clients.Add(client);
        return client;
    }

    private sealed class Collector : IProgress<VatWhitelistPreparationProgress>
    {
        public List<VatWhitelistPreparationProgress> Reports { get; } = new();

        public void Report(VatWhitelistPreparationProgress value) => Reports.Add(value);
    }

    [Fact]
    public async Task Check_active_pair()
    {
        var result = await Client(Server()).CheckAsync(ActiveNip, ActiveAccount, cancellationToken: Token);

        Assert.Equal(VatWhitelistOfflineStatus.Active, result.Status);
        Assert.False(result.MatchedVirtualAccountMask);
        Assert.Equal(new DateOnly(2026, 10, 13), result.FileDate);
        Assert.Equal(new DateOnly(2026, 10, 13), result.GeneratedOn);
        Assert.Equal(ActiveNip, result.Nip);
        Assert.Equal(ActiveAccount, result.Account);
    }

    [Fact]
    public async Task Check_virtual_account()
    {
        var result = await Client(Server()).CheckAsync(ActiveNip, VirtualAccount, cancellationToken: Token);

        Assert.Equal(VatWhitelistOfflineStatus.Active, result.Status);
        Assert.True(result.MatchedVirtualAccountMask);
    }

    [Fact]
    public async Task Check_unknown_is_not_found()
    {
        var result = await Client(Server()).CheckAsync(ActiveNip, UnknownAccount, cancellationToken: Token);

        Assert.Equal(VatWhitelistOfflineStatus.NotFound, result.Status);
        Assert.False(result.MatchedVirtualAccountMask);
    }

    [Fact]
    public async Task Check_rejects_empty_values()
    {
        var client = Client(Server());

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.CheckAsync(default, ActiveAccount, cancellationToken: Token));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.CheckAsync(ActiveNip, default, cancellationToken: Token));
    }

    [Fact]
    public async Task Second_check_same_day_makes_no_request()
    {
        var server = Server();
        var client = Client(server);

        _ = await client.CheckAsync(ActiveNip, ActiveAccount, cancellationToken: Token);
        _ = await client.CheckAsync(ExemptNip, ExemptAccount, cancellationToken: Token);

        Assert.Single(server.RequestUris);
        Assert.EndsWith("/20261013.7z", server.RequestUris[0].AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckMany_empty_does_not_touch_network()
    {
        var server = Server();

        var results = await Client(server).CheckManyAsync([], cancellationToken: Token);

        Assert.Empty(results);
        Assert.Empty(server.RequestUris);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public async Task CheckMany_preserves_order_and_duplicates()
    {
        var server = Server();
        var pairs = new[] { (ActiveNip, UnknownAccount), (ActiveNip, ActiveAccount), (ActiveNip, ActiveAccount), (ExemptNip, ExemptAccount) };

        var results = await Client(server).CheckManyAsync(pairs, cancellationToken: Token);

        Assert.Equal(
            [VatWhitelistOfflineStatus.NotFound, VatWhitelistOfflineStatus.Active, VatWhitelistOfflineStatus.Active, VatWhitelistOfflineStatus.Exempt],
            results.Select(r => r.Status));
        Assert.Equal(pairs.Select(p => p.Item2), results.Select(r => r.Account));
        Assert.Single(server.RequestUris);
    }

    [Fact]
    public async Task CheckMany_rejects_an_empty_value()
    {
        var client = Client(Server());

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.CheckManyAsync([(ActiveNip, default(Nrb))], cancellationToken: Token));
    }

    [Fact]
    public async Task Date_outside_window_throws_without_request()
    {
        var server = Server();
        var client = Client(server);

        var ex = await Assert.ThrowsAsync<VatWhitelistDataUnavailableException>(
            () => client.CheckAsync(ActiveNip, ActiveAccount, new DateOnly(2026, 9, 1), Token));
        _ = await Assert.ThrowsAsync<VatWhitelistDataUnavailableException>(
            () => client.CheckManyAsync([(ActiveNip, ActiveAccount)], new DateOnly(2026, 10, 15), Token));
        _ = await Assert.ThrowsAsync<VatWhitelistDataUnavailableException>(
            () => client.PrepareAsync(new DateOnly(2026, 9, 1), cancellationToken: Token));

        Assert.Equal(VatWhitelistDataUnavailableReason.OutsideWindow, ex.Reason);
        Assert.Empty(server.RequestUris);
    }

    [Fact]
    public async Task Checksum_mismatch_throws_and_leaves_no_index()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));
        var client = Client(Server("20261014-badsum.7z"));

        var ex = await Assert.ThrowsAsync<VatWhitelistDataUnavailableException>(
            () => client.CheckAsync(ActiveNip, ActiveAccount, cancellationToken: Token));

        Assert.Equal(VatWhitelistDataUnavailableReason.ChecksumMismatch, ex.Reason);
        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.EndsWith(".wlidx", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Prepare_returns_info_and_reports_both_stages()
    {
        var progress = new Collector();

        var info = await Client(Server()).PrepareAsync(progress: progress, cancellationToken: Token);

        Assert.Equal(new DateOnly(2026, 10, 13), info.FileDate);
        Assert.Equal(22, info.ActiveCount);
        Assert.Contains(progress.Reports, r => r.Stage == VatWhitelistPreparationStage.Downloading);
        Assert.Equal(VatWhitelistPreparationStage.Indexing, progress.Reports.Last().Stage);
    }
}
