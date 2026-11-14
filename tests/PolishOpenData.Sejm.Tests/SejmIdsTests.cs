using System;

namespace PolishOpenData.Sejm.Tests;

public class SejmIdsTests
{
    [Theory]
    [InlineData(" 1344 ", "1344")]
    [InlineData("32-s", "32-s")]
    [InlineData("128-BA", "128-BA")]
    [InlineData("19-001", "19-001")]
    public void PrintNumber_TrimsAcceptsSuffixes(string input, string expected)
    {
        Assert.Equal(expected, SejmIds.Print(input));
    }

    [Theory]
    [InlineData("1344/../x")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1344?x=1")]
    public void PrintNumber_RejectsPathCharsAndGarbage(string input)
    {
        Assert.Throws<ArgumentException>(() => SejmIds.Print(input));
    }

    [Fact]
    public void PrintNumber_RejectsNull()
    {
        Assert.Throws<ArgumentException>(() => SejmIds.Print(null!));
    }

    [Theory]
    [InlineData("KO")]
    [InlineData("niez.")]
    [InlineData("Polska2050-TD")]
    [InlineData("Razem")]
    [InlineData("Lewica")]
    public void Club_AcceptsKnownShapes(string id)
    {
        Assert.Equal(id, SejmIds.Club(id));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("KO?x")]
    public void Club_Rejects(string id)
    {
        Assert.Throws<ArgumentException>(() => SejmIds.Club(id));
    }

    [Theory]
    [InlineData("ASW")]
    [InlineData("SUE01")]
    public void Committee_Accepts(string code)
    {
        Assert.Equal(code, SejmIds.Committee(code));
    }

    [Theory]
    [InlineData("asw")]
    [InlineData("")]
    [InlineData("A/B")]
    [InlineData("TOOLONGCODE1")]
    public void Committee_Rejects(string code)
    {
        Assert.Throws<ArgumentException>(() => SejmIds.Committee(code));
    }
}
