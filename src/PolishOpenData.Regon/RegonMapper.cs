using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using PolishOpenData.Bir.Wire;

namespace PolishOpenData.Bir;

/// <summary>
/// Turns the flat rows of GUS (<see cref="BirRow"/>) into the public models. It never throws on data: a missing or empty
/// value, an unparseable date or number and an invalid identifier all become <c>null</c> (or <c>Unknown</c>), so one bad
/// field never fails a whole answer. The field names come from <see cref="RegonFields"/>.
/// </summary>
internal static class RegonMapper
{
    // StanDanych: "dd-MM-yyyy" on the test service, documented as 19 characters (research-gus-bir/api-facts.md section 5.6 and 10).
    private static readonly string[] DataDateFormats =
    [
        "dd-MM-yyyy",
        "yyyy-MM-dd",
        "dd.MM.yyyy",
        "dd-MM-yyyy HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "dd.MM.yyyy HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
    ];

    /// <summary>Maps one row of <c>DaneSzukajPodmioty</c>. A search row carries names only, so the TERYT codes stay <c>null</c>.</summary>
    public static RegonEntity MapEntity(BirRow searchRow)
    {
        ArgumentNullException.ThrowIfNull(searchRow);
        return new RegonEntity
        {
            Regon = ParseRegon(Text(searchRow[RegonFields.SearchRegon])),
            Nip = ParseNip(Text(searchRow[RegonFields.SearchNip])),
            StatusNip = Text(searchRow[RegonFields.SearchStatusNip]),
            Name = Text(searchRow[RegonFields.SearchName]) ?? string.Empty,
            Address = new RegonAddress
            {
                Voivodeship = Text(searchRow[RegonFields.SearchVoivodeship]),
                County = Text(searchRow[RegonFields.SearchCounty]),
                Commune = Text(searchRow[RegonFields.SearchCommune]),
                City = Text(searchRow[RegonFields.SearchCity]),
                PostTown = Text(searchRow[RegonFields.SearchPostTown]),
                Street = Text(searchRow[RegonFields.SearchStreet]),
                PostalCode = NormalizePostalCode(searchRow[RegonFields.SearchPostalCode]),
                BuildingNumber = Text(searchRow[RegonFields.SearchBuildingNumber]),
                ApartmentNumber = Text(searchRow[RegonFields.SearchApartmentNumber]),
            },
            Type = ParseType(searchRow[RegonFields.SearchType]),
            Silo = ParseSilo(searchRow[RegonFields.SearchSiloId]),
            EndDate = ParseDate(searchRow[RegonFields.SearchEndDate]),
        };
    }

    /// <summary>Maps the address of a row of a full report (the field names of the <c>Address*</c> tables). <see cref="MapEntity"/> maps the address of a search row itself.</summary>
    public static RegonAddress MapAddress(BirRow reportRow)
    {
        ArgumentNullException.ThrowIfNull(reportRow);
        return new RegonAddress
        {
            CountryCode = First(reportRow, RegonFields.AddressCountryCode),
            VoivodeshipCode = First(reportRow, RegonFields.AddressVoivodeshipCode),
            CountyCode = First(reportRow, RegonFields.AddressCountyCode),
            CommuneCode = First(reportRow, RegonFields.AddressCommuneCode),
            CityCode = First(reportRow, RegonFields.AddressCityCode),
            PostTownCode = First(reportRow, RegonFields.AddressPostTownCode),
            StreetCode = First(reportRow, RegonFields.AddressStreetCode),
            Voivodeship = First(reportRow, RegonFields.AddressVoivodeship),
            County = First(reportRow, RegonFields.AddressCounty),
            Commune = First(reportRow, RegonFields.AddressCommune),
            City = First(reportRow, RegonFields.AddressCity),
            PostTown = First(reportRow, RegonFields.AddressPostTown),
            Street = First(reportRow, RegonFields.AddressStreet),
            PostalCode = NormalizePostalCode(First(reportRow, RegonFields.AddressPostalCode)),
            BuildingNumber = First(reportRow, RegonFields.AddressBuildingNumber),
            ApartmentNumber = First(reportRow, RegonFields.AddressApartmentNumber),
        };
    }

