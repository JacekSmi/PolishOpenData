using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.BialaLista.Offline.Tests;

public sealed class VatWhitelistOfflineServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private ServiceProvider Provider(StubHttpMessageHandler handler, Action<VatWhitelistOfflineOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero)));
        services.AddVatWhitelistOffline(o =>
        {
            o.DataDirectory = _dir;
            configure?.Invoke(o);
        }).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static StubHttpMessageHandler Server() =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Fixture.ReadBytes("bialalista-offline/20261013.7z")) });

    [Fact]
    public async Task Registers_singleton_store_shared_by_clients()
    {
        var server = Server();
        using var provider = Provider(server);
        var nip = Nip.Parse("5261040828");
        var account = Nrb.Parse("06160011271843983820000034");

        var first = provider.GetRequiredService<IVatWhitelistOffline>();
        var second = provider.GetRequiredService<IVatWhitelistOffline>();
        _ = await first.CheckAsync(nip, account, cancellationToken: TestContext.Current.CancellationToken);
        var result = await second.CheckAsync(nip, account, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotSame(first, second);
        Assert.Equal(VatWhitelistOfflineStatus.Active, result.Status);
        Assert.Single(server.RequestUris);
        Assert.Same(provider.GetRequiredService<OfflineIndexStore>(), provider.GetRequiredService<OfflineIndexStore>());
    }

    [Fact]
    public async Task Options_are_applied()
    {
        var server = Server();
        using var provider = Provider(server, o => o.BaseAddress = new Uri("https://example.test/files/"));

        _ = await provider.GetRequiredService<IVatWhitelistOffline>().PrepareAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("https://example.test/files/20261013.7z", server.RequestUris[0].AbsoluteUri);
        Assert.True(File.Exists(Path.Combine(_dir, "20261013.wlidx")));
        Assert.Equal(Timeout.InfiniteTimeSpan, provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IVatWhitelistOffline)).Timeout);
    }

    [Fact]
    public void Default_options()
    {
        var options = new VatWhitelistOfflineOptions();

        Assert.Equal("https://plikplaski.mf.gov.pl/pliki/", options.BaseAddress.AbsoluteUri);
        Assert.Equal(5, options.DatesToKeep);
        Assert.Equal(TimeSpan.FromMinutes(15), options.LockTimeout);
        Assert.EndsWith(Path.Combine("PolishOpenData", "BialaListaOffline"), options.ResolveDataDirectory(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_options_throw()
    {
        var options = new VatWhitelistOfflineOptions();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => options.DatesToKeep = 0);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => options.LockTimeout = TimeSpan.Zero);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => options.LockTimeout = Timeout.InfiniteTimeSpan);
        _ = Assert.Throws<ArgumentException>(() => options.BaseAddress = new Uri("relative/", UriKind.Relative));
        _ = Assert.Throws<ArgumentException>(() => options.BaseAddress = new Uri("https://example.test/files"));
        _ = Assert.Throws<ArgumentException>(() => options.DataDirectory = " ");
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddVatWhitelistOffline(o => o.DatesToKeep = -1));
    }
}
