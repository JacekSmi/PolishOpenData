using PolishOpenData.Bdl;

namespace PolishOpenData.Bdl.Tests;

public class BdlModelTests
{
    [Fact]
    public void Teryt_IsDerivedPerLevel()
    {
        Assert.Equal("1261011", new BdlUnit("011212161011", "Kraków", "011212161000", BdlLevel.Gmina, "1").Teryt);
        Assert.Equal("1261", new BdlUnit("011212161000", "Powiat m. Kraków", "011212100000", BdlLevel.Powiat, "2").Teryt);
        Assert.Equal("12", new BdlUnit("011200000000", "MAŁOPOLSKIE", null, BdlLevel.Voivodeship, null).Teryt);
        Assert.Null(new BdlUnit("011212100000", "PODREGION MIASTO KRAKÓW", "011210000000", BdlLevel.Subregion, null).Teryt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(50)]
    [InlineData(91)]
    public void Attributes_NoValueIdsNullTheValue(int id)
    {
        Assert.Null(BdlAttributes.ValueFor(id, 0));
        Assert.Equal(BdlValueKind.NoValue, BdlAttributes.Get(id).Kind);
    }

    [Fact]
    public void Attributes_FlaggedValuesKeepValue()
    {
        Assert.Equal(0, BdlAttributes.ValueFor(7, 0));
        Assert.Equal("a", BdlAttributes.Get(7).Symbol);
        Assert.Equal(12.5, BdlAttributes.ValueFor(9, 12.5));
        Assert.Equal("s", BdlAttributes.Get(9).Symbol);
    }

    [Fact]
    public void Attributes_UnknownIdKeepsValue()
    {
        Assert.Equal(BdlValueKind.Unknown, BdlAttributes.Get(999).Kind);
        Assert.Equal(3, BdlAttributes.ValueFor(999, 3));
    }
}
