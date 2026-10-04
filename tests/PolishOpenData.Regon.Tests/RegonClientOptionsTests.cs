using System;

namespace PolishOpenData.Bir.Tests;

public class RegonClientOptionsTests
{
    private const string DummyKey = "dummy-key-not-a-secret";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Production_without_a_key_throws_naming_the_option(string? key)
    {
        var options = new RegonClientOptions { UserKey = key };

        var ex = Assert.Throws<InvalidOperationException>(() => options.ResolveUserKey());

        Assert.Contains("RegonClientOptions.UserKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_with_a_key_uses_it_and_the_production_address()
    {
        var options = new RegonClientOptions { UserKey = DummyKey };

        Assert.Equal(DummyKey, options.ResolveUserKey());
        Assert.Equal(new Uri("https://wyszukiwarkaregon.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc"), options.ResolveAddress());
    }

    [Fact]
    public void Test_environment_uses_the_published_key_and_the_test_address()
    {
        var options = new RegonClientOptions { Environment = RegonEnvironment.Test };

        Assert.Equal(RegonClientOptions.TestUserKey, options.ResolveUserKey());
        Assert.Equal(20, options.ResolveUserKey().Length);
        Assert.Equal(new Uri("https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc"), options.ResolveAddress());
    }

    [Fact]
    public void The_test_environment_ignores_a_configured_key()
    {
        var options = new RegonClientOptions { Environment = RegonEnvironment.Test, UserKey = DummyKey };

        Assert.Equal(RegonClientOptions.TestUserKey, options.ResolveUserKey());
    }

    [Fact]
    public void BaseAddress_overrides_the_environment_address()
    {
        var fake = new Uri("https://fake.example.test/bir.svc");

        Assert.Equal(fake, new RegonClientOptions { Environment = RegonEnvironment.Test, BaseAddress = fake }.ResolveAddress());
        Assert.Equal(fake, new RegonClientOptions { UserKey = DummyKey, BaseAddress = fake }.ResolveAddress());
    }

    [Theory]
    [InlineData("lifetime-zero")]
    [InlineData("lifetime-negative")]
    [InlineData("wait-negative")]
    [InlineData("environment-undefined")]
    [InlineData("rate-limit-undefined")]
    public void Invalid_values_are_rejected(string scenario)
    {
        var options = new RegonClientOptions { Environment = RegonEnvironment.Test, UserKey = DummyKey };
        switch (scenario)
        {
            case "lifetime-zero":
                options.SessionLifetime = TimeSpan.Zero;
                break;
            case "lifetime-negative":
                options.SessionLifetime = TimeSpan.FromMinutes(-1);
                break;
            case "wait-negative":
                options.MaxRateLimitWait = TimeSpan.FromMilliseconds(-1);
                break;
            case "environment-undefined":
                options.Environment = (RegonEnvironment)7;
                break;
            case "rate-limit-undefined":
                options.RateLimit = (RegonRateLimit)7;
                break;
            default:
                throw new InvalidOperationException("Unknown scenario " + scenario);
        }

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

        Assert.Contains("RegonClientOptions.", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(DummyKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relative_base_address_is_rejected()
    {
        var options = new RegonClientOptions { Environment = RegonEnvironment.Test, BaseAddress = new Uri("/bir.svc", UriKind.Relative) };

        var ex = Assert.Throws<ArgumentException>(() => options.Validate());

        Assert.Contains("RegonClientOptions.BaseAddress", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_http_base_address_is_rejected()
    {
        var options = new RegonClientOptions { Environment = RegonEnvironment.Test, BaseAddress = new Uri("http://fake.example.test/bir.svc") };

        var ex = Assert.Throws<ArgumentException>(() => options.Validate());

        Assert.Contains("RegonClientOptions.BaseAddress", ex.Message, StringComparison.Ordinal);
        Assert.Contains("https", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Test_host_with_production_environment_is_rejected()
    {
        var options = new RegonClientOptions { UserKey = DummyKey, BaseAddress = new Uri("https://wyszukiwarkaregontest.stat.gov.pl/other/Fake.svc") };

        var ex = Assert.Throws<InvalidOperationException>(() => options.ResolveEnvironment());

        Assert.Contains("RegonClientOptions.BaseAddress", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(DummyKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_host_with_test_environment_is_rejected()
    {
        var options = new RegonClientOptions { Environment = RegonEnvironment.Test, BaseAddress = RegonClientOptions.ProductionAddress };

        var ex = Assert.Throws<InvalidOperationException>(() => options.ResolveEnvironment());

        Assert.Contains("RegonClientOptions.BaseAddress", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fake_host_keeps_the_configured_environment()
    {
        var fake = new Uri("https://fake.example.test/bir.svc");

        Assert.Equal(RegonEnvironment.Production, new RegonClientOptions { UserKey = DummyKey, BaseAddress = fake }.ResolveEnvironment());
        Assert.Equal(RegonEnvironment.Test, new RegonClientOptions { Environment = RegonEnvironment.Test, BaseAddress = fake }.ResolveEnvironment());
        Assert.Equal(RegonEnvironment.Production, new RegonClientOptions { UserKey = DummyKey }.ResolveEnvironment());
        Assert.Equal(RegonEnvironment.Test, new RegonClientOptions { Environment = RegonEnvironment.Test }.ResolveEnvironment());
        Assert.Equal(RegonEnvironment.Test, new RegonClientOptions { Environment = RegonEnvironment.Test, BaseAddress = RegonClientOptions.TestAddress }.ResolveEnvironment());
    }

    [Fact]
    public void Defaults_are_30_minutes_strictest_and_5_seconds()
    {
        var options = new RegonClientOptions();

        Assert.Equal(TimeSpan.FromMinutes(30), options.SessionLifetime);
        Assert.Equal(RegonRateLimit.Strictest, options.RateLimit);
        Assert.Equal(TimeSpan.FromSeconds(5), options.MaxRateLimitWait);
        Assert.Equal(RegonEnvironment.Production, options.Environment);
        Assert.Null(options.UserKey);
        Assert.Null(options.BaseAddress);
        options.Validate();
    }

    [Fact]
    public void Key_is_not_in_the_exception_message()
    {
        var options = new RegonClientOptions { UserKey = DummyKey, SessionLifetime = TimeSpan.Zero };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

        Assert.DoesNotContain(DummyKey, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(DummyKey, options.ToString() ?? string.Empty, StringComparison.Ordinal);
    }
}
