using System;
using System.Text.Json;
using PolishOpenData;

namespace PolishOpenData.Core.Tests;

public class NipTests
{
    [Theory]
    [InlineData("7740001454", "7740001454")]      // ORLEN
    [InlineData("774-000-14-54", "7740001454")]
    [InlineData("774 000 14 54", "7740001454")]
    [InlineData("PL7740001454", "7740001454")]
    [InlineData("pl 774-000-14-54", "7740001454")]
    [InlineData(" 5260251049 ", "5260251049")]    // PZU
    public void Parses_valid_input(string input, string expected)
    {
        Assert.True(Nip.TryParse(input, out var nip));
        Assert.Equal(expected, nip.ToString());
        Assert.False(nip.IsEmpty);
        Assert.Equal(nip, Nip.Parse(input));
    }

    [Theory]
    [InlineData("7740001455")]    // wrong check digit
    [InlineData("774000145")]     // 9 digits
    [InlineData("77400014544")]   // 11 digits
    [InlineData("77400014a4")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DE7740001454")]
    public void Rejects_invalid_input(string input)
    {
        Assert.False(Nip.TryParse(input, out var nip));
        Assert.True(nip.IsEmpty);
        Assert.Throws<FormatException>(() => Nip.Parse(input));
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.False(Nip.TryParse((string?)null, out _));
    }

    [Fact]
    public void Checksum_edge_cases()
    {
        // weights 6,5,7,2,3,4,5,6,7: digits ...,1,1 give 6 + 7 = 13, 13 % 11 = 2, so the check digit is 2
        Assert.True(Nip.TryParse("0000000112", out _));

        // digit 3 in position 9 gives 3 * 7 = 21, 21 % 11 = 10: no check digit can make this valid
        for (var last = 0; last <= 9; last++)
        {
            Assert.False(Nip.TryParse("000000003" + last.ToString(System.Globalization.CultureInfo.InvariantCulture), out _));
        }
    }

    [Fact]
    public void Default_is_empty()
    {
        Assert.True(default(Nip).IsEmpty);
        Assert.Equal(string.Empty, default(Nip).ToString());
    }

    [Fact]
    public void Equality_and_ordering()
    {
        var a = Nip.Parse("5260251049");
        var b = Nip.Parse("7740001454");
        Assert.True(a < b);
        Assert.True(b >= a);
        Assert.NotEqual(a, b);
        Assert.Equal(a, Nip.Parse("PL5260251049"));
        Assert.Equal(a.GetHashCode(), Nip.Parse("526-025-10-49").GetHashCode());
    }

    [Fact]
    public void Serializes_as_canonical_string()
    {
        var nip = Nip.Parse("774-000-14-54");
        Assert.Equal("\"7740001454\"", JsonSerializer.Serialize(nip));
        Assert.Equal(nip, JsonSerializer.Deserialize<Nip>("\"PL7740001454\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Nip>("\"7740001455\""));
    }
}
