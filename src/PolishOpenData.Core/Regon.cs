using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using PolishOpenData.Internal;

namespace PolishOpenData;

/// <summary>
/// Polish statistical number (REGON): 9 digits for an entity or 14 digits for a local unit, each with a mod-11 check
/// digit. A valid 14-digit REGON also starts with a valid 9-digit REGON. <c>default(Regon)</c> is empty.
/// </summary>
[JsonConverter(typeof(RegonJsonConverter))]
public readonly struct Regon : IEquatable<Regon>, IComparable<Regon>
#if NET
    , ISpanParsable<Regon>, ISpanFormattable
#endif
{
    private static readonly int[] Weights9 = [8, 9, 2, 3, 4, 5, 6, 7];
    private static readonly int[] Weights14 = [2, 4, 8, 5, 0, 9, 7, 3, 6, 1, 2, 4, 8];
    private readonly string? _value;

    private Regon(string value) => _value = value;

    /// <summary>True for <c>default(Regon)</c>.</summary>
    public bool IsEmpty => _value is null;

    /// <summary>9 or 14; 0 when empty.</summary>
    public int Length => _value?.Length ?? 0;

    /// <summary>True for a 14-digit REGON of a local unit (<i>jednostka lokalna</i>).</summary>
    public bool IsLocalUnit => Length == 14;

    /// <summary>The 9-digit REGON of the entity (itself when already 9 digits).</summary>
    public Regon BaseRegon => IsLocalUnit ? new Regon(_value!.Substring(0, 9)) : this;

    /// <summary>Parses a REGON; accepts spaces and dashes.</summary>
    /// <exception cref="FormatException">The input is not a valid REGON.</exception>
    public static Regon Parse(string s) =>
        TryParse(s, out var result) ? result : throw new FormatException($"'{s}' is not a valid REGON.");

    /// <summary>Tries to parse a REGON; accepts spaces and dashes.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out Regon result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), out result);
    }

    /// <summary>Tries to parse a REGON from characters; accepts spaces and dashes.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out Regon result)
    {
        result = default;
        Span<char> digits = stackalloc char[14];
        var count = DigitText.Extract(s, digits, allowPlPrefix: false);
        if (count == 9 && HasValidCheckDigit(digits, Weights9))
        {
            result = new Regon(digits.Slice(0, 9).ToString());
            return true;
        }

        if (count == 14 && HasValidCheckDigit(digits, Weights9) && HasValidCheckDigit(digits, Weights14))
        {
            result = new Regon(digits.ToString());
            return true;
        }

        return false;
    }

    private static bool HasValidCheckDigit(ReadOnlySpan<char> digits, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            sum += (digits[i] - '0') * weights[i];
        }

        var check = sum % 11;
        if (check == 10)
        {
            check = 0;
        }

        return check == digits[weights.Length] - '0';
    }

    /// <summary>The 9 or 14 digits, or an empty string for <c>default</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc/>
    public bool Equals(Regon other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Regon other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    /// <inheritdoc/>
    public int CompareTo(Regon other) => string.CompareOrdinal(_value, other._value);

    /// <summary>Equality.</summary>
    public static bool operator ==(Regon left, Regon right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(Regon left, Regon right) => !left.Equals(right);

    /// <summary>Ordinal less-than.</summary>
    public static bool operator <(Regon left, Regon right) => left.CompareTo(right) < 0;

    /// <summary>Ordinal less-than-or-equal.</summary>
    public static bool operator <=(Regon left, Regon right) => left.CompareTo(right) <= 0;

    /// <summary>Ordinal greater-than.</summary>
    public static bool operator >(Regon left, Regon right) => left.CompareTo(right) > 0;

    /// <summary>Ordinal greater-than-or-equal.</summary>
    public static bool operator >=(Regon left, Regon right) => left.CompareTo(right) >= 0;

#if NET
    static Regon IParsable<Regon>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<Regon>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Regon result) =>
        TryParse(s, out result);

    static Regon ISpanParsable<Regon>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        TryParse(s, out var result) ? result : throw new FormatException("Not a valid REGON.");

    static bool ISpanParsable<Regon>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Regon result) =>
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