    /// <summary>
    /// Maps the core reports of one entity. The rows are merged first (a later row wins on a shared field name, so the
    /// activity report of a natural person, which comes after the general one, decides), then read through the tables.
    /// </summary>
    public static RegonDetails MapDetails(
        RegonEntityType type,
        RegonSilo silo,
        IReadOnlyList<BirRow> coreRows,
        IReadOnlyList<RegonPkd>? pkd,
        IReadOnlyList<RegonLocalUnit>? localUnits,
        IReadOnlyList<RegonPartner>? partners)
    {
        ArgumentNullException.ThrowIfNull(coreRows);
        var row = Merge(coreRows);
        return new RegonDetails
        {
            Type = type,
            Silo = silo,
            Regon = ParseRegon(First(row, RegonFields.Regon9) ?? First(row, RegonFields.Regon14)),
            Nip = ParseNip(First(row, RegonFields.Nip)),
            StatusNip = First(row, RegonFields.StatusNip),
            Name = First(row, RegonFields.Name) ?? string.Empty,
            ShortName = First(row, RegonFields.ShortName),
            LegalFormCode = First(row, RegonFields.LegalFormCode),
            LegalFormName = First(row, RegonFields.LegalFormName),
            RegisterTypeCode = First(row, RegonFields.RegisterTypeCode),
            RegisterTypeName = First(row, RegonFields.RegisterTypeName),
            RegisterNumber = First(row, RegonFields.RegisterNumber),
            RegisterEntryDate = ParseDate(First(row, RegonFields.RegisterEntryDate)),
            FormationDate = ParseDate(First(row, RegonFields.FormationDate)),
            ActivityStartDate = ParseDate(First(row, RegonFields.ActivityStartDate)),
            RegonEntryDate = ParseDate(First(row, RegonFields.RegonEntryDate)),
            SuspensionDate = ParseDate(First(row, RegonFields.SuspensionDate)),
            ResumptionDate = ParseDate(First(row, RegonFields.ResumptionDate)),
            EndDate = ParseDate(First(row, RegonFields.EndDate)),
            RegonRemovalDate = ParseDate(First(row, RegonFields.RegonRemovalDate)),
            BankruptcyDate = ParseDate(First(row, RegonFields.BankruptcyDate)),
            LocalUnitCount = ParseInt(First(row, RegonFields.LocalUnitCount)),
            HasCeidgActivity = ParseFlag(First(row, RegonFields.HasCeidgActivity)),
            HasAgriculturalActivity = ParseFlag(First(row, RegonFields.HasAgriculturalActivity)),
            HasOtherActivity = ParseFlag(First(row, RegonFields.HasOtherActivity)),
            HasActivityRemovedBefore2014 = ParseFlag(First(row, RegonFields.HasActivityRemovedBefore2014)),
            ActivityNotStarted = ParseFlag(First(row, RegonFields.ActivityNotStarted)),
            Address = MapAddress(row),
            Pkd = pkd,
            LocalUnits = localUnits,
            Partners = partners,
            Fields = NonEmptyFields(row),
        };
    }

    /// <summary>Maps one row of a PKD report. <c>Silo</c> is set only when the row names one (the PKD report of a natural person does).</summary>
    public static RegonPkd MapPkd(BirRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var silo = First(row, RegonFields.PkdSiloId);
        return new RegonPkd
        {
            Code = First(row, RegonFields.PkdCode) ?? string.Empty,
            Name = First(row, RegonFields.PkdName),
            IsPrimary = ParseFlag(First(row, RegonFields.PkdPrimary)) == true,
            Version = First(row, RegonFields.PkdVersion),
            Silo = silo is null ? null : ParseSilo(silo),
        };
    }

    /// <summary>Maps one row of a list of local units.</summary>
    public static RegonLocalUnit MapLocalUnit(BirRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new RegonLocalUnit
        {
            Regon = ParseRegon(First(row, RegonFields.Regon14)),
            Name = First(row, RegonFields.Name) ?? string.Empty,
            Silo = ParseSilo(First(row, RegonFields.LocalUnitSiloId)),
            Address = MapAddress(row),
            FormationDate = ParseDate(First(row, RegonFields.FormationDate)),
            ActivityStartDate = ParseDate(First(row, RegonFields.ActivityStartDate)),
            RegonEntryDate = ParseDate(First(row, RegonFields.RegonEntryDate)),
            SuspensionDate = ParseDate(First(row, RegonFields.SuspensionDate)),
            ResumptionDate = ParseDate(First(row, RegonFields.ResumptionDate)),
            EndDate = ParseDate(First(row, RegonFields.EndDate)),
            RegonRemovalDate = ParseDate(First(row, RegonFields.RegonRemovalDate)),
        };
    }

