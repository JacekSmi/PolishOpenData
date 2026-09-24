using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using PolishOpenData.Internal;

namespace PolishOpenData;

/// <summary>
/// National Court Register number (KRS, <i>numer w Krajowym Rejestrze Sądowym</i>): up to 10 digits, always
/// represented zero-padded to 10 digits. KRS numbers have no check digit. <c>default(KrsNumber)</c> is empty.
/// </summary>
[JsonConverter(typeof(KrsNumberJsonConverter))]
public readonly struct KrsNumber : IEquatable<KrsNumber>, IComparable<KrsNumber>
#if NET
    , ISpanParsable<KrsNumber>, ISpanFormattable
#endif
{
    private readonly string? _value;

    private KrsNumber(string value) => _value = value;

    /// <summary>True for <c>default(KrsNumber)</c>.</summary>
    public bool IsEmpty => _value is null;

    /// <summary>Parses a KRS number (1–10 digits, not all zeros) and pads it to 10 digits.</summary>
    /// <exception cref="FormatException">The input is not a KRS number.</exception>
    public static KrsNumber Parse(string s) =>
        TryParse(s, out var result) ? result : throw new FormatException($"'{s}' is not a valid KRS number.");

    /// <summary>Tries to parse a KRS number (1–10 digits, not all zeros) and pads it to 10 digits.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out KrsNumber result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), out result);
    }

    /// <summary>Tries to parse a KRS number from characters and pads it to 10 digits.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out KrsNumber result)
    {
        result = default;
        Span<char> digits = stackalloc char[10];
        var count = DigitText.Extract(s, digits, allowPlPrefix: false);
        if (count < 1)
        {
            return false;
        }

        var allZeros = true;
        for (var i = 0; i < count; i++)
        {
            if (digits[i] != '0')
            {
                allZeros = false;
                break;
            }
        }

        if (allZeros)
        {
            return false;
        }

        result = new KrsNumber(new string('0', 10 - count) + digits.Slice(0, count).ToString());
        return true;
    }

    /// <summary>The zero-padded 10 digits, or an empty string for <c>default</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc/>
    public bool Equals(KrsNumber other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is KrsNumber other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    /// <inheritdoc/>
    public int CompareTo(KrsNumber other) => string.CompareOrdinal(_value, other._value);

    /// <summary>Equality.</summary>
    public static bool operator ==(KrsNumber left, KrsNumber right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(KrsNumber left, KrsNumber right) => !left.Equals(right);

    /// <summary>Ordinal less-than.</summary>
    public static bool operator <(KrsNumber left, KrsNumber right) => left.CompareTo(right) < 0;

    /// <summary>Ordinal less-than-or-equal.</summary>
    public static bool operator <=(KrsNumber left, KrsNumber right) => left.CompareTo(right) <= 0;

    /// <summary>Ordinal greater-than.</summary>
    public static bool operator >(KrsNumber left, KrsNumber right) => left.CompareTo(right) > 0;

    /// <summary>Ordinal greater-than-or-equal.</summary>
    public static bool operator >=(KrsNumber left, KrsNumber right) => left.CompareTo(right) >= 0;

#if NET
    static KrsNumber IParsable<KrsNumber>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<KrsNumber>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out KrsNumber result) =>
        TryParse(s, out result);

    static KrsNumber ISpanParsable<KrsNumber>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        TryParse(s, out var result) ? result : throw new FormatException("Not a valid KRS number.");

    static bool ISpanParsable<KrsNumber>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out KrsNumber result) =>
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
