using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>Hashing and masking of whitelist entries exactly as the Ministry of Finance flat-file spec defines them. Stateless, so safe to call from any number of threads.</summary>
internal static class FlatFileHash
{
    private const int DigestLength = 64;
    private const int HexLength = DigestLength * 2;

    /// <summary>SHA-512 applied <paramref name="transformations"/> times in total; every round after the first hashes the lowercase hex text of the previous digest.</summary>
    public static byte[] Compute(string input, int transformations)
    {
        if (transformations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(transformations), transformations, "At least one round is required.");
        }

        var digest = new byte[DigestLength];
        var hex = new byte[HexLength];
#if NET
        SHA512.HashData(Encoding.UTF8.GetBytes(input), digest);
        for (var round = 1; round < transformations; round++)
        {
            ToHexAscii(digest, hex);
            SHA512.HashData(hex, digest);
        }
#else
        using var sha = SHA512.Create();
        digest = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        for (var round = 1; round < transformations; round++)
        {
            ToHexAscii(digest, hex);
            digest = sha.ComputeHash(hex);
        }
#endif
        return digest;
    }

    /// <summary>The hash input: <c>yyyyMMdd + NIP + NRB</c>. Works on raw strings; checksums are not validated here.</summary>
    public static string Input(DateOnly date, string nip, string account) =>
        date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + nip + account;

    /// <summary>Applies a flat-file mask to an NRB (<c>Y</c> copies the account digit, anything else stays); <c>null</c> when the mask belongs to another bank (characters 3-10 differ).</summary>
    public static string? ApplyMask(string nrb, string mask)
    {
        if (nrb.Length != mask.Length || nrb.Length < 10 || string.CompareOrdinal(nrb, 2, mask, 2, 8) != 0)
        {
            return null;
        }

        var chars = mask.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == 'Y')
            {
                chars[i] = nrb[i];
            }
        }
        return new string(chars);
    }

    /// <summary>Parses 128 lowercase hex characters into 64 bytes; uppercase or wrong lengths are rejected.</summary>
    public static bool TryParseHex(ReadOnlySpan<char> hex128, Span<byte> digest64)
    {
        if (hex128.Length != HexLength || digest64.Length != DigestLength)
        {
            return false;
        }

        for (var i = 0; i < DigestLength; i++)
        {
            var hi = Nibble(hex128[2 * i]);
            var lo = Nibble(hex128[(2 * i) + 1]);
            if ((hi | lo) < 0)
            {
                return false;
            }
            digest64[i] = (byte)((hi << 4) | lo);
        }
        return true;
    }

    private static int Nibble(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1,
    };

    private static void ToHexAscii(byte[] digest, byte[] hex)
    {
        for (var i = 0; i < digest.Length; i++)
        {
            hex[2 * i] = (byte)HexChar(digest[i] >> 4);
            hex[(2 * i) + 1] = (byte)HexChar(digest[i] & 0xF);
        }
    }

    private static char HexChar(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
}
