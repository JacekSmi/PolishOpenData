using System;
using System.Collections.Generic;

namespace PolishOpenData.Bir;

/// <summary>GUS BIR12 field names the mapper reads, and the ones it deliberately leaves in the raw dictionary.</summary>
internal static class RegonFields
{
    // search rows (DaneSzukajPodmioty)
    public const string SearchRegon = "Regon";
    public const string SearchNip = "Nip";
    public const string SearchStatusNip = "StatusNip";
    public const string SearchName = "Nazwa";
    public const string SearchVoivodeship = "Wojewodztwo";
    public const string SearchCounty = "Powiat";
    public const string SearchCommune = "Gmina";
    public const string SearchCity = "Miejscowosc";
    public const string SearchPostalCode = "KodPocztowy";
    public const string SearchStreet = "Ulica";
    public const string SearchBuildingNumber = "NrNieruchomosci";
    public const string SearchApartmentNumber = "NrLokalu";
    public const string SearchType = "Typ";
    public const string SearchSiloId = "SilosID";
    public const string SearchEndDate = "DataZakonczeniaDzialalnosci";
    public const string SearchPostTown = "MiejscowoscPoczty";

    // report rows: candidate field names per property, in priority order
    public static readonly string[] Regon9 = ["praw_regon9", "fiz_regon9"];
    public static readonly string[] Regon14 = ["lokpraw_regon14", "lokfiz_regon14"];
    public static readonly string[] Nip = ["praw_nip", "fiz_nip", "lokpraw_nip"];
    public static readonly string[] StatusNip = ["praw_statusNip", "fiz_statusNip", "lokpraw_statusNip"];
    public static readonly string[] Name = ["praw_nazwa", "fiz_nazwa", "lokpraw_nazwa", "lokfiz_nazwa"];
    public static readonly string[] ShortName = ["praw_nazwaSkrocona", "fiz_nazwaSkrocona"];
    public static readonly string[] LegalFormCode = ["praw_podstawowaFormaPrawna_Symbol", "fiz_podstawowaFormaPrawna_Symbol"];
    public static readonly string[] LegalFormName = ["praw_podstawowaFormaPrawna_Nazwa", "fiz_podstawowaFormaPrawna_Nazwa"];
    public static readonly string[] RegisterTypeCode = ["praw_rodzajRejestruEwidencji_Symbol", "fizC_RodzajRejestru_Symbol", "fizP_RodzajRejestru_Symbol", "lokpraw_rodzajRejestruEwidencji_Symbol", "lokfiz_RodzajRejestru_Symbol"];
    public static readonly string[] RegisterTypeName = ["praw_rodzajRejestruEwidencji_Nazwa", "fizC_RodzajRejestru_Nazwa", "fizP_RodzajRejestru_Nazwa", "lokpraw_rodzajRejestruEwidencji_Nazwa", "lokfiz_RodzajRejestru_Nazwa"];
    public static readonly string[] RegisterNumber = ["praw_numerWRejestrzeEwidencji", "fizC_numerWRejestrzeEwidencji", "fizP_numerWRejestrzeEwidencji", "lokpraw_numerWrejestrzeEwidencji", "lokfiz_numerwRejestrzeEwidencji"];
    public static readonly string[] RegisterEntryDate = ["praw_dataWpisuDoRejestruEwidencji", "fizC_dataWpisuDoRejestruEwidencji", "fizP_dataWpisuDoRejestruEwidencji", "lokpraw_dataWpisuDoRejestruEwidencji", "lokfiz_dataWpisuDoRejestruEwidencji"];
    public static readonly string[] FormationDate = ["praw_dataPowstania", "fiz_dataPowstania", "lokpraw_dataPowstania", "lokfiz_dataPowstania"];
    public static readonly string[] ActivityStartDate = ["praw_dataRozpoczeciaDzialalnosci", "fiz_dataRozpoczeciaDzialalnosci", "lokpraw_dataRozpoczeciaDzialalnosci", "lokfiz_dataRozpoczeciaDzialalnosci"];
    public static readonly string[] RegonEntryDate = ["praw_dataWpisuDoRegon", "fiz_dataWpisuDzialalnosciDoRegon", "fiz_dataWpisuPodmiotuDoRegon", "lokpraw_dataWpisuDoRegon", "lokfiz_dataWpisuDoRegon"];
    public static readonly string[] SuspensionDate = ["praw_dataZawieszeniaDzialalnosci", "fiz_dataZawieszeniaDzialalnosci", "lokpraw_dataZawieszeniaDzialalnosci", "lokfiz_dataZawieszeniaDzialalnosci"];
    public static readonly string[] ResumptionDate = ["praw_dataWznowieniaDzialalnosci", "fiz_dataWznowieniaDzialalnosci", "lokpraw_dataWznowieniaDzialalnosci", "lokfiz_dataWznowieniaDzialalnosci"];
    public static readonly string[] EndDate = ["praw_dataZakonczeniaDzialalnosci", "fiz_dataZakonczeniaDzialalnosci", "lokpraw_dataZakonczeniaDzialalnosci", "lokfiz_dataZakonczeniaDzialalnosci"];
    public static readonly string[] RegonRemovalDate = ["praw_dataSkresleniaZRegon", "fiz_dataSkresleniaDzialalnosciZRegon", "fiz_dataSkresleniaDzialalanosciZRegon", "fiz_dataSkresleniaPodmiotuZRegon", "lokpraw_dataSkresleniaZRegon", "lokfiz_dataSkresleniaZRegon"];
    public static readonly string[] BankruptcyDate = ["praw_dataOrzeczeniaOUpadlosci", "fiz_dataOrzeczeniaOUpadlosci"];
    public static readonly string[] LocalUnitCount = ["praw_liczbaJednLokalnych", "fiz_liczbaJednLokalnych"];
    public static readonly string[] HasCeidgActivity = ["fiz_dzialalnoscCeidg"];
    public static readonly string[] HasAgriculturalActivity = ["fiz_dzialalnoscRolnicza"];
    public static readonly string[] HasOtherActivity = ["fiz_dzialalnoscPozostala"];
    public static readonly string[] HasActivityRemovedBefore2014 = ["fiz_dzialalnoscSkreslonaDo20141108"];
    public static readonly string[] ActivityNotStarted = ["fizC_NiePodjetoDzialalnosci", "lokfizC_NiePodjetoDzialalnosci"];
    public static readonly string[] AddressCountryCode = ["praw_adSiedzKraj_Symbol", "fiz_adSiedzKraj_Symbol", "lokpraw_adSiedzKraj_Symbol", "lokfiz_adSiedzKraj_Symbol"];
    public static readonly string[] AddressVoivodeshipCode = ["praw_adSiedzWojewodztwo_Symbol", "fiz_adSiedzWojewodztwo_Symbol", "lokpraw_adSiedzWojewodztwo_Symbol", "lokfiz_adSiedzWojewodztwo_Symbol"];
    public static readonly string[] AddressCountyCode = ["praw_adSiedzPowiat_Symbol", "fiz_adSiedzPowiat_Symbol", "lokpraw_adSiedzPowiat_Symbol", "lokfiz_adSiedzPowiat_Symbol"];
    public static readonly string[] AddressCommuneCode = ["praw_adSiedzGmina_Symbol", "fiz_adSiedzGmina_Symbol", "lokpraw_adSiedzGmina_Symbol", "lokfiz_adSiedzGmina_Symbol"];
    public static readonly string[] AddressCityCode = ["praw_adSiedzMiejscowosc_Symbol", "fiz_adSiedzMiejscowosc_Symbol", "lokpraw_adSiedzMiejscowosc_Symbol", "lokfiz_adSiedzMiejscowosc_Symbol"];
    public static readonly string[] AddressPostTownCode = ["praw_adSiedzMiejscowoscPoczty_Symbol", "fiz_adSiedzMiejscowoscPoczty_Symbol", "lokpraw_adSiedzMiejscowoscPoczty_Symbol", "lokfiz_adSiedzMiejscowoscPoczty_Symbol"];
    public static readonly string[] AddressStreetCode = ["praw_adSiedzUlica_Symbol", "fiz_adSiedzUlica_Symbol", "lokpraw_adSiedzUlica_Symbol", "lokfiz_adSiedzUlica_Symbol"];
    public static readonly string[] AddressVoivodeship = ["praw_adSiedzWojewodztwo_Nazwa", "fiz_adSiedzWojewodztwo_Nazwa", "lokpraw_adSiedzWojewodztwo_Nazwa", "lokfiz_adSiedzWojewodztwo_Nazwa"];
    public static readonly string[] AddressCounty = ["praw_adSiedzPowiat_Nazwa", "fiz_adSiedzPowiat_Nazwa", "lokpraw_adSiedzPowiat_Nazwa", "lokfiz_adSiedzPowiat_Nazwa"];
    public static readonly string[] AddressCommune = ["praw_adSiedzGmina_Nazwa", "fiz_adSiedzGmina_Nazwa", "lokpraw_adSiedzGmina_Nazwa", "lokfiz_adSiedzGmina_Nazwa"];
    public static readonly string[] AddressCity = ["praw_adSiedzMiejscowosc_Nazwa", "fiz_adSiedzMiejscowosc_Nazwa", "lokpraw_adSiedzMiejscowosc_Nazwa", "lokfiz_adSiedzMiejscowosc_Nazwa"];
    public static readonly string[] AddressPostTown = ["praw_adSiedzMiejscowoscPoczty_Nazwa", "fiz_adSiedzMiejscowoscPoczty_Nazwa", "lokpraw_adSiedzMiejscowoscPoczty_Nazwa", "lokfiz_adSiedzMiejscowoscPoczty_Nazwa"];
    public static readonly string[] AddressStreet = ["praw_adSiedzUlica_Nazwa", "fiz_adSiedzUlica_Nazwa", "lokpraw_adSiedzUlica_Nazwa", "lokfiz_adSiedzUlica_Nazwa"];
    public static readonly string[] AddressPostalCode = ["praw_adSiedzKodPocztowy", "fiz_adSiedzKodPocztowy", "lokpraw_adSiedzKodPocztowy", "lokfiz_adSiedzKodPocztowy"];
    public static readonly string[] AddressBuildingNumber = ["praw_adSiedzNumerNieruchomosci", "fiz_adSiedzNumerNieruchomosci", "lokpraw_adSiedzNumerNieruchomosci", "lokfiz_adSiedzNumerNieruchomosci"];
    public static readonly string[] AddressApartmentNumber = ["praw_adSiedzNumerLokalu", "fiz_adSiedzNumerLokalu", "lokpraw_adSiedzNumerLokalu", "lokfiz_adSiedzNumerLokalu"];
    public static readonly string[] PkdCode = ["praw_pkdKod", "fiz_pkd_Kod", "lokpraw_pkdKod", "lokfiz_pkd_Kod"];
    public static readonly string[] PkdName = ["praw_pkdNazwa", "fiz_pkd_Nazwa", "lokpraw_pkdNazwa", "lokfiz_pkd_Nazwa"];
    public static readonly string[] PkdPrimary = ["praw_pkdPrzewazajace", "fiz_pkd_Przewazajace", "lokpraw_pkdPrzewazajace", "lokfiz_pkd_Przewazajace"];
    public static readonly string[] PkdVersion = ["praw_pkdWersja", "fiz_pkdWersja", "lokpraw_pkdWersja", "lokfiz_pkdWersja"];
    public static readonly string[] PkdSiloId = ["fiz_SilosID"];
    public static readonly string[] LocalUnitSiloId = ["lokfiz_silosID"];
    public static readonly string[] PartnerRegon = ["wspolsc_regonWspolnikSpolki"];
    public static readonly string[] PartnerFirstName = ["wspolsc_imiePierwsze"];
    public static readonly string[] PartnerSecondName = ["wspolsc_imieDrugie"];
    public static readonly string[] PartnerLastName = ["wspolsc_nazwisko"];
    public static readonly string[] PartnerCompanyName = ["wspolsc_firmaNazwa"];
    public static readonly string[] EntityType = ["Typ"];

