using System.Linq;

namespace PolishOpenData.Sejm.Tests;

public class SejmPrintRefsTests
{
    [Fact]
    public void ThreeTokens_FromAList()
    {
        var refs = SejmPrintRefs.FromTitle("Sprawozdanie o projekcie (druki nr 1344, 2100 i 2100-A)");
        Assert.Equal(new[] { "1344", "2100", "2100-A" }, refs.ToArray());
    }

    [Fact]
    public void DrukNr_Single()
    {
        Assert.Equal(new[] { "401" }, SejmPrintRefs.FromTitle("Informacja (druk nr 401)").ToArray());
    }

    [Theory]
    [InlineData("Głosowanie nad całością projektu")]
    [InlineData("")]
    [InlineData(null)]
    public void NoMatch_IsEmpty(string? text)
    {
        Assert.Empty(SejmPrintRefs.FromTitle(text));
    }

    [Fact]
    public void TokenEquality_13440IsNot1344()
    {
        var refs = SejmPrintRefs.FromTitle("druk nr 13440");
        Assert.DoesNotContain("1344", refs);
        Assert.Contains("13440", refs);
    }

    [Fact]
    public void DruksPlural_Genitive()
    {
        Assert.Equal(new[] { "10", "11" }, SejmPrintRefs.FromTitle("projekt (druków nr 10, 11)").ToArray());
    }
}
