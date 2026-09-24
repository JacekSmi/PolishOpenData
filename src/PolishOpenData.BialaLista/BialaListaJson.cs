using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PolishOpenData.BialaLista;

#if NET
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(WlEntityResponse))]
[JsonSerializable(typeof(WlEntityListResponse))]
[JsonSerializable(typeof(WlEntryListResponse))]
[JsonSerializable(typeof(WlCheckResponse))]
internal sealed partial class BialaListaJsonContext : JsonSerializerContext;
#endif

/// <summary>Type infos: source-generated on .NET, reflection on netstandard2.0.</summary>
internal static class BialaListaJson
{
#if NET
    public static JsonTypeInfo<WlEntityResponse> EntityResponse => BialaListaJsonContext.Default.WlEntityResponse;

    public static JsonTypeInfo<WlEntityListResponse> EntityListResponse => BialaListaJsonContext.Default.WlEntityListResponse;

    public static JsonTypeInfo<WlEntryListResponse> EntryListResponse => BialaListaJsonContext.Default.WlEntryListResponse;

    public static JsonTypeInfo<WlCheckResponse> CheckResponse => BialaListaJsonContext.Default.WlCheckResponse;
#else
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static JsonTypeInfo<WlEntityResponse> EntityResponse { get; } = (JsonTypeInfo<WlEntityResponse>)Options.GetTypeInfo(typeof(WlEntityResponse));

    public static JsonTypeInfo<WlEntityListResponse> EntityListResponse { get; } = (JsonTypeInfo<WlEntityListResponse>)Options.GetTypeInfo(typeof(WlEntityListResponse));

    public static JsonTypeInfo<WlEntryListResponse> EntryListResponse { get; } = (JsonTypeInfo<WlEntryListResponse>)Options.GetTypeInfo(typeof(WlEntryListResponse));

    public static JsonTypeInfo<WlCheckResponse> CheckResponse { get; } = (JsonTypeInfo<WlCheckResponse>)Options.GetTypeInfo(typeof(WlCheckResponse));
#endif

    public static T? Deserialize<T>(string json, JsonTypeInfo<T> typeInfo) => JsonSerializer.Deserialize(json, typeInfo);
}