    private static readonly string[][] AllTables =
    [
        Regon9, Regon14, Nip, StatusNip,
        Name, ShortName, LegalFormCode, LegalFormName,
        RegisterTypeCode, RegisterTypeName, RegisterNumber, RegisterEntryDate,
        FormationDate, ActivityStartDate, RegonEntryDate, SuspensionDate,
        ResumptionDate, EndDate, RegonRemovalDate, BankruptcyDate,
        LocalUnitCount, HasCeidgActivity, HasAgriculturalActivity, HasOtherActivity,
        HasActivityRemovedBefore2014, ActivityNotStarted, AddressCountryCode, AddressVoivodeshipCode,
        AddressCountyCode, AddressCommuneCode, AddressCityCode, AddressPostTownCode,
        AddressStreetCode, AddressVoivodeship, AddressCounty, AddressCommune,
        AddressCity, AddressPostTown, AddressStreet, AddressPostalCode,
        AddressBuildingNumber, AddressApartmentNumber, PkdCode, PkdName,
        PkdPrimary, PkdVersion, PkdSiloId, LocalUnitSiloId,
        PartnerRegon, PartnerFirstName, PartnerSecondName, PartnerLastName,
        PartnerCompanyName, EntityType,
    ];

    /// <summary>Every report field name that some table reads (the search-row names above are not report fields).</summary>
    public static readonly HashSet<string> Mapped = BuildMapped();

