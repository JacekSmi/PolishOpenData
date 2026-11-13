using System;
using System.Collections.Generic;
using System.Linq;

namespace PolishOpenData.Bdl;

/// <summary>Aggregation level of a territorial unit.</summary>
public enum BdlLevel
{
    /// <summary>Poland.</summary>
    Poland = 0,

    /// <summary>Macroregion.</summary>
    Macroregion = 1,

    /// <summary>Voivodeship (województwo).</summary>
    Voivodeship = 2,

    /// <summary>Region.</summary>
    Region = 3,

    /// <summary>Subregion (podregion).</summary>
    Subregion = 4,

    /// <summary>Powiat.</summary>
    Powiat = 5,

    /// <summary>Gmina.</summary>
    Gmina = 6,

    /// <summary>Statistical locality.</summary>
    Locality = 7,
}

/// <summary>A territorial unit (voivodeship, powiat, gmina...).</summary>
/// <param name="Id">12-digit BDL unit id.</param>
/// <param name="Name">Unit name.</param>
/// <param name="ParentId">Parent unit id, or null.</param>
/// <param name="Level">Aggregation level.</param>
/// <param name="Kind">GUS unit kind code (for gminas: 1 urban, 2 rural, 3 urban-rural), or null.</param>
public sealed record BdlUnit(string Id, string Name, string? ParentId, BdlLevel Level, string? Kind)
{
    /// <summary>
    /// TERYT code derived from the id (2 digits for voivodeships, 4 for powiats, 7 for gminas), or null for other levels or an
    /// unexpected id. The derivation is inferred from the id structure, not documented by GUS.
    /// </summary>
    public string? Teryt
    {
        get
        {
            if (Id.Length != 12 || !Id.All(static c => c is >= '0' and <= '9'))
            {
                return null;
            }

            var length = Level switch
            {
                BdlLevel.Voivodeship => 2,
                BdlLevel.Powiat => 4,
                BdlLevel.Gmina => 7,
                _ => 0,
            };
            if (length == 0)
            {
                return null;
            }

            char[] digits = [Id[2], Id[3], Id[7], Id[8], Id[9], Id[10], Id[11]];
            return new string(digits, 0, length);
        }
    }
}

/// <summary>A statistical variable (indicator).</summary>
/// <param name="Id">Variable id.</param>
/// <param name="SubjectId">Id of the subject (topic) it belongs to.</param>
/// <param name="Dimensions">Non-empty dimension labels (n1..n5). They are only meaningful together with the subject name.</param>
/// <param name="Level">Finest level the variable exists for.</param>
/// <param name="MeasureUnitName">Unit of measure (for example "osoba"), or null.</param>
/// <param name="Years">Years with data (only when fetched by id), or null.</param>
public sealed record BdlVariable(int Id, string SubjectId, IReadOnlyList<string> Dimensions, BdlLevel Level, string? MeasureUnitName, IReadOnlyList<int>? Years);

/// <summary>A subject (topic) of the BDL catalogue.</summary>
public sealed record BdlSubject(string Id, string? ParentId, string Name, IReadOnlyList<string> Dimensions);

/// <summary>One value for one year.</summary>
/// <param name="Year">Year.</param>
/// <param name="Value">The number, or null when the cell has no value (see <see cref="Flag"/>).</param>
/// <param name="Flag">GUS flag of the cell.</param>
public sealed record BdlValue(int Year, double? Value, BdlValueFlag Flag);

/// <summary>Values of one variable for one unit.</summary>
/// <param name="VariableId">Variable id.</param>
/// <param name="MeasureUnitId">Measure unit id.</param>
/// <param name="LastUpdate">When GUS last changed the series, exactly as GUS sends it: without an offset (<see cref="DateTimeKind.Unspecified"/>), its time zone is not documented.</param>
/// <param name="Values">One value per year.</param>
public sealed record BdlSeries(int VariableId, int MeasureUnitId, DateTime? LastUpdate, IReadOnlyList<BdlValue> Values);

/// <summary>Result of reading several variables for one unit. <see cref="Series"/> is empty for an unknown unit.</summary>
public sealed record BdlUnitData(string UnitId, string UnitName, IReadOnlyList<BdlSeries> Series);

/// <summary>Values of one variable for one unit within <see cref="BdlVariableData"/>.</summary>
public sealed record BdlUnitValues(string UnitId, string Name, IReadOnlyList<BdlValue> Values);

/// <summary>Values of one variable across units.</summary>
/// <param name="VariableId">Variable id.</param>
/// <param name="MeasureUnitId">Measure unit id.</param>
/// <param name="Units">Units read.</param>
/// <param name="TotalRecords">Units the API reports in total.</param>
/// <param name="Truncated">True when more units exist than were read: the page cap was reached or GUS's rate limit stopped the read.</param>
/// <param name="NextPage">When <paramref name="Truncated"/>, the page index to pass as <c>startPage</c> to continue without re-reading; otherwise null.</param>
/// <param name="RateLimited">True when GUS's rate limit (HTTP 429) stopped the read after at least one page: the units read are returned, none are lost.</param>
public sealed record BdlVariableData(int VariableId, int MeasureUnitId, IReadOnlyList<BdlUnitValues> Units, int TotalRecords, bool Truncated, int? NextPage, bool RateLimited);

/// <summary>A list result with the total the API knows about.</summary>
public sealed record BdlPage<T>(IReadOnlyList<T> Items, int TotalRecords);
