using System;
using System.Security.Cryptography;
using System.Text;

namespace PolishOpenData.BialaLista.Offline.Tests;

public sealed class FlatFileHashTests
{
    private static string ToHexLower(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static byte[] Sha512(string text)
    {
#if NET
        return SHA512.HashData(Encoding.UTF8.GetBytes(text));
#else
        using var sha = SHA512.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(text));
#endif
    }

    [Fact]
    public void Spec_example_5000_rounds() => Assert.Equal(
        "f8b915776eab735fdd10266b2e66068447904852b82c30eeb6de30703a087eb17ea4c4a37630494607194ddb9354c1211bd984fb5f4d9cff95f5a24ed52065e7",
        ToHexLower(FlatFileHash.Compute("20191018143572123034102012221314181237774212", 5000)));

    [Fact]
    public void One_round_is_plain_sha512() => Assert.Equal(Sha512("abc"), FlatFileHash.Compute("abc", 1));

    [Fact]
    public void Rounds_hash_lowercase_hex_text()
    {
        const string x = "20191018143572123034102012221314181237774212";
        var hex = ToHexLower(FlatFileHash.Compute(x, 1));

        var two = FlatFileHash.Compute(x, 2);

        Assert.Equal(Sha512(hex), two);
        Assert.NotEqual(Sha512(hex.ToUpperInvariant()), two);
    }

    [Fact]
    public void Input_is_date_nip_account() => Assert.Equal(
        "20191018143572123034102012221314181237774212",
        FlatFileHash.Input(new DateOnly(2019, 10, 18), "1435721230", "34102012221314181237774212"));

    [Fact]
    public void Spec_mask_example() => Assert.Equal(
        "XX72123370XXXXXXX022XXXXXX",
        FlatFileHash.ApplyMask("20721233708680000022663112", "XX72123370XXXXXXXYYYXXXXXX"));

    [Fact]
    public void Mask_of_other_bank_returns_null() => Assert.Null(
        FlatFileHash.ApplyMask("06160011271843983820000034", "XX10201026XXXXXXXXXXYYYYXX"));

    [Fact]
    public void Hex_roundtrip_and_uppercase_rejected()
    {
        var digest = FlatFileHash.Compute("abc", 1);
        var hex = ToHexLower(digest);
        var parsed = new byte[64];

        Assert.True(FlatFileHash.TryParseHex(hex.AsSpan(), parsed));
        Assert.Equal(digest, parsed);
        Assert.False(FlatFileHash.TryParseHex(hex.ToUpperInvariant().AsSpan(), parsed));
        Assert.False(FlatFileHash.TryParseHex(hex.AsSpan(0, 126), parsed));
    }
}
