using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolishOpenData.Internal;

namespace PolishOpenData.Krs.Serialization;

/// <summary>
/// Reads and writes a KRS date in the registry's <c>dd.MM.yyyy</c> format (for example <c>17.09.2026</c>) as a
/// <see cref="DateOnly"/>. The date properties of the KRS models use it. It is public so that a
/// <see cref="JsonSerializerContext"/> in your own assembly can serialise those models with the source generator.
/// </summary>
/// <remarks>
/// Reading any other text throws <see cref="JsonException"/>. On a nullable property, JSON <c>null</c> reads as
/// <c>null</c>.
/// </remarks>
public sealed class KrsDateJsonConverter : JsonConverter<DateOnly>
{
    private static readonly DottedDateOnlyConverter Inner = new();

    /// <inheritdoc/>
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Inner.Read(ref reader, typeToConvert, options);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
        Inner.Write(writer, value, options);
}

/// <summary>
/// Reads and writes a KRS timestamp in the registry's <c>dd.MM.yyyy HH:mm:ss</c> format, which is Europe/Warsaw
/// wall-clock time (for example <c>24.09.2026 02:32:16</c>), as a <see cref="DateTimeOffset"/> with the Warsaw offset
/// of that moment. Writing converts the value to Warsaw time first. The <c>dataCzasOdpisu</c> properties of the KRS
/// models use it. It is public so that a <see cref="JsonSerializerContext"/> in your own assembly can serialise those
/// models with the source generator.
/// </summary>
/// <remarks>
/// Reading any other text throws <see cref="JsonException"/>. On a nullable property, JSON <c>null</c> reads as
/// <c>null</c>.
/// </remarks>
public sealed class KrsTimestampJsonConverter : JsonConverter<DateTimeOffset>
{
    private static readonly DottedWarsawDateTimeConverter Inner = new();

    /// <inheritdoc/>
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Inner.Read(ref reader, typeToConvert, options);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        Inner.Write(writer, value, options);
}
