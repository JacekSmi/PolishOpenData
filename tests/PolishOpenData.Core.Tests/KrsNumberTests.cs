using System;
using System.Text.Json;
using PolishOpenData;

namespace PolishOpenData.Core.Tests;

public class KrsNumberTests
{
    [Theory]
    [InlineData("0000028860", "0000028860")]
    [InlineData("28860", "0000028860")]
    [InlineData(" 28860 ", "0000028860")]
    [InlineData("1268296", "0001268296")]
    [InlineData("9999999999", "9999999999")]
    public void Parses_and_pads(string input, string expected)
    {
        Assert.True(KrsNumber.TryParse(input, out var krs));
        Assert.Equal(expected, krs.ToString());
        Assert.Equal(krs, KrsNumber.Parse(expected));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0000000000")]
    [InlineData("12345678901")]
    [InlineData("12a45")]
    [InlineData("")]
    public void Rejects_invalid_input(string input)
    {
        Assert.False(KrsNumber.TryParse(input, out var krs));
        Assert.True(krs.IsEmpty);
        Assert.Throws<FormatException>(() => KrsNumber.Parse(input));
    }

    [Fact]
    public void Serializes_padded()
    {
        Assert.Equal("\"0000028860\"", JsonSerializer.Serialize(KrsNumber.Parse("28860")));
        Assert.Equal(KrsNumber.Parse("28860"), JsonSerializer.Deserialize<KrsNumber>("\"28860\""));
    }
}
