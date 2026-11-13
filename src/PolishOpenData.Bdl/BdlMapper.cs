using System;
using System.Collections.Generic;
using System.Globalization;

namespace PolishOpenData.Bdl;

// An unreadable year, level or timestamp throws FormatException (the client reports it as an unusable response): a value that
// silently lands in the wrong year, or a series that loses its flag, is worse than an error.
internal static class BdlMapper
{
    public static BdlUnit ToUnit(BdlUnitDto dto) =>
        new(dto.Id ?? string.Empty, dto.Name ?? string.Empty, dto.ParentId, Level(dto.Level), dto.Kind);

    public static BdlVariable ToVariable(BdlVariableDto dto) =>
        new(dto.Id, dto.SubjectId ?? string.Empty, Dimensions(dto.N1, dto.N2, dto.N3, dto.N4, dto.N5), Level(dto.Level), dto.MeasureUnitName, dto.Years);

    public static BdlSubject ToSubject(BdlSubjectDto dto) =>
        new(dto.Id ?? string.Empty, dto.ParentId, dto.Name ?? string.Empty, (IReadOnlyList<string>?)dto.Dimensions ?? Array.Empty<string>());

    public static BdlUnitData ToUnitData(BdlUnitDataDto dto)
    {
        var series = new List<BdlSeries>();
        foreach (var r in dto.Results ?? [])
        {
            series.Add(new BdlSeries(r.Id, r.MeasureUnitId, Timestamp(r.LastUpdate), Values(r.Values)));
        }

        return new BdlUnitData(dto.UnitId ?? string.Empty, dto.UnitName ?? string.Empty, series);
    }

    public static BdlVariableData ToVariableData(BdlVariableDataDto dto, bool truncated)
    {
        var units = new List<BdlUnitValues>();
        foreach (var u in dto.Results ?? [])
        {
            units.Add(new BdlUnitValues(u.Id ?? string.Empty, u.Name ?? string.Empty, Values(u.Values)));
        }

        return new BdlVariableData(dto.VariableId, dto.MeasureUnitId, units, dto.TotalRecords ?? units.Count, truncated);
    }

    private static List<BdlValue> Values(List<BdlYearValueDto>? values)
    {
        var result = new List<BdlValue>();
        foreach (var v in values ?? [])
        {
            if (!int.TryParse(v.Year, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            {
                throw new FormatException("BDL year is not a number.");
            }

            // A value without an attribute id is kept as given and marked Unknown: dropping a number is worse.
            result.Add(v.AttrId is { } attr
                ? new BdlValue(year, BdlAttributes.ValueFor(attr, v.Val), BdlAttributes.Get(attr))
                : new BdlValue(year, v.Val, new BdlValueFlag(-1, null, BdlValueKind.Unknown)));
        }

        return result;
    }

    private static BdlLevel Level(int level) =>
        level is >= 0 and <= 7 ? (BdlLevel)level : throw new FormatException("BDL level is out of range.");

    private static DateTimeOffset? Timestamp(string? text) =>
        string.IsNullOrEmpty(text) ? null
        : DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t
        : throw new FormatException("BDL timestamp is not readable.");

    private static List<string> Dimensions(params string?[] names)
    {
        var result = new List<string>();
        foreach (var n in names)
        {
            if (!string.IsNullOrWhiteSpace(n))
            {
                result.Add(n!);
            }
        }

        return result;
    }
}
