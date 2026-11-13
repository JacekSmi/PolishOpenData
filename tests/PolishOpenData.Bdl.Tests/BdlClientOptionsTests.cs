using System;

namespace PolishOpenData.Bdl.Tests;

public class BdlClientOptionsTests
{
    [Theory]
    [InlineData("http://bdl.stat.gov.pl/api/v1/")]
    [InlineData("/api/v1/")]
    public void Validate_RejectsNonHttpsOrRelative(string address)
    {
        var options = new BdlClientOptions { BaseAddress = new Uri(address, UriKind.RelativeOrAbsolute) };
        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void ResolveClientId_TrimsAndNullsEmpty()
    {
        Assert.Equal("abc-123", new BdlClientOptions { ClientId = " abc-123\r\n" }.ResolveClientId());
        Assert.Null(new BdlClientOptions { ClientId = "  " }.ResolveClientId());
        Assert.Null(new BdlClientOptions().ResolveClientId());
    }

    [Fact]
    public void ResolveClientId_RejectsNonPrintableWithoutLeakingIt()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new BdlClientOptions { ClientId = "abécd" }.ResolveClientId());
        Assert.DoesNotContain("abécd", ex.ToString());
    }

    [Fact]
    public void Defaults_ArePolishAndProduction()
    {
        var o = new BdlClientOptions();
        Assert.Equal(BdlLanguage.Pl, o.Language);
        Assert.Equal("https://bdl.stat.gov.pl/api/v1/", o.BaseAddress.AbsoluteUri);
    }
}
