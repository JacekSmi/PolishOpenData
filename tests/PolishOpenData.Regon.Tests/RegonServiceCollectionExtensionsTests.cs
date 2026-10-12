using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace PolishOpenData.Bir.Tests;

public class RegonServiceCollectionExtensionsTests
{
    private static void UseTestEnvironment(RegonClientOptions options)
    {
        options.Environment = RegonEnvironment.Test;
        options.RateLimit = RegonRateLimit.Off;
    }

    [Fact]
    public void Registers_the_typed_client()
    {
        var services = new ServiceCollection();
        services.AddRegonClient(UseTestEnvironment);
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IRegonClient>();

        Assert.IsType<RegonClient>(client);
    }

    [Fact]
    public void Singleton_session_and_limiter_are_shared_between_resolves()
    {
        var services = new ServiceCollection();
        services.AddRegonClient(UseTestEnvironment);
        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<BirSession>(), provider.GetRequiredService<BirSession>());
        Assert.Same(provider.GetRequiredService<RegonRateLimiter>(), provider.GetRequiredService<RegonRateLimiter>());
        Assert.NotSame(provider.GetRequiredService<IRegonClient>(), provider.GetRequiredService<IRegonClient>());   // the typed client itself is transient
    }

    [Fact]
    public async Task Two_resolves_log_in_once()
    {
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus");
        var services = new ServiceCollection();
        services.AddRegonClient(UseTestEnvironment).ConfigurePrimaryHttpMessageHandler(() => server.Handler);
        using var provider = services.BuildServiceProvider();
        var nip = Nip.Parse("5261040828");

        var first = await provider.GetRequiredService<IRegonClient>().FindByNipAsync(nip, TestContext.Current.CancellationToken);
        var second = await provider.GetRequiredService<IRegonClient>().FindByNipAsync(nip, TestContext.Current.CancellationToken);

        Assert.Single(first.Value);
        Assert.Single(second.Value);
        Assert.Equal(1, server.Count("Zaloguj"));
        Assert.Equal(2, server.Count("DaneSzukajPodmioty"));
    }

    [Fact]
    public async Task Configured_production_key_reaches_the_login_of_the_typed_client()
    {
        // production with a dummy key: the fake handler answers, nothing reaches GUS
        const string key = "dummy-key-not-a-secret";
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus");
        var services = new ServiceCollection();
        services.AddRegonClient(o =>
        {
            o.UserKey = key;
            o.RateLimit = RegonRateLimit.Off;
        }).ConfigurePrimaryHttpMessageHandler(() => server.Handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IRegonClient>().FindByNipAsync(Nip.Parse("5261040828"), TestContext.Current.CancellationToken);

        Assert.Equal(RegonEnvironment.Production, result.Environment);
        var login = Assert.Single(server.Calls, c => c.Operation == "Zaloguj");
        Assert.Equal(key, login.Arg("pKluczUzytkownika"));
        Assert.Equal("https://wyszukiwarkaregon.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc", login.WsaTo);
    }

    [Fact]
    public void Configure_runs_eagerly()
    {
        var ran = false;

        new ServiceCollection().AddRegonClient(options =>
        {
            ran = true;
            UseTestEnvironment(options);
        });

        Assert.True(ran);
    }

    [Fact]
    public void TimeProvider_is_registered_when_missing()
    {
        var services = new ServiceCollection();
        services.AddRegonClient(UseTestEnvironment);
        using var provider = services.BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task An_existing_TimeProvider_is_kept()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero));
        var server = new FakeBirServer().Search("Nip", "5261040828", "search-regon-gus");
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddRegonClient(UseTestEnvironment).ConfigurePrimaryHttpMessageHandler(() => server.Handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IRegonClient>().FindByNipAsync(Nip.Parse("5261040828"), TestContext.Current.CancellationToken);

        Assert.Same(clock, provider.GetRequiredService<TimeProvider>());
        Assert.Equal(clock.GetUtcNow(), result.RetrievedAt);
    }

    [Fact]
    public void Returns_the_http_client_builder_for_further_configuration()
    {
        var services = new ServiceCollection();

        var builder = services.AddRegonClient(UseTestEnvironment);

        Assert.NotNull(builder);
        Assert.Equal(nameof(IRegonClient), builder.Name);
        Assert.Same(builder, builder.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Missing_key_throws_on_first_resolve()
    {
        var services = new ServiceCollection();
        services.AddRegonClient();   // production, no key
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IRegonClient>());

        Assert.Contains("RegonClientOptions.UserKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_services_throws()
    {
        Assert.Throws<ArgumentNullException>(() => RegonServiceCollectionExtensions.AddRegonClient(null!));
    }
}
