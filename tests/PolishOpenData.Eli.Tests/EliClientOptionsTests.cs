using System;

namespace PolishOpenData.Eli.Tests;

public class EliClientOptionsTests
{
    [Theory]
    [InlineData("http://api.sejm.gov.pl/eli/")]
    [InlineData("/eli/")]
    public void Validate_RejectsNonHttpsOrRelative(string address)
    {
        var options = new EliClientOptions { BaseAddress = new Uri(address, UriKind.RelativeOrAbsolute) };
        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Default_IsTheSejmApi()
    {
        Assert.Equal("https://api.sejm.gov.pl/eli/", new EliClientOptions().BaseAddress.AbsoluteUri);
    }

    [Fact]
    public void ResolveBaseAddress_AddsTheTrailingSlash()
    {
        var options = new EliClientOptions { BaseAddress = new Uri("https://api.sejm.gov.pl/eli") };
        Assert.Equal("https://api.sejm.gov.pl/eli/", options.ResolveBaseAddress().AbsoluteUri);
    }
}
