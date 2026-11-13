using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PolishOpenData.Bdl;

internal sealed class BdlPagedDto<T>
{
    public int? TotalRecords { get; set; }

    public List<T>? Results { get; set; }
}

internal sealed class BdlUnitDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? ParentId { get; set; }

    public int Level { get; set; }

    public string? Kind { get; set; }
}

internal sealed class BdlVariableDto
{
    public int Id { get; set; }

    public string? SubjectId { get; set; }

    public string? N1 { get; set; }

    public string? N2 { get; set; }

    public string? N3 { get; set; }

    public string? N4 { get; set; }

    public string? N5 { get; set; }

    public int Level { get; set; }

    public string? MeasureUnitName { get; set; }

    public List<int>? Years { get; set; }
}

internal sealed class BdlSubjectDto
{
    public string? Id { get; set; }

    public string? ParentId { get; set; }

    public string? Name { get; set; }

    public List<string>? Dimensions { get; set; }
}

internal sealed class BdlYearValueDto
{
    public string? Year { get; set; }

    public double? Val { get; set; }

    public int? AttrId { get; set; }
}

internal sealed class BdlVariableValuesDto
{
    public int Id { get; set; }

    public int MeasureUnitId { get; set; }

    public string? LastUpdate { get; set; }

    public List<BdlYearValueDto>? Values { get; set; }
}

internal sealed class BdlUnitDataDto
{
    public string? UnitId { get; set; }

    public string? UnitName { get; set; }

    public List<BdlVariableValuesDto>? Results { get; set; }
}

internal sealed class BdlUnitValuesDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public List<BdlYearValueDto>? Values { get; set; }
}

internal sealed class BdlVariableDataDto
{
    public int? TotalRecords { get; set; }

    public int VariableId { get; set; }

    public int MeasureUnitId { get; set; }

    public List<BdlUnitValuesDto>? Results { get; set; }
}

internal sealed class BdlErrorsDto
{
    public List<BdlErrorDto>? Errors { get; set; }
}

internal sealed class BdlErrorDto
{
    public int ErrorCode { get; set; }

    public string? ErrorReason { get; set; }
}

#if NET
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(BdlPagedDto<BdlUnitDto>))]
[JsonSerializable(typeof(BdlPagedDto<BdlVariableDto>))]
[JsonSerializable(typeof(BdlUnitDto))]
[JsonSerializable(typeof(BdlVariableDto))]
[JsonSerializable(typeof(BdlSubjectDto))]
[JsonSerializable(typeof(BdlUnitDataDto))]
[JsonSerializable(typeof(BdlVariableDataDto))]
[JsonSerializable(typeof(BdlErrorsDto))]
internal sealed partial class BdlJsonContext : JsonSerializerContext;
#endif

/// <summary>Type infos: source-generated on .NET, reflection on netstandard2.0.</summary>
internal static class BdlJson
{
#if NET
    public static JsonTypeInfo<BdlPagedDto<BdlUnitDto>> Units => BdlJsonContext.Default.BdlPagedDtoBdlUnitDto;

    public static JsonTypeInfo<BdlPagedDto<BdlVariableDto>> Variables => BdlJsonContext.Default.BdlPagedDtoBdlVariableDto;

    public static JsonTypeInfo<BdlUnitDto> Unit => BdlJsonContext.Default.BdlUnitDto;

    public static JsonTypeInfo<BdlVariableDto> Variable => BdlJsonContext.Default.BdlVariableDto;

    public static JsonTypeInfo<BdlSubjectDto> Subject => BdlJsonContext.Default.BdlSubjectDto;

    public static JsonTypeInfo<BdlUnitDataDto> UnitData => BdlJsonContext.Default.BdlUnitDataDto;

    public static JsonTypeInfo<BdlVariableDataDto> VariableData => BdlJsonContext.Default.BdlVariableDataDto;

    public static JsonTypeInfo<BdlErrorsDto> Errors => BdlJsonContext.Default.BdlErrorsDto;
#else
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    public static JsonTypeInfo<BdlPagedDto<BdlUnitDto>> Units { get; } = Info<BdlPagedDto<BdlUnitDto>>();

    public static JsonTypeInfo<BdlPagedDto<BdlVariableDto>> Variables { get; } = Info<BdlPagedDto<BdlVariableDto>>();

    public static JsonTypeInfo<BdlUnitDto> Unit { get; } = Info<BdlUnitDto>();

    public static JsonTypeInfo<BdlVariableDto> Variable { get; } = Info<BdlVariableDto>();

    public static JsonTypeInfo<BdlSubjectDto> Subject { get; } = Info<BdlSubjectDto>();

    public static JsonTypeInfo<BdlUnitDataDto> UnitData { get; } = Info<BdlUnitDataDto>();

    public static JsonTypeInfo<BdlVariableDataDto> VariableData { get; } = Info<BdlVariableDataDto>();

    public static JsonTypeInfo<BdlErrorsDto> Errors { get; } = Info<BdlErrorsDto>();
#endif
}
