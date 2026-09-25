using System;
using System.Text.Json;
using PolishOpenData;

namespace PolishOpenData.Core.Tests;

public class RegonTests
{
    [Theory]
    [InlineData("610188201", "610188201", 9)]            // ORLEN
    [InlineData("610 188 201", "610188201", 9)]
    [InlineData("545772924", "545772924", 9)]
    [InlineData("61018820100010", "61018820100010", 14)] // local unit of 610188201
    public void Parses_valid_input(string input, string expected, int length)
    {
        Assert.True(Regon.TryParse(input, out var regon));
        Assert.Equal(expected, regon.ToString());
        Assert.Equal(length, regon.Length);
        Assert.Equal(length == 14, regon.IsLocalUnit);
    }

    [Theory]
    [InlineData("610188202")]         // wrong check digit
    [InlineData("61018820100000")]    // KRS-style "9 digits + 00000": fails the 14-digit checksum
    [InlineData("61018820200010")]    // 14-digit whose first 9 digits are invalid
    [InlineData("6101882")]
    [InlineData("6101882011")]        // 10 digits
    [InlineData("PL610188201")]       // no country prefix for REGON
    [InlineData("")]
    public void Rejects_invalid_input(string input)
    {
        Assert.False(Regon.TryParse(input, out var regon));
        Assert.True(regon.IsEmpty);
        Assert.Equal(0, regon.Length);
        Assert.Throws<FormatException>(() => Regon.Parse(input));
    }

    [Fact]
    public void Remainder_ten_gives_check_digit_zero()
    {
        // 9 digits: weights 8 9 2 3 4 5 6 7 on 1 2 3 4 5 6 7 4 give
        // 8 + 18 + 6 + 12 + 20 + 30 + 42 + 28 = 164 = 14 * 11 + 10; remainder 10 means check digit 0.
        Assert.True(Regon.TryParse("123456740", out var regon));
        Assert.Equal("123456740", regon.ToString());
        Assert.False(Regon.TryParse("123456741", out _));

        // 14 digits: 610188201 (valid) + 7000, weights 2 4 8 5 0 9 7 3 6 1 2 4 8 give
        // 12 + 4 + 0 + 5 + 0 + 72 + 14 + 0 + 6 + 7 + 0 + 0 + 0 = 120 = 10 * 11 + 10; check digit 0 again.
        Assert.True(Regon.TryParse("61018820170000", out var local));
        Assert.True(local.IsLocalUnit);
        Assert.Equal(Regon.Parse("610188201"), local.BaseRegon);
        Assert.False(Regon.TryParse("61018820170001", out _));
    }

    [Fact]
    public void Base_regon_of_local_unit_is_first_nine_digits()
    {
        Assert.Equal(Regon.Parse("610188201"), Regon.Parse("61018820100010").BaseRegon);
        Assert.Equal(Regon.Parse("610188201"), Regon.Parse("610188201").BaseRegon);
    }

    [Fact]
    public void Serializes_as_canonical_string()
    {
        Assert.Equal("\"610188201\"", JsonSerializer.Serialize(Regon.Parse("610 188 201")));
        Assert.Equal(Regon.Parse("61018820100010"), JsonSerializer.Deserialize<Regon>("\"61018820100010\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Regon>("\"610188202\""));
    }
}
