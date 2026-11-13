using System;
using System.Collections.Generic;
using System.Globalization;

namespace PolishOpenData.Ceidg;

internal static class CeidgMapper
{
    public static CeidgBusiness Map(CeidgFirmaDto dto) => new()
    {
        Id = Id(dto.Id),
        Name = dto.Nazwa,
        Status = MapStatus(dto.Status),
        StatusRaw = dto.Status,
        OwnerFirstName = dto.Wlasciciel?.Imie,
        OwnerLastName = dto.Wlasciciel?.Nazwisko,
        Nip = PolishOpenData.Nip.TryParse(dto.Wlasciciel?.Nip, out var nip) ? nip : null,
        Regon = PolishOpenData.Regon.TryParse(dto.Wlasciciel?.Regon, out var regon) ? regon : null,
        StartDate = Date(dto.DataRozpoczecia),
        SuspendedOn = Date(dto.DataZawieszenia),
        ResumedOn = Date(dto.DataWznowienia),
        EndedOn = Date(dto.DataZakonczenia),
        Address = dto.AdresDzialalnosci is { } a ? MapAddress(a) : null,
        MainPkd = MapPkd(dto.PkdGlowny),
        Pkd = MapPkds(dto.Pkd),
        Link = dto.Link,
    };

    private static CeidgStatus MapStatus(string? raw) => raw switch
    {
        "AKTYWNY" => CeidgStatus.Active,
        "ZAWIESZONY" => CeidgStatus.Suspended,
        "WYKRESLONY" => CeidgStatus.Deleted,
        "OCZEKUJE_NA_ROZPOCZECIE_DZIALANOSCI" => CeidgStatus.PendingStart,
        "WYLACZNIE_W_FORMIE_SPOLKI" => CeidgStatus.PartnershipOnly,
        _ => CeidgStatus.Unknown,
    };

    // An unreadable id or date throws (the client reports it as an unusable response) rather than becoming a missing value:
    // a business that "was never suspended" because its date changed format is worse than an error.
    private static Guid Id(string? text) =>
        string.IsNullOrEmpty(text) ? Guid.Empty : Guid.TryParse(text, out var id) ? id : throw new FormatException("CEIDG id is not a GUID.");

    private static DateOnly? Date(string? text) =>
        string.IsNullOrEmpty(text) ? null
        : DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : throw new FormatException("CEIDG date is not in the format yyyy-MM-dd.");

    private static CeidgAddress MapAddress(CeidgAdresDto a) => new()
    {
        Street = a.Ulica,
        Building = a.Budynek,
        Unit = a.Lokal,
        City = a.Miasto,
        Voivodeship = a.Wojewodztwo,
        County = a.Powiat,
        Commune = a.Gmina,
        Country = a.Kraj,
        PostalCode = a.Kod,
        TercCode = a.Terc,
        SimcCode = a.Simc,
        UlicCode = a.Ulic,
    };

    private static CeidgPkd? MapPkd(CeidgPkdDto? p) =>
        string.IsNullOrEmpty(p?.Kod) ? null : new CeidgPkd { Code = p!.Kod!, Name = p.Nazwa };

    private static List<CeidgPkd> MapPkds(List<CeidgPkdDto>? list)
    {
        var result = new List<CeidgPkd>();
        if (list is not null)
        {
            foreach (var p in list)
            {
                if (MapPkd(p) is { } mapped)
                {
                    result.Add(mapped);
                }
            }
        }

        return result;
    }
}
