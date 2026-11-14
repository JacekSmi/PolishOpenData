using System;

namespace PolishOpenData.Sejm.Tests;

public class SejmClientOptionsTests
{
    [Theory]
    [InlineData("http://api.sejm.gov.pl/sejm/")]
    [InlineData("/sejm/")]
    public void Validate_RejectsNonHttpsOrRelative(string address)
    {
        var options = new SejmClientOptions { BaseAddress = new Uri(address, UriKind.RelativeOrAbsolute) };
        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Default_IsTheSejmApi()
    {
        Assert.Equal("https://api.sejm.gov.pl/sejm/", new SejmClientOptions().BaseAddress.AbsoluteUri);
    }

    [Fact]
    public void ResolveBaseAddress_AddsTheTrailingSlash()
    {
        var options = new SejmClientOptions { BaseAddress = new Uri("https://api.sejm.gov.pl/sejm") };
        Assert.Equal("https://api.sejm.gov.pl/sejm/", options.ResolveBaseAddress().AbsoluteUri);
    }
}
