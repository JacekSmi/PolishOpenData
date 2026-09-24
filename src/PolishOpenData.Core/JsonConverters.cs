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
