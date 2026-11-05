using PolishOpenData.Demo.Search;

namespace PolishOpenData.Demo.Tests;

public class KwShapeTests
{
    // The whole text is a KW: both rules say so. Spaces, case and '/' or '-' do not matter; the check digit is not verified.
    [Theory]
    [InlineData("WA1M/00012345/6")]
    [InlineData(" wa1m 00012345 6 ")]
    [InlineData("WA1M-00012345-6")]
    [InlineData("WA1M000123456")]
    public void A_text_that_is_a_KW_matches_and_contains(string text)
    {
        Assert.True(KwShape.Matches(text));
        Assert.True(KwShape.Contains(text));
    }

    // A KW inside a longer text: only Contains (the link label rule) says so; Matches (the typed-input rule, as in the MCP tool) does not.
    [Theory]
    [InlineData("Krucza 50 WA1M/00012345/6")]
    [InlineData("WA1M/00012345/6 Krucza 50")]
    [InlineData("Krucza 50, WA1M-00012345-6, Warszawa")]
    [InlineData("Krucza 50 wa1m / 00012345 / 6 Warszawa")]
    [InlineData("Krucza 50 WA1M 00012345 6")]
    public void A_KW_inside_a_longer_text_is_contained_but_not_matched(string text)
    {
        Assert.False(KwShape.Matches(text));
        Assert.True(KwShape.Contains(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("00-001 Warszawa")]
    [InlineData("Warszawa, Krucza 50")]
    [InlineData("Krucza 50 12345")]
    [InlineData("12345678")] // eight digits
    [InlineData("WA1M/00012345")] // no check digit
    [InlineData("WA1M/0001234/56")] // seven digits, then two
    [InlineData("WA1M\t00012345\t6")] // only spaces are removed, as in IdentifierTools
    public void Text_that_only_resembles_a_KW_is_neither(string text)
    {
        Assert.False(KwShape.Matches(text));
        Assert.False(KwShape.Contains(text));
    }

    // The accepted false positive for a label: dropping a label is harmless. Typed input never meets it (a long number there is simply looked up).
    [Theory]
    [InlineData("ul. 1234567890123")] // thirteen digits
    [InlineData("Warszawa 123456789")] // four letters of the town and nine digits
    public void A_long_number_in_a_text_is_contained(string text)
    {
        Assert.True(KwShape.Contains(text));
        Assert.False(KwShape.Matches(text));
    }
}
