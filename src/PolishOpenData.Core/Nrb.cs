using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using PolishOpenData.Internal;

namespace PolishOpenData;

/// <summary>
/// Polish bank account number (NRB, <i>numer rachunku bankowego</i>): 26 digits validated with the IBAN mod-97 rule
/// for country code PL. <c>default(Nrb)</c> is empty.
/// </summary>
[JsonConverter(typeof(NrbJsonConverter))]
public readonly struct Nrb : IEquatable<Nrb>, IComparable<Nrb>
#if NET
    , ISpanParsable<Nrb>, ISpanFormattable
#endif
{
    private readonly string? _value;

    private Nrb(string value) => _value = value;

    /// <summary>True for <c>default(Nrb)</c>.</summary>
    public bool IsEmpty => _value is null;

    /// <summary>Parses an NRB; accepts spaces, dashes and a <c>PL</c> prefix (IBAN form).</summary>
    /// <exception cref="FormatException">The input is not a valid NRB.</exception>
    public static Nrb Parse(string s) =>
        TryParse(s, out var result) ? result : throw new FormatException($"'{s}' is not a valid NRB bank account number.");

    /// <summary>Tries to parse an NRB; accepts spaces, dashes and a <c>PL</c> prefix (IBAN form).</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out Nrb result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), out result);
    }

    /// <summary>Tries to parse an NRB from characters.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out Nrb result)
    {
        result = default;
        Span<char> digits = stackalloc char[26];
        if (DigitText.Extract(s, digits, allowPlPrefix: true) != 26)
        {
            return false;
        }

        // IBAN check: move "PL" + 2 check digits to the end, P=25 L=21, remainder mod 97 must be 1.
        var remainder = 0;
        for (var i = 2; i < 26; i++)
        {
            remainder = ((remainder * 10) + (digits[i] - '0')) % 97;
        }

        foreach (var c in "2521")
        {
            remainder = ((remainder * 10) + (c - '0')) % 97;
        }

        remainder = ((remainder * 10) + (digits[0] - '0')) % 97;
        remainder = ((remainder * 10) + (digits[1] - '0')) % 97;
        if (remainder != 1)
        {
            return false;
        }

        result = new Nrb(digits.ToString());
        return true;
    }

    /// <summary>The account in IBAN form, e.g. <c>PL06160011271843983820000034</c>.</summary>
    public string ToIban() => _value is null ? string.Empty : "PL" + _value;

    /// <summary>The 26 digits, or an empty string for <c>default</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc/>
    public bool Equals(Nrb other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Nrb other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    /// <inheritdoc/>
    public int CompareTo(Nrb other) => string.CompareOrdinal(_value, other._value);

    /// <summary>Equality.</summary>
    public static bool operator ==(Nrb left, Nrb right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(Nrb left, Nrb right) => !left.Equals(right);

    /// <summary>Ordinal less-than.</summary>
    public static bool operator <(Nrb left, Nrb right) => left.CompareTo(right) < 0;

    /// <summary>Ordinal less-than-or-equal.</summary>
    public static bool operator <=(Nrb left, Nrb right) => left.CompareTo(right) <= 0;

    /// <summary>Ordinal greater-than.</summary>
    public static bool operator >(Nrb left, Nrb right) => left.CompareTo(right) > 0;

    /// <summary>Ordinal greater-than-or-equal.</summary>
    public static bool operator >=(Nrb left, Nrb right) => left.CompareTo(right) >= 0;

#if NET
    static Nrb IParsable<Nrb>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<Nrb>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Nrb result) =>
        TryParse(s, out result);

    static Nrb ISpanParsable<Nrb>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        TryParse(s, out var result) ? result : throw new FormatException("Not a valid NRB.");

    static bool ISpanParsable<Nrb>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Nrb result) =>
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
