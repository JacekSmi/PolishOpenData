using System;
using System.Collections.Generic;
using System.Linq;
using PolishOpenData.Internal;

namespace PolishOpenData.Krs;

/// <summary>Builds <see cref="KrsCompanySummary"/> from a current extract.</summary>
public static class KrsSummaryExtensions
{
    /// <summary>Flattens a current extract into an English summary. Never copies free-text proxy details.</summary>
    public static KrsCompanySummary ToSummary(this KrsCurrentExtract extract)
    {
        ArgumentNullException.ThrowIfNull(extract);
        var header = extract.NaglowekA;
        var dzial1 = extract.Dane?.Dzial1;
        var entity = dzial1?.DanePodmiotu;
        var seat = dzial1?.SiedzibaIAdres;
        var capital = dzial1?.Kapital?.WysokoscKapitaluZakladowego;
        var dzial2 = extract.Dane?.Dzial2;
        var dzial3 = extract.Dane?.Dzial3;

        return new KrsCompanySummary
        {
            Krs = KrsNumber.TryParse(header?.NumerKrs, out var krs) ? krs : default,
            Register = string.Equals(header?.Rejestr, "RejS", StringComparison.Ordinal) ? KrsRegister.Associations : KrsRegister.Entrepreneurs,
            Name = entity?.Nazwa ?? string.Empty,
            LegalForm = entity?.FormaPrawna,
            Nip = Nip.TryParse(entity?.Identyfikatory?.Nip, out var nip) ? nip : null,
            Regon = NormalizeRegon(entity?.Identyfikatory?.Regon),
            Address = FormatAddress(seat?.Adres),
            Locality = seat?.Siedziba?.Miejscowosc,
            Voivodeship = seat?.Siedziba?.Wojewodztwo,
            Email = seat?.AdresPocztyElektronicznej,
            Website = seat?.AdresStronyInternetowej,
            EDeliveryAddress = seat?.AdresDoDoreczenElektronicznychWpisanyDoBae,
            ShareCapital = PolishFormats.TryParseDecimal(capital?.Wartosc, out var amount) ? amount : null,
            ShareCapitalCurrency = capital?.Waluta,
            RegisteredOn = header?.DataRejestracjiWKrs,
            LastEntryNumber = header?.NumerOstatniegoWpisu,
            LastEntryOn = header?.DataOstatniegoWpisu,
            StateAsOf = header?.StanZDnia,
            ExtractedAt = header?.DataCzasOdpisu,
            IsPublicBenefitOrganization = entity?.CzyPosiadaStatusOpp,
            Representation = dzial2?.Reprezentacja is { } representation ? ToBody(representation) : null,
            SupervisoryBodies = dzial2?.OrganNadzoru?.Select(ToBody).ToList() ?? [],
            Proxies = dzial2?.Prokurenci?.Select(ToPerson).ToList() ?? [],
            Shareholders = dzial1?.WspolnicySpzoo?.Select(ToShareholder).ToList() ?? [],
            MainActivity = dzial3?.PrzedmiotDzialalnosci?.PrzedmiotPrzewazajacejDzialalnosci?.Select(ToActivity).FirstOrDefault(),
            OtherActivities = OtherActivities(dzial3),
        };
    }

    private static List<KrsActivity> OtherActivities(KrsDzial3? dzial3)
    {
        var activities = new List<KrsActivity>();
        if (dzial3?.PrzedmiotDzialalnosci?.PrzedmiotPozostalejDzialalnosci is { } other)
        {
            activities.AddRange(other.Select(ToActivity));
        }

        if (dzial3?.PrzedmiotDzialalnosciOpp is { } publicBenefit)
        {
            activities.AddRange((publicBenefit.OdplatnyPkd ?? []).Select(ToActivity));
            activities.AddRange((publicBenefit.NieodplatnyPkd ?? []).Select(ToActivity));
        }

        return activities;
    }

    private static Regon? NormalizeRegon(string? value)
    {
        // KRS writes a 9-digit REGON padded with "00000". About 2 in 11 padded values also pass the 14-digit
        // checksum, so strip the padding before validating instead of relying on the checksum failing.
        if (value is { Length: 14 } && value.EndsWith("00000", StringComparison.Ordinal))
        {
            return Regon.TryParse(value.AsSpan(0, 9), out var baseRegon) ? baseRegon : null;
        }

        return Regon.TryParse(value, out var regon) ? regon : null;
    }

    private static string? FormatAddress(KrsAdres? address)
    {
        if (address is null)
        {
            return null;
        }

        var houseNumber = address.NrDomu is null ? null : address.NrLokalu is null ? address.NrDomu : address.NrDomu + "/" + address.NrLokalu;
        var first = JoinNonEmpty(" ", address.Ulica ?? address.Miejscowosc, houseNumber);
        var second = JoinNonEmpty(" ", address.KodPocztowy, address.Poczta ?? address.Miejscowosc);
        var result = JoinNonEmpty(", ", first, second);
        return result.Length == 0 ? null : result;
    }

    private static string JoinNonEmpty(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string MaskedName(KrsOsoba person)
    {
        if (person.Nazwa is { Length: > 0 } entityName)
        {
            return entityName;
        }

        var surname = person.Nazwisko?.NazwiskoIICzlon is { Length: > 0 } second
            ? person.Nazwisko.NazwiskoICzlon + "-" + second
            : person.Nazwisko?.NazwiskoICzlon;
        return JoinNonEmpty(" ", person.Imiona?.Imie, person.Imiona?.ImieDrugie, surname);
    }

    private static KrsPersonSummary ToPerson(KrsOsoba person) =>
        new(MaskedName(person), person.FunkcjaWOrganie, person.CzyZawieszona);

    private static KrsBody ToBody(KrsOrgan body) =>
        new(body.NazwaOrganu ?? body.Nazwa ?? string.Empty, body.SposobReprezentacji, body.Sklad?.Select(ToPerson).ToList() ?? []);

    private static KrsShareholderSummary ToShareholder(KrsOsoba person) =>
        new(
            MaskedName(person),
            person.Nazwa is { Length: > 0 },
            KrsNumber.TryParse(person.Krs?.Krs, out var krs) ? krs : null,
            NormalizeRegon(person.Identyfikator?.Regon),
            person.PosiadaneUdzialy,
            person.CzyPosiadaCaloscUdzialow);

    private static KrsActivity ToActivity(KrsPkd pkd) => new(pkd.Code, pkd.Opis ?? string.Empty);
}
