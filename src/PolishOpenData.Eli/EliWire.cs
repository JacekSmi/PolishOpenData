using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PolishOpenData.Eli;

internal sealed class EliActDto
{
    public string? Publisher { get; set; }

    public int? Year { get; set; }

    public int? Pos { get; set; }

    public string? DisplayAddress { get; set; }

    public string? Title { get; set; }

    public string? Type { get; set; }

    public string? Status { get; set; }

    public string? InForce { get; set; }

    public bool TextHtml { get; set; }

    public bool TextPdf { get; set; }

    public string? AnnouncementDate { get; set; }

    public string? EntryIntoForce { get; set; }

    public List<string>? Keywords { get; set; }

    public List<string>? ReleasedBy { get; set; }

    public Dictionary<string, List<EliRefDto>>? References { get; set; }
}

internal sealed class EliRefDto
{
    public string? Id { get; set; }

    public string? Date { get; set; }
}

internal sealed class EliSearchDto
{
    public int? TotalCount { get; set; }

    public List<EliActDto>? Items { get; set; }
}

internal sealed class EliUnitDto
{
    public string? Id { get; set; }

    public string? Type { get; set; }

    public string? Name { get; set; }

    public string? Title { get; set; }

    public List<EliUnitDto>? Children { get; set; }
}

#if NET
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(EliActDto))]
[JsonSerializable(typeof(EliSearchDto))]
[JsonSerializable(typeof(List<EliUnitDto>))]
internal sealed partial class EliJsonContext : JsonSerializerContext;
#endif

/// <summary>Type infos: source-generated on .NET, reflection on netstandard2.0.</summary>
internal static class EliJson
{
#if NET
    public static JsonTypeInfo<EliActDto> Act => EliJsonContext.Default.EliActDto;

    public static JsonTypeInfo<EliSearchDto> Search => EliJsonContext.Default.EliSearchDto;

    public static JsonTypeInfo<List<EliUnitDto>> Struct => EliJsonContext.Default.ListEliUnitDto;
#else
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    public static JsonTypeInfo<EliActDto> Act { get; } = Info<EliActDto>();

    public static JsonTypeInfo<EliSearchDto> Search { get; } = Info<EliSearchDto>();

    public static JsonTypeInfo<List<EliUnitDto>> Struct { get; } = Info<List<EliUnitDto>>();
#endif
}
