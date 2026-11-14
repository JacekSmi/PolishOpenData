using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Sejm.Tests;

public class SejmServiceCollectionTests
{
    [Fact]
    public async Task AddSejmClient_ResolvesAClientThatUsesTheConfiguredAddress()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/terms.json"));
        var services = new ServiceCollection();
        services.AddSejmClient(o => o.BaseAddress = new Uri("https://example.test/sejm/")).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var terms = await provider.GetRequiredService<ISejmClient>().GetTermsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(terms);
        Assert.Equal("https://example.test/sejm/term", handler.RequestUris.Single().AbsoluteUri);
    }

    [Fact]
    public async Task AddSejmClient_WithoutConfigure_UsesTheSejmApi()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.FromFixture(HttpStatusCode.OK, "sejm/terms.json"));
        var services = new ServiceCollection();
        services.AddSejmClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<ISejmClient>().GetTermsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://api.sejm.gov.pl/sejm/term", handler.RequestUris.Single().AbsoluteUri);
    }

    [Fact]
    public void AddSejmClient_RejectsAnHttpAddressAtRegistration()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddSejmClient(o => o.BaseAddress = new Uri("http://api.sejm.gov.pl/sejm/")));
    }
}
