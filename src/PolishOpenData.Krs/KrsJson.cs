using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PolishOpenData.Krs;

internal sealed class KrsCurrentEnvelope
{
    public KrsCurrentExtract? Odpis { get; set; }
}

internal sealed class KrsFullEnvelope
{
    public KrsFullExtract? Odpis { get; set; }
}

#if NET
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(KrsCurrentEnvelope))]
[JsonSerializable(typeof(KrsFullEnvelope))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class KrsJsonContext : JsonSerializerContext;
#endif

/// <summary>Type infos: source-generated on .NET, reflection on netstandard2.0 (the generator cannot handle the DateOnly polyfill there).</summary>
internal static class KrsJson
{
#if NET
    public static JsonTypeInfo<KrsCurrentEnvelope> Current => KrsJsonContext.Default.KrsCurrentEnvelope;

    public static JsonTypeInfo<KrsFullEnvelope> Full => KrsJsonContext.Default.KrsFullEnvelope;

    public static JsonTypeInfo<string[]> StringArray => KrsJsonContext.Default.StringArray;
#else
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static JsonTypeInfo<KrsCurrentEnvelope> Current { get; } = (JsonTypeInfo<KrsCurrentEnvelope>)Options.GetTypeInfo(typeof(KrsCurrentEnvelope));

    public static JsonTypeInfo<KrsFullEnvelope> Full { get; } = (JsonTypeInfo<KrsFullEnvelope>)Options.GetTypeInfo(typeof(KrsFullEnvelope));

    public static JsonTypeInfo<string[]> StringArray { get; } = (JsonTypeInfo<string[]>)Options.GetTypeInfo(typeof(string[]));
#endif

    public static KrsCurrentExtract? DeserializeCurrent(string json) => JsonSerializer.Deserialize(json, Current)?.Odpis;

    public static KrsFullExtract? DeserializeFull(string json) => JsonSerializer.Deserialize(json, Full)?.Odpis;
}
