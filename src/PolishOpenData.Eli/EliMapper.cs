using System;
using System.Collections.Generic;
using System.Globalization;

namespace PolishOpenData.Eli;

// An unreadable id or date throws FormatException (the client reports it as an unusable response): an amendment that silently
// loses its date, or a consolidated text that silently disappears, would make "is this text current" wrong.
internal static class EliMapper
{
    internal const string ConsolidatedKey = "Inf. o tekście jednolitym";
    internal const string AmendingKey = "Akty zmieniające";
    internal const string BaseActKey = "Tekst jednolity dla aktu";

    public static EliActSummary ToSummary(EliActDto dto) => new(
        Id(dto),
        dto.DisplayAddress ?? string.Empty,
        dto.Title ?? string.Empty,
        dto.Type,
        dto.Status,
        dto.InForce switch
        {
            "IN_FORCE" => true,
            "NOT_IN_FORCE" => false,
            _ => null,
        },
        Date(dto.AnnouncementDate),
        Date(dto.EntryIntoForce),
        dto.TextHtml,
        dto.TextPdf);

    public static EliAct ToAct(EliActDto dto)
    {
        var consolidated = new List<EliActId>();
        var amendments = new List<EliAmendment>();
        var others = new Dictionary<string, IReadOnlyList<EliActId>>(StringComparer.Ordinal);
        foreach (var group in dto.References ?? [])
        {
            switch (group.Key)
            {
                case ConsolidatedKey:
                    foreach (var r in group.Value)
                    {
                        consolidated.Add(RequiredId(r.Id));
                    }

                    break;
                case AmendingKey:
                    foreach (var r in group.Value)
                    {
                        amendments.Add(new EliAmendment(RequiredId(r.Id), Date(r.Date)));
                    }

                    break;
                default:
                    var ids = new List<EliActId>();
                    foreach (var r in group.Value)
                    {
                        if (EliActId.TryParse(r.Id, out var id))
                        {
                            ids.Add(id);
                        }
                    }

                    others[group.Key] = ids;
                    break;
            }
        }

        return new EliAct(ToSummary(dto), dto.Keywords ?? [], dto.ReleasedBy ?? [], consolidated, amendments, others);
    }

    public static IReadOnlyList<EliUnit> ToUnits(List<EliUnitDto>? dtos)
    {
        var result = new List<EliUnit>();
        foreach (var d in dtos ?? [])
        {
            result.Add(new EliUnit(d.Id ?? string.Empty, d.Type ?? string.Empty, d.Name, d.Title, ToUnits(d.Children)));
        }

        return result;
    }

    private static EliActId Id(EliActDto dto) =>
        dto.Publisher is not null && dto.Year is { } y && dto.Pos is { } p
        && EliActId.TryParse(dto.Publisher + "/" + y.ToString(CultureInfo.InvariantCulture) + "/" + p.ToString(CultureInfo.InvariantCulture), out var id)
            ? id
            : throw new FormatException("ELI act has no readable publisher, year and position.");

    private static EliActId RequiredId(string? text) =>
        EliActId.TryParse(text, out var id) ? id : throw new FormatException("ELI reference is not an act id.");

    private static DateOnly? Date(string? text) =>
        string.IsNullOrEmpty(text) ? null
        : DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : throw new FormatException("ELI date is not in the format yyyy-MM-dd.");
}
