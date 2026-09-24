using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;

namespace PolishOpenData;

/// <summary>
/// Land-registry number (<i>numer księgi wieczystej</i>, KW) in the form <c>CCCC/NNNNNNNN/K</c>: a 4-character court
/// code, an 8-digit register number and a check digit. Offline validation only — this type never contacts the
/// eKW portal. Port of the validation logic of pyekw (github.com/mhajder/pyekw, MIT) with the 2026 court table.
/// <c>default(KwNumber)</c> is empty.
/// </summary>
[JsonConverter(typeof(KwNumberJsonConverter))]
public readonly struct KwNumber : IEquatable<KwNumber>, IComparable<KwNumber>
#if NET
    , ISpanParsable<KwNumber>, ISpanFormattable
#endif
{
    // X=10, A=11 ... Z=33 in this order; Q and V are not used.
    private const string Letters = "XABCDEFGHIJKLMNOPRSTUWYZ";
    private static readonly int[] Weights = [1, 3, 7, 1, 3, 7, 1, 3, 7, 1, 3, 7];
    private readonly string? _value;

    private KwNumber(string value) => _value = value;

    /// <summary>True for <c>default(KwNumber)</c>.</summary>
    public bool IsEmpty => _value is null;

    /// <summary>The 4-character court code, e.g. <c>WA1M</c>.</summary>
    public string CourtCode => _value is null ? string.Empty : _value.Substring(0, 4);

    /// <summary>The 8-digit register number.</summary>
    public string Number => _value is null ? string.Empty : _value.Substring(5, 8);

    /// <summary>The check digit (0–9), or -1 when empty.</summary>
    public int CheckDigit => _value is null ? -1 : _value[14] - '0';

    /// <summary>The court for <see cref="CourtCode"/>, or <c>null</c> when the code is not in the known table.</summary>
    public KwCourt? Court => _value is null ? null : KwCourts.Find(CourtCode);

    /// <summary>Parses a KW number; accepts lower case, spaces, <c>/</c> or <c>-</c> separators, or no separators.</summary>
    /// <exception cref="FormatException">The input is not a valid KW number.</exception>
    public static KwNumber Parse(string s) =>
        TryParse(s, out var result) ? result : throw new FormatException($"'{s}' is not a valid KW number.");

    /// <summary>Tries to parse a KW number.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out KwNumber result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), out result);
    }

    /// <summary>Tries to parse a KW number from characters.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out KwNumber result)
    {
        result = default;
        var chars = new char[13];
        if (!TryNormalize(s, chars) || !IsWellFormedCourtCode(chars))
        {
            return false;
        }

        for (var i = 4; i < 13; i++)
        {
            if (chars[i] < '0' || chars[i] > '9')
            {
                return false;
            }
        }

        if (Compute(chars) != chars[12] - '0')
        {
            return false;
        }

        result = new KwNumber(Format(new string(chars, 0, 4), new string(chars, 4, 8), chars[12] - '0'));
        return true;
    }

    /// <summary>Computes the check digit for a court code and an 8-digit register number.</summary>
    /// <exception cref="ArgumentException">The code is not 4 characters with KW values, or the number is not 8 digits.</exception>
    public static int ComputeCheckDigit(string courtCode, string number) =>
        TryComputeCheckDigit(courtCode, number, out var digit)
            ? digit
            : throw new ArgumentException("The court code must be 4 characters (digits or KW letters) and the number 8 digits.");

    /// <summary>Tries to compute the check digit; digits are allowed in the code so that typos can be analysed.</summary>
    public static bool TryComputeCheckDigit(string? courtCode, string? number, out int checkDigit)
    {
        checkDigit = -1;
        if (courtCode is null || number is null || courtCode.Length != 4 || number.Length != 8)
        {
            return false;
        }

        var chars = new char[12];
        for (var i = 0; i < 4; i++)
        {
            chars[i] = char.ToUpperInvariant(courtCode[i]);
            if (Value(chars[i]) < 0)
            {
                return false;
            }
        }

        for (var i = 0; i < 8; i++)
        {
            if (number[i] < '0' || number[i] > '9')
            {
                return false;
            }

            chars[4 + i] = number[i];
        }

        checkDigit = Compute(chars);
        return true;
    }

    /// <summary>
    /// Suggests likely corrections for a mistyped KW number: one confusable swap in the court code (0/O, 1/I, 5/S, 8/B,
    /// 2/Z) and confusable letters in the number converted to digits. A suggestion is returned only when its court code
    /// is known and the printed check digit matches. Returns an empty list for a valid input or when only the check
    /// digit is wrong (there is no safe guess then).
    /// </summary>
    public static IReadOnlyList<KwNumber> SuggestCorrections(string? input)
    {
        if (input is null || TryParse(input, out _))
        {
            return [];
        }

        var chars = new char[13];
        if (!TryNormalize(input.AsSpan(), chars))
        {
            return [];
        }

        var number = new char[8];
        for (var i = 0; i < 8; i++)
        {
            var digit = ToDigit(chars[4 + i]);
            if (digit < 0)
            {
                return [];
            }

            number[i] = (char)('0' + digit);
        }

        var check = ToDigit(chars[12]);
        if (check < 0)
        {
            return [];
        }

        var numberText = new string(number);
        var results = new List<KwNumber>();
        foreach (var code in CodeVariants(new string(chars, 0, 4)))
        {
            if (KwCourts.Find(code) is null || !TryComputeCheckDigit(code, numberText, out var expected) || expected != check)
            {
                continue;
            }

            var candidate = new KwNumber(Format(code, numberText, check));
            if (!results.Contains(candidate))
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    private static IEnumerable<string> CodeVariants(string code)
    {
        yield return code;
        for (var i = 0; i < 4; i++)
        {
            var swapped = Confusable(code[i]);
            if (swapped != '\0')
            {
                var chars = code.ToCharArray();
                chars[i] = swapped;
                yield return new string(chars);
            }
        }
    }

    private static char Confusable(char c) => c switch
    {
        '0' => 'O',
        'O' => '0',
        '1' => 'I',
        'I' => '1',
        '5' => 'S',
        'S' => '5',
        '8' => 'B',
        'B' => '8',
        '2' => 'Z',
        'Z' => '2',
        _ => '\0',
    };

    private static int ToDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        'O' => 0,
        'I' => 1,
        'S' => 5,
        'B' => 8,
        'Z' => 2,
        _ => -1,
    };

    // Uppercases and removes spaces; accepts either no separators or '/' or '-' exactly after the code and the number.
    private static bool TryNormalize(ReadOnlySpan<char> input, char[] destination)
    {
        var count = 0;
        var separatorAfterCode = false;
        var separatorAfterNumber = false;
        foreach (var raw in input.Trim())
        {
            var c = char.ToUpperInvariant(raw);
            if (c == ' ')
            {
                continue;
            }

            if (c == '/' || c == '-')
            {
                if (count == 4 && !separatorAfterCode)
                {
                    separatorAfterCode = true;
                    continue;
                }

                if (count == 12 && !separatorAfterNumber)
                {
                    separatorAfterNumber = true;
                    continue;
                }

                return false;
            }

            if (count == 13)
            {
                return false;
            }

            destination[count++] = c;
        }

        return count == 13 && separatorAfterCode == separatorAfterNumber;
    }

    // Value() is 10..33 for letters of the table, 0..9 for digits and -1 otherwise.
    private static bool IsWellFormedCourtCode(char[] chars) =>
        Value(chars[0]) >= 10 &&
        Value(chars[1]) >= 10 &&
        chars[2] >= '1' && chars[2] <= '9' &&
        Value(chars[3]) >= 10;

    private static int Value(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }

        var index = Letters.IndexOf(c);
        return index >= 0 ? 10 + index : -1;
    }

    private static int Compute(char[] codeAndNumber)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += Value(codeAndNumber[i]) * Weights[i];
        }

        return sum % 10;
    }

    private static string Format(string code, string number, int check) =>
        code + "/" + number + "/" + check.ToString(CultureInfo.InvariantCulture);

    /// <summary>The number as <c>CCCC/NNNNNNNN/K</c>, or an empty string for <c>default</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc/>
    public bool Equals(KwNumber other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is KwNumber other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    /// <inheritdoc/>
    public int CompareTo(KwNumber other) => string.CompareOrdinal(_value, other._value);

    /// <summary>Equality.</summary>
    public static bool operator ==(KwNumber left, KwNumber right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(KwNumber left, KwNumber right) => !left.Equals(right);

    /// <summary>Ordinal less-than.</summary>
    public static bool operator <(KwNumber left, KwNumber right) => left.CompareTo(right) < 0;

    /// <summary>Ordinal less-than-or-equal.</summary>
    public static bool operator <=(KwNumber left, KwNumber right) => left.CompareTo(right) <= 0;

    /// <summary>Ordinal greater-than.</summary>
    public static bool operator >(KwNumber left, KwNumber right) => left.CompareTo(right) > 0;

    /// <summary>Ordinal greater-than-or-equal.</summary>
    public static bool operator >=(KwNumber left, KwNumber right) => left.CompareTo(right) >= 0;

#if NET
    static KwNumber IParsable<KwNumber>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<KwNumber>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out KwNumber result) =>
        TryParse(s, out result);

    static KwNumber ISpanParsable<KwNumber>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        TryParse(s, out var result) ? result : throw new FormatException("Not a valid KW number.");

    static bool ISpanParsable<KwNumber>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out KwNumber result) =>
        TryParse(s, out result);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <inheritdoc/>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var value = ToString();
        if (value.AsSpan().TryCopyTo(destination))
        {
            charsWritten = value.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }
#endif
}
