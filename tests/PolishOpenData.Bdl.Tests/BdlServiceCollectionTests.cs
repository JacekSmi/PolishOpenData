using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bdl.Tests;

public class BdlServiceCollectionTests
{
    [Fact]
    public async Task AddBdlClient_ResolvesAClientThatSendsTheConfiguredClientId()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bdl/units-search-krakow.json"));
        var services = new ServiceCollection();
        services.AddBdlClient(o => o.ClientId = "key-123").ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var units = await provider.GetRequiredService<IBdlClient>().SearchUnitsAsync("Kraków", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, units.Count);
        Assert.Equal("key-123", handler.Requests.Single().ClientId);
    }

    [Fact]
    public async Task AddBdlClient_WithoutConfigure_SendsNoClientId()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "bdl/units-search-krakow.json"));
        var services = new ServiceCollection();
        services.AddBdlClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<IBdlClient>().SearchUnitsAsync("Kraków", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(handler.Requests.Single().ClientId);
    }
}
