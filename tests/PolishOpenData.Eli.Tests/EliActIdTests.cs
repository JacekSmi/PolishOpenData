using System;
using PolishOpenData.Eli;

namespace PolishOpenData.Eli.Tests;

public class EliActIdTests
{
    [Theory]
    [InlineData("DU/2024/1061")]
    [InlineData("WDU20240001061")]
    [InlineData("Dz.U. 2024 poz. 1061")]
    [InlineData("dz.u. 2024 poz 1061")]
    [InlineData(" DU/2024/1061 ")]
    public void Parse_AcceptsThreeFormatsOfDziennikUstaw(string text)
    {
        var id = EliActId.Parse(text);

        Assert.Equal("DU", id.Publisher);
        Assert.Equal(2024, id.Year);
        Assert.Equal(1061, id.Position);
        Assert.Equal("DU/2024/1061", id.ToString());
    }

    [Theory]
    [InlineData("MP/2023/1261")]
    [InlineData("WMP20230001261")]
    [InlineData("M.P. 2023 poz. 1261")]
    public void Parse_AcceptsMonitorPolski(string text)
    {
        Assert.Equal("MP/2023/1261", EliActId.Parse(text).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DU/2024")]
    [InlineData("XX/abc/1")]
    [InlineData("DU/2024/0")]
    [InlineData("DU/1700/5")]
    [InlineData("D/2024/5")]
    [InlineData("DU/2024/5/6")]
    public void TryParse_RejectsGarbage(string text)
    {
        Assert.False(EliActId.TryParse(text, out _));
        Assert.Throws<FormatException>(() => EliActId.Parse(text));
    }

    [Fact]
    public void TryParse_RejectsNull()
    {
        Assert.False(EliActId.TryParse(null, out var id));
        Assert.Equal(default, id);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(EliActId.Parse("DU/2024/1061"), EliActId.Parse("WDU20240001061"));
        Assert.NotEqual(EliActId.Parse("DU/2024/1061"), EliActId.Parse("DU/2024/1062"));
    }

    [Theory]
    [InlineData("415", "415")]
    [InlineData("446^1", "446_1")]
    [InlineData("446¹", "446_1")]
    [InlineData("446_1", "446_1")]
    [InlineData("art. 415", "415")]
    [InlineData("Art. 21a", "21a")]
    [InlineData(" 415 ", "415")]
    [InlineData("3²", "3_2")]
    public void Normalize_ArticleNames(string input, string expected)
    {
        Assert.Equal(expected, EliArticle.Normalize(input));
    }

    [Theory]
    [InlineData("4l5")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("art.")]
    [InlineData("415/416")]
    [InlineData("-1")]
    public void Normalize_RejectsWhatIsNotAnArticleNumber(string? input)
    {
        Assert.Null(EliArticle.Normalize(input));
    }
}