    /// <summary>Maps one row of the report of the partners of a civil partnership.</summary>
    public static RegonPartner MapPartner(BirRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new RegonPartner
        {
            Regon = ParseRegon(First(row, RegonFields.PartnerRegon)),
            FirstName = First(row, RegonFields.PartnerFirstName),
            SecondName = First(row, RegonFields.PartnerSecondName),
            LastName = First(row, RegonFields.PartnerLastName),
            CompanyName = First(row, RegonFields.PartnerCompanyName),
        };
    }

    /// <summary>Maps one row of any report to a raw record: the non-empty fields only.</summary>
    public static RegonRecord MapRecord(BirRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new RegonRecord(NonEmptyFields(row));
    }

    /// <summary>Joins rows into one. A later row wins on a shared field name, except that an empty value never replaces a filled one.</summary>
    public static BirRow Merge(IReadOnlyList<BirRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var merged = new BirRow();
        foreach (var row in rows)
        {
            foreach (var field in row.Fields)
            {
                if (field.Value.Length > 0)
                {
                    merged.Set(field.Key, field.Value);
                }
            }
        }

        return merged;
    }

    /// <summary>A report date: exactly <c>yyyy-MM-dd</c> (research-gus-bir/api-facts.md section 10); anything else is <c>null</c>.</summary>
    public static DateOnly? ParseDate(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateOnly.FromDateTime(date)
            : null;
    }

    /// <summary>
    /// The date of the data (<c>StanDanych</c>). Accepts <c>dd-MM-yyyy</c> (what the test service sends), <c>yyyy-MM-dd</c> and
    /// <c>dd.MM.yyyy</c>, each with an optional <c>HH:mm:ss</c> tail (the 19 characters of the documentation); anything else is <c>null</c>.
    /// </summary>
    public static DateOnly? ParseDataDate(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return DateTime.TryParseExact(value.Trim(), DataDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateOnly.FromDateTime(date)
            : null;
    }

    /// <summary>Five digits become <c>NN-NNN</c> (reports send them without the dash); a value that has the dash or is anything else is kept, trimmed; empty is <c>null</c>.</summary>
    public static string? NormalizePostalCode(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length == 5 && IsDigits(text))
        {
            return text.Insert(2, "-");
        }

        return text;
    }

    /// <summary><c>P</c>, <c>F</c>, <c>LP</c>, <c>LF</c> (exactly); anything else is <see cref="RegonEntityType.Unknown"/>.</summary>
    public static RegonEntityType ParseType(string? value) => value switch
    {
        "P" => RegonEntityType.LegalPerson,
        "F" => RegonEntityType.NaturalPerson,
        "LP" => RegonEntityType.LegalPersonLocalUnit,
        "LF" => RegonEntityType.NaturalPersonLocalUnit,
        _ => RegonEntityType.Unknown,
    };

    /// <summary><c>1</c>, <c>2</c>, <c>3</c>, <c>4</c>, <c>6</c> (exactly); anything else is <see cref="RegonSilo.Unknown"/>.</summary>
    public static RegonSilo ParseSilo(string? value) => value switch
    {
        "1" => RegonSilo.Ceidg,
        "2" => RegonSilo.Agricultural,
        "3" => RegonSilo.Other,
        "4" => RegonSilo.RemovedBefore2014,
        "6" => RegonSilo.LegalPerson,
        _ => RegonSilo.Unknown,
    };

    // The first candidate that holds a value wins. BirRow already answers null for a missing or empty field.
    private static string? First(BirRow row, string[] table)
    {
        foreach (var name in table)
        {
            var value = Text(row[name]);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // Core's Regon and Nip are strict (mod 11 check digit): an invalid value is null, never an exception.
    private static Regon? ParseRegon(string? value) =>
        value is not null && Regon.TryParse(value, out var regon) ? regon : null;

    private static Nip? ParseNip(string? value) =>
        value is not null && Nip.TryParse(value, out var nip) ? nip : null;

    // "1" and "true" are true, "0" and "false" are false, case-insensitive; fiz_dzialalnoscCeidg is an int, fizC_NiePodjetoDzialalnosci an xs:boolean
    // whose wire form is not known (research-gus-bir/api-facts.md section 10).
    private static bool? ParseFlag(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (string.Equals(value, "1", StringComparison.Ordinal) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "0", StringComparison.Ordinal) || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static int? ParseInt(string? value) =>
        value is not null && int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static bool IsDigits(string text)
    {
        foreach (var c in text)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static ReadOnlyDictionary<string, string> NonEmptyFields(BirRow row)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in row.Fields)
        {
            if (field.Value.Length > 0)
            {
                fields[field.Key] = field.Value;
            }
        }

        return new ReadOnlyDictionary<string, string>(fields);
    }
}
