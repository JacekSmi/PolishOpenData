using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using PolishOpenData.Internal;

namespace PolishOpenData;

/// <summary>
/// Polish tax identification number (NIP, <i>numer identyfikacji podatkowej</i>): 10 digits with a mod-11 check digit.
/// <c>default(Nip)</c> is empty and never valid.
/// </summary>
[JsonConverter(typeof(NipJsonConverter))]
public readonly struct Nip : IEquatable<Nip>, IComparable<Nip>
#if NET
    , ISpanParsable<Nip>, ISpanFormattable
#endif
{
    private static readonly int[] Weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
    private readonly string? _value;

    private Nip(string value) => _value = value;

    /// <summary>True for <c>default(Nip)</c>.</summary>
    public bool IsEmpty => _value is null;

    /// <summary>Parses a NIP; accepts spaces, dashes and a <c>PL</c> prefix.</summary>
    /// <exception cref="FormatException">The input is not a valid NIP.</exception>
    public static Nip Parse(string s) =>
        TryParse(s, out var result) ? result : throw new FormatException($"'{s}' is not a valid NIP.");

    /// <summary>Tries to parse a NIP; accepts spaces, dashes and a <c>PL</c> prefix.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out Nip result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), out result);
    }

    /// <summary>Tries to parse a NIP from characters; accepts spaces, dashes and a <c>PL</c> prefix.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out Nip result)
    {
        result = default;
        Span<char> digits = stackalloc char[10];
        if (DigitText.Extract(s, digits, allowPlPrefix: true) != 10)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (digits[i] - '0') * Weights[i];
        }

        var check = sum % 11;
        if (check == 10 || check != digits[9] - '0')
        {
            return false;
        }

        result = new Nip(digits.ToString());
        return true;
    }

    /// <summary>The 10 digits, or an empty string for <c>default</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc/>
    public bool Equals(Nip other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Nip other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    /// <inheritdoc/>
    public int CompareTo(Nip other) => string.CompareOrdinal(_value, other._value);

    /// <summary>Equality.</summary>
    public static bool operator ==(Nip left, Nip right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(Nip left, Nip right) => !left.Equals(right);

    /// <summary>Ordinal less-than.</summary>
    public static bool operator <(Nip left, Nip right) => left.CompareTo(right) < 0;

    /// <summary>Ordinal less-than-or-equal.</summary>
    public static bool operator <=(Nip left, Nip right) => left.CompareTo(right) <= 0;

    /// <summary>Ordinal greater-than.</summary>
    public static bool operator >(Nip left, Nip right) => left.CompareTo(right) > 0;

    /// <summary>Ordinal greater-than-or-equal.</summary>
    public static bool operator >=(Nip left, Nip right) => left.CompareTo(right) >= 0;

#if NET
    static Nip IParsable<Nip>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<Nip>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Nip result) =>
        TryParse(s, out result);

    static Nip ISpanParsable<Nip>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        TryParse(s, out var result) ? result : throw new FormatException("Not a valid NIP.");

    static bool ISpanParsable<Nip>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Nip result) =>
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
