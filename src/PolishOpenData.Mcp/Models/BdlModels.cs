using System.Collections.Generic;

namespace PolishOpenData.Mcp;

/// <summary>A territorial unit of the GUS local data bank.</summary>
internal sealed record BdlUnitView(string Id, string Name, string Level, string? Kind, string? Teryt, string? ParentId);

/// <summary>find_statistical_unit result.</summary>
internal sealed record BdlUnitSearchResult
{
    public required string Query { get; init; }

    public required IReadOnlyList<BdlUnitView> Units { get; init; }

    /// <summary>Attribution required by the CC BY 4.0 licence.</summary>
    public required string Source { get; init; }
}

internal sealed record BdlVariableView(int Id, string Label, string? MeasureUnit, string SubjectId);

/// <summary>search_statistics result.</summary>
internal sealed record BdlVariableSearchResult
{
    public required string Query { get; init; }

    /// <summary>All matches GUS reports; <see cref="Variables"/> holds the first ones.</summary>
    public required int TotalMatches { get; init; }

    public required IReadOnlyList<BdlVariableView> Variables { get; init; }

    public required string Source { get; init; }
}

/// <summary>One year's value; <see cref="Value"/> is null when GUS has no value (see <see cref="Flag"/>).</summary>
internal sealed record BdlValueView(int Year, double? Value, string? Flag);

internal sealed record BdlSeriesView(int VariableId, IReadOnlyList<BdlValueView> Values);

/// <summary>get_statistics result.</summary>
internal sealed record BdlStatisticsResult
{
    public required string UnitId { get; init; }

    public string? UnitName { get; init; }

    /// <summary>False when GUS has no data for this unit id (an unknown unit, or none of the variables exist for it).</summary>
    public required bool Found { get; init; }

    public required IReadOnlyList<BdlSeriesView> Series { get; init; }

    public required string Source { get; init; }
}
