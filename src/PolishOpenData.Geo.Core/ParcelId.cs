using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolishOpenData.Geo;

/// <summary>
/// TERYT parcel identifier (<i>identyfikator działki</i>), e.g. <c>146510_8.0507.41/1</c> or
/// <c>146510_8.0507.AR_2.41/1</c>: <c>unit.region[.sheet].number</c>. <c>default(ParcelId)</c> is empty.
/// </summary>
[JsonConverter(typeof(ParcelIdJsonConverter))]
public readonly struct ParcelId : IEquatable<ParcelId>
{
    private const int UnitLength = 8;    // "146510_8"
    private const int RegionStart = 9;   // after "146510_8."
    private const int RegionLength = 4;  // "0507"
    private const int RestStart = 14;    // after "146510_8.0507."

    private readonly string? _value;

    private ParcelId(string value) => _value = value;

    /// <summary>True for <c>default(ParcelId)</c>.</summary>
    public bool IsEmpty => _value is null;

    /// <summary>The TERYT commune code with its type digit, e.g. <c>146510_8</c>.</summary>
    public string Unit => _value is null ? string.Empty : _value.Substring(0, UnitLength);

    /// <summary>The cadastral region (<i>obręb</i>) number, e.g. <c>0507</c>.</summary>
    public string Region => _value is null ? string.Empty : _value.Substring(RegionStart, RegionLength);

    /// <summary>The map sheet (<i>arkusz</i>), e.g. <c>AR_2</c>, or null when the identifier has none.</summary>
    public string? Sheet
    {
        get
        {
            if (_value is null || !_value.AsSpan(RestStart).StartsWith("AR_".AsSpan(), StringComparison.Ordinal))
            {
                return null;
            }

            return _value.Substring(RestStart, _value.IndexOf('.', RestStart) - RestStart);
        }
    }

    /// <summary>The parcel number, possibly with slashes, e.g. <c>41/1</c>.</summary>
    public string Number
    {
        get
        {
            if (_value is null)
            {
                return string.Empty;
            }

            return _value.Substring(Sheet is { } sheet ? RestStart + sheet.Length + 1 : RestStart);
        }
    }

    /// <summary>Parses a parcel identifier (surrounding whitespace is ignored).</summary>
    /// <exception cref="FormatException">The input is not a parcel identifier.</exception>
    public static ParcelId Parse(string s) =>
        TryParse(s, out var result) ? result : throw new FormatException($"'{s}' is not a valid parcel identifier.");

    /// <summary>Tries to parse a parcel identifier (surrounding whitespace is ignored).</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out ParcelId result)
    {
        result = default;
        var t = s?.Trim();
        if (t is null || !IsValid(t))
        {
            return false;
        }

        result = new ParcelId(t);
        return true;
    }

    // Grammar: \d{6}_\d\.\d{4}\.(AR_\d+\.)?[0-9A-Za-z]+(/[0-9A-Za-z]+)* with ASCII-only classes.
    private static bool IsValid(string t)
    {
        if (t.Length < RestStart + 1 || t[6] != '_' || t[UnitLength] != '.' || t[RestStart - 1] != '.')
        {
            return false;
        }

        if (!AllDigits(t, 0, 6) || !IsDigit(t[7]) || !AllDigits(t, RegionStart, RegionLength))
        {
            return false;
        }

        var rest = RestStart;
        if (string.CompareOrdinal(t, rest, "AR_", 0, 3) == 0)
        {
            var dot = t.IndexOf('.', rest);
            if (dot < rest + 4 || !AllDigits(t, rest + 3, dot - rest - 3))
            {
                return false;
            }

            rest = dot + 1;
        }

        // Number: alphanumeric segments separated by single slashes, none empty.
        var segment = 0;
        for (var i = rest; i < t.Length; i++)
        {
            if (t[i] == '/')
            {
                if (segment == 0)
                {
                    return false;
                }

                segment = 0;
            }
            else if (IsDigit(t[i]) || (t[i] is >= 'a' and <= 'z') || (t[i] is >= 'A' and <= 'Z'))
            {
                segment++;
            }
            else
            {
                return false;
            }
        }

        return segment > 0;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool AllDigits(string s, int start, int count)
    {
        for (var i = start; i < start + count; i++)
        {
            if (!IsDigit(s[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The canonical identifier, or an empty string for <c>default</c>.</summary>
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc/>
    public bool Equals(ParcelId other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ParcelId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    /// <summary>Equality.</summary>
    public static bool operator ==(ParcelId left, ParcelId right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(ParcelId left, ParcelId right) => !left.Equals(right);
}

/// <summary>Serialises <see cref="ParcelId"/> as its canonical string.</summary>
public sealed class ParcelIdJsonConverter : JsonConverter<ParcelId>
{
    /// <inheritdoc/>
    public override ParcelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return ParcelId.TryParse(s, out var value) ? value : throw new JsonException($"Invalid parcel identifier '{s}'.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ParcelId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
