using System;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Eli.Tests;

public class EliHtmlTextTests
{
    [Fact]
    public void Fragment415_BecomesTwoCleanLines()
    {
        var text = EliHtmlText.ToPlainText(Fixture.Read("eli/fragment-arti-415.html"), 20_000, out var truncated);

        Assert.Equal("Art. 415.\nKto z winy swej wyrządził drugiemu szkodę, obowiązany jest do jej naprawienia.", text);
        Assert.False(truncated);
    }

    [Fact]
    public void Fragment417_KeepsParagraphsAndDropsAllMarkup()
    {
        var text = EliHtmlText.ToPlainText(Fixture.Read("eli/fragment-arti-417.html"), 20_000, out _);

        Assert.StartsWith("Art. 417.\n§ 1.", text, StringComparison.Ordinal);
        Assert.DoesNotContain('<', text);
        Assert.DoesNotContain('>', text);
        Assert.DoesNotContain("&nbsp;", text, StringComparison.Ordinal);
        Assert.DoesNotContain(' ', text);
        Assert.Contains("\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptsStylesAndComments_AreDropped_SuperscriptsAndEntitiesDecoded()
    {
        var html = "<div><script>alert('x')</script><style>p{color:red}</style><!-- hidden --><p>Art.&nbsp;446<sup>1</sup>.</p><br/>Tekst &amp; więcej &#8211; &#x142;</div>";

        var text = EliHtmlText.ToPlainText(html, 20_000, out _);

        Assert.Equal("Art. 446^1.\nTekst & więcej – ł", text);
    }

    [Fact]
    public void WhitespaceIsCollapsed_EmptyLinesDropped()
    {
        var text = EliHtmlText.ToPlainText("<div>\n   <p>  a   \t b </p>\n\n\n<p></p><p>c</p></div>", 20_000, out _);

        Assert.Equal("a b\nc", text);
    }

    [Fact]
    public void LongText_IsCutAtTheCapWithAFlag()
    {
        var html = "<p>" + new string('x', 30_000) + "</p>";

        var text = EliHtmlText.ToPlainText(html, 20_000, out var truncated);

        Assert.Equal(20_000, text.Length);
        Assert.True(truncated);
    }

    [Fact]
    public void TextAtExactlyTheCap_IsNotTruncated()
    {
        var text = EliHtmlText.ToPlainText("<p>" + new string('x', 100) + "</p>", 100, out var truncated);

        Assert.Equal(100, text.Length);
        Assert.False(truncated);
    }

    [Fact]
    public void RawAngleBracketsInText_AreNotTreatedAsTagsAfterDecoding()
    {
        var text = EliHtmlText.ToPlainText("<p>x &lt;b&gt; y</p>", 20_000, out _);

        Assert.Equal("x <b> y", text);
    }
}
