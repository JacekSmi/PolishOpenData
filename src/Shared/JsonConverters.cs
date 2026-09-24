using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolishOpenData.Internal;

/// <summary>Reads and writes <c>dd.MM.yyyy</c> dates (KRS).</summary>
internal sealed class DottedDateOnlyConverter : JsonConverter<DateOnly>
{
    private const string Format = "dd.MM.yyyy";

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (string.IsNullOrEmpty(text) ||
            !DateOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new JsonException($"Expected a {Format} date but got '{text}'.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
    }
}

/// <summary>Reads and writes <c>yyyy-MM-dd</c> dates (Biała Lista); needed because netstandard2.0 has no built-in DateOnly support.</summary>
internal sealed class IsoDateOnlyConverter : JsonConverter<DateOnly>
{
    private const string Format = "yyyy-MM-dd";

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (string.IsNullOrEmpty(text) ||
            !DateOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new JsonException($"Expected a {Format} date but got '{text}'.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
    }
}

/// <summary>Reads and writes <c>dd.MM.yyyy HH:mm:ss</c> Warsaw wall-clock timestamps (KRS <c>dataCzasOdpisu</c>).</summary>
internal sealed class DottedWarsawDateTimeConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "dd.MM.yyyy HH:mm:ss";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (string.IsNullOrEmpty(text) ||
            !DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            throw new JsonException($"Expected a {Format} timestamp but got '{text}'.");
        }

        return WarsawTime.FromLocal(local);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var local = TimeZoneInfo.ConvertTime(value, WarsawTime.Zone);
        writer.WriteStringValue(local.ToString(Format, CultureInfo.InvariantCulture));
    }
}