    // Raw only on purpose (they stay in RegonDetails.Fields and RegonRecord.Fields): contact data (*_numerTelefonu,
    // *_numerWewnetrznyTelefonu, *_numerFaksu, *_adresEmail, *_adresEmail2, *_adresStronyinternetowej) and the person-name
    // fields fiz_imie1, fiz_imie2 and fiz_nazwisko (data minimisation); the funding, ownership, specific legal
    // form, founding organ and registry organ codes and names; *_adSiedzKraj_Nazwa and *_adSiedzNietypoweMiejsceLokalizacji;
    // *_dataZaistnieniaZmiany*; *_dataZakonczeniaPostepowaniaUpadlosciowego; fizC_dataSkresleniaZRejestruEwidencji; and the
    // silo symbol and name text fields (fiz_Silos_Symbol, lokfiz_Silos_Symbol, lokfiz_silos_Symbol, lokfiz_silos_Nazwa).
    // The field names are those of tests/Fixtures/regon/bir12-field-names.tsv.
    /// <summary>Report field names that are deliberately not typed; they stay available in <c>RegonDetails.Fields</c> / <c>RegonRecord.Fields</c>.</summary>
    public static readonly HashSet<string> RawOnly = new(StringComparer.Ordinal)
    {
        "fizC_OrganRejestrowy_Nazwa", "fizC_OrganRejestrowy_Symbol", "fizC_dataSkresleniaZRejestruEwidencji",
        "fizP_OrganRejestrowy_Nazwa", "fizP_OrganRejestrowy_Symbol", "fiz_Silos_Symbol",
        "fiz_adSiedzKraj_Nazwa", "fiz_adSiedzNietypoweMiejsceLokalizacji", "fiz_adresEmail",
        "fiz_adresEmail2", "fiz_adresStronyinternetowej", "fiz_dataZaistnieniaZmiany",
        "fiz_dataZaistnieniaZmianyDzialalnosci", "fiz_dataZakonczeniaPostepowaniaUpadlosciowego", "fiz_formaFinansowania_Nazwa",
        "fiz_formaFinansowania_Symbol", "fiz_formaWlasnosci_Nazwa", "fiz_formaWlasnosci_Symbol",
        "fiz_imie1", "fiz_imie2", "fiz_nazwisko",
        "fiz_numerFaksu", "fiz_numerTelefonu", "fiz_numerWewnetrznyTelefonu",
        "fiz_szczegolnaFormaPrawna_Nazwa", "fiz_szczegolnaFormaPrawna_Symbol", "lokfiz_FormaFinansowania_Nazwa",
        "lokfiz_FormaFinansowania_Symbol", "lokfiz_OrganRejestrowy_Nazwa", "lokfiz_OrganRejestrowy_Symbol",
        "lokfiz_Silos_Symbol", "lokfiz_adSiedzKraj_Nazwa", "lokfiz_adSiedzNietypoweMiejsceLokalizacji",
        "lokfiz_dataZaistnieniaZmiany", "lokfiz_silos_Nazwa", "lokfiz_silos_Symbol",
        "lokpraw_adSiedzKraj_Nazwa", "lokpraw_adSiedzNietypoweMiejsceLokalizacji", "lokpraw_dataZaistnieniaZmiany",
        "lokpraw_formaFinansowania_Nazwa", "lokpraw_formaFinansowania_Symbol", "lokpraw_organRejestrowy_Nazwa",
        "lokpraw_organRejestrowy_Symbol", "praw_adSiedzKraj_Nazwa", "praw_adSiedzNietypoweMiejsceLokalizacji",
        "praw_adresEmail", "praw_adresStronyinternetowej", "praw_dataZaistnieniaZmiany",
        "praw_dataZakonczeniaPostepowaniaUpadlosciowego", "praw_formaFinansowania_Nazwa", "praw_formaFinansowania_Symbol",
        "praw_formaWlasnosci_Nazwa", "praw_formaWlasnosci_Symbol", "praw_numerFaksu",
        "praw_numerTelefonu", "praw_numerWewnetrznyTelefonu", "praw_organRejestrowy_Nazwa",
        "praw_organRejestrowy_Symbol", "praw_organZalozycielski_Nazwa", "praw_organZalozycielski_Symbol",
        "praw_szczegolnaFormaPrawna_Nazwa", "praw_szczegolnaFormaPrawna_Symbol",
    };

    private static HashSet<string> BuildMapped()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in AllTables)
        {
            foreach (var name in table)
            {
                set.Add(name);
            }
        }

        return set;
    }
}
