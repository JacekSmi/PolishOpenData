using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolishOpenData;

/// <summary>Serialises <see cref="Nip"/> as its canonical 10-digit string.</summary>
public sealed class NipJsonConverter : JsonConverter<Nip>
{
    /// <inheritdoc/>
    public override Nip Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return Nip.TryParse(s, out var value) ? value : throw new JsonException($"Invalid NIP '{s}'.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Nip value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Serialises <see cref="Regon"/> as its canonical 9- or 14-digit string.</summary>
public sealed class RegonJsonConverter : JsonConverter<Regon>
{
    /// <inheritdoc/>
    public override Regon Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return Regon.TryParse(s, out var value) ? value : throw new JsonException($"Invalid REGON '{s}'.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Regon value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Serialises <see cref="KrsNumber"/> as its zero-padded 10-digit string.</summary>
public sealed class KrsNumberJsonConverter : JsonConverter<KrsNumber>
{
    /// <inheritdoc/>
    public override KrsNumber Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return KrsNumber.TryParse(s, out var value) ? value : throw new JsonException($"Invalid KRS number '{s}'.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, KrsNumber value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Serialises <see cref="Nrb"/> as its canonical 26-digit string.</summary>
public sealed class NrbJsonConverter : JsonConverter<Nrb>
{
    /// <inheritdoc/>
    public override Nrb Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return Nrb.TryParse(s, out var value) ? value : throw new JsonException($"Invalid NRB '{s}'.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Nrb value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Serialises <see cref="KwNumber"/> as <c>CCCC/NNNNNNNN/K</c>.</summary>
public sealed class KwNumberJsonConverter : JsonConverter<KwNumber>
{
    /// <inheritdoc/>
    public override KwNumber Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return KwNumber.TryParse(s, out var value) ? value : throw new JsonException($"Invalid KW number '{s}'.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, KwNumber value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
