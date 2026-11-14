using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Eli.Tests;

public class EliServiceCollectionTests
{
    [Fact]
    public async Task AddEliClient_ResolvesAClientThatUsesTheConfiguredAddress()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-1964-93.json"));
        var services = new ServiceCollection();
        services.AddEliClient(o => o.BaseAddress = new System.Uri("https://example.test/eli/")).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var act = await provider.GetRequiredService<IEliClient>().GetActAsync(EliActId.Parse("DU/1964/93"), TestContext.Current.CancellationToken);

        Assert.Equal("Dz.U. 1964 nr 16 poz. 93", act!.Summary.DisplayAddress);
        Assert.Equal("https://example.test/eli/acts/DU/1964/93", handler.RequestUris.Single().AbsoluteUri);
    }

    [Fact]
    public async Task AddEliClient_WithoutConfigure_UsesTheSejmApi()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "eli/act-DU-1964-93.json"));
        var services = new ServiceCollection();
        services.AddEliClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<IEliClient>().GetActAsync(EliActId.Parse("DU/1964/93"), TestContext.Current.CancellationToken);

        Assert.Equal("https://api.sejm.gov.pl/eli/acts/DU/1964/93", handler.RequestUris.Single().AbsoluteUri);
    }

    [Fact]
    public void AddEliClient_RejectsAnHttpAddressAtRegistration()
    {
        var services = new ServiceCollection();

        Assert.Throws<System.ArgumentException>(() => services.AddEliClient(o => o.BaseAddress = new System.Uri("http://api.sejm.gov.pl/eli/")));
    }
}
