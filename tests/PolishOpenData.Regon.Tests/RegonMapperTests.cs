using System;
using System.Collections.Generic;
using System.Linq;
using PolishOpenData.Bir.Wire;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Bir.Tests;

// Every expectation below is a value pinned by the fixtures of Task 2; the tests do not depend on how
// the fixtures fill the fields that table leaves open.
public class RegonMapperTests
{
    private static List<BirRow> Rows(string fixture) => BirReader.ReadRows(RegonTestResponses.Inner(fixture));

    private static BirRow OneRow(string fixture) => Assert.Single(Rows(fixture));

    private static BirRow Row(params (string Name, string Value)[] fields)
    {
        var row = new BirRow();
        foreach (var (name, value) in fields)
        {
            row.Set(name, value);
        }

        return row;
    }

    // A recorded answer goes through the real pipeline: the MTOM bytes, the Content-Type of the .headers file, the envelope, the inner result.
    private static List<BirRow> RecordedRows(string name, string resultElement)
    {
        var contentType = Fixture.Read("regon/" + name + ".headers").Split('\n')[1].Substring("Content-Type: ".Length).TrimEnd('\r');
        var package = MimeResponse.Parse(Fixture.ReadBytes("regon/" + name + ".mtom"), contentType);
        var inner = BirReader.ReadResult(package, resultElement, BirEnvelope.Publ);
        Assert.NotNull(inner);
        return BirReader.ReadRows(inner);
    }

    private static RegonDetails Details(RegonEntityType type, RegonSilo silo, params BirRow[] rows) =>
        RegonMapper.MapDetails(type, silo, rows, null, null, null);

    [Fact]
    public void Maps_the_recorded_search_row_of_gus()
    {
        var row = Assert.Single(RecordedRows("search-regon-gus", "DaneSzukajPodmiotyResult"));

        var entity = RegonMapper.MapEntity(row);

        Assert.Equal(Regon.Parse("000331501"), entity.Regon);
        Assert.Equal(Nip.Parse("5261040828"), entity.Nip);
        Assert.Null(entity.StatusNip);
        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", entity.Name);
        Assert.Equal("MAZOWIECKIE", entity.Address.Voivodeship);
        Assert.Equal("m. st. Warszawa", entity.Address.County);
        Assert.Equal("Śródmieście", entity.Address.Commune);
        Assert.Equal("Warszawa", entity.Address.City);
        Assert.Equal("Warszawa", entity.Address.PostTown);
        Assert.Equal("00-925", entity.Address.PostalCode);
        Assert.Equal("ul. Test-Krucza", entity.Address.Street);
        Assert.Equal("208", entity.Address.BuildingNumber);
        Assert.Null(entity.Address.ApartmentNumber);
        Assert.Equal(RegonEntityType.LegalPerson, entity.Type);
        Assert.Equal(RegonSilo.LegalPerson, entity.Silo);
        Assert.Null(entity.EndDate);
        Assert.Null(entity.Address.CountryCode);
        Assert.Null(entity.Address.VoivodeshipCode);
        Assert.Null(entity.Address.StreetCode);
    }

    [Fact]
    public void Search_postal_code_keeps_the_dash()
    {
        var gus = RegonMapper.MapEntity(Assert.Single(RecordedRows("search-regon-gus", "DaneSzukajPodmiotyResult")));
        var person = Rows("synthetic-search-natural-person").Select(RegonMapper.MapEntity).ToArray();

        Assert.Equal("00-925", gus.Address.PostalCode);
        Assert.Equal("00-001", person[0].Address.PostalCode);
        Assert.Equal("05-500", person[1].Address.PostalCode);
    }

    [Fact]
    public void A_search_row_without_a_street_element_has_a_null_street()
    {
        var rows = Rows("synthetic-search-natural-person");
        Assert.DoesNotContain(rows[1].Fields, field => string.Equals(field.Key, "Ulica", StringComparison.Ordinal));

        var entities = rows.Select(RegonMapper.MapEntity).ToArray();

        Assert.Equal("ul. Test-Prosta", entities[0].Address.Street);
        Assert.Null(entities[1].Address.Street);
    }

    [Fact]
    public void Maps_the_search_rows_of_a_natural_person_and_of_local_units()
    {
        var person = Rows("synthetic-search-natural-person").Select(RegonMapper.MapEntity).ToArray();
        var legalUnit = RegonMapper.MapEntity(OneRow("synthetic-search-local-unit-lp"));
        var naturalUnit = RegonMapper.MapEntity(OneRow("synthetic-search-local-unit-lf"));

        Assert.Equal(2, person.Length);
        Assert.All(person, e => Assert.Equal(Regon.Parse("999999990"), e.Regon));
        Assert.All(person, e => Assert.Equal(Nip.Parse("9999999999"), e.Nip));
        Assert.All(person, e => Assert.Equal("JAN KOWALSKI", e.Name));
        Assert.All(person, e => Assert.Equal(RegonEntityType.NaturalPerson, e.Type));
        Assert.Equal(RegonSilo.Ceidg, person[0].Silo);
        Assert.Equal("2", person[0].Address.ApartmentNumber);
        Assert.Null(person[0].EndDate);
        Assert.Equal(RegonSilo.Agricultural, person[1].Silo);
        Assert.Equal("piaseczyński", person[1].Address.County);
        Assert.Equal(new DateOnly(2020, 12, 31), person[1].EndDate);
        Assert.Equal(Regon.Parse("14726216300027"), legalUnit.Regon);
        Assert.Equal(RegonEntityType.LegalPersonLocalUnit, legalUnit.Type);
        Assert.Equal("02-672", legalUnit.Address.PostalCode);
        Assert.Equal(Regon.Parse("99999999000026"), naturalUnit.Regon);
        Assert.Equal(RegonEntityType.NaturalPersonLocalUnit, naturalUnit.Type);
        Assert.Equal(RegonSilo.Ceidg, naturalUnit.Silo);
    }

    [Fact]
    public void Maps_osprawna_of_gus_to_the_typed_core()
    {
        var details = Details(RegonEntityType.LegalPerson, RegonSilo.LegalPerson, OneRow("synthetic-report-osprawna-gus"));

        Assert.Equal(RegonEntityType.LegalPerson, details.Type);
        Assert.Equal(RegonSilo.LegalPerson, details.Silo);
        Assert.Equal(Regon.Parse("000331501"), details.Regon);
        Assert.Equal(Nip.Parse("5261040828"), details.Nip);
        Assert.Null(details.StatusNip);
        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", details.Name);
        Assert.Equal("GUS", details.ShortName);
        Assert.Equal("125", details.LegalFormCode);
        Assert.Equal("PAŃSTWOWA JEDNOSTKA BUDŻETOWA", details.LegalFormName);
        Assert.Equal("000", details.RegisterTypeCode);
        Assert.Equal("BRAK REJESTRU", details.RegisterTypeName);
        Assert.Equal("0000135115", details.RegisterNumber);
        Assert.Equal(new DateOnly(1990, 1, 2), details.RegisterEntryDate);
        Assert.Equal(new DateOnly(1975, 12, 15), details.FormationDate);
        Assert.Equal(new DateOnly(1975, 12, 15), details.ActivityStartDate);
        Assert.Equal(new DateOnly(1990, 1, 2), details.RegonEntryDate);
        Assert.Null(details.EndDate);
        Assert.Null(details.SuspensionDate);
        Assert.Null(details.RegonRemovalDate);
        Assert.Equal(1, details.LocalUnitCount);
        Assert.Equal("PL", details.Address.CountryCode);
        Assert.Equal("14", details.Address.VoivodeshipCode);
        Assert.Equal("65", details.Address.CountyCode);
        Assert.Equal("108", details.Address.CommuneCode);
        Assert.Equal("0918123", details.Address.CityCode);
        Assert.Equal("0918123", details.Address.PostTownCode);
        Assert.Equal("99999", details.Address.StreetCode);
        Assert.Equal("MAZOWIECKIE", details.Address.Voivodeship);
        Assert.Equal("m. st. Warszawa", details.Address.County);
        Assert.Equal("Śródmieście", details.Address.Commune);
        Assert.Equal("Warszawa", details.Address.City);
        Assert.Equal("Warszawa", details.Address.PostTown);
        Assert.Equal("ul. Test-Krucza", details.Address.Street);
        Assert.Equal("00-925", details.Address.PostalCode);
        Assert.Equal("208", details.Address.BuildingNumber);
        Assert.Null(details.Address.ApartmentNumber);
        Assert.Null(details.Pkd);
        Assert.Null(details.LocalUnits);
        Assert.Null(details.Partners);
        Assert.Equal("6083000", details.Fields["praw_numerTelefonu"]);
    }

    [Fact]
    public void Report_postal_code_gets_a_dash()
    {
        var gus = RegonMapper.MapAddress(OneRow("synthetic-report-osprawna-gus"));
        var unit = RegonMapper.MapAddress(OneRow("synthetic-report-jednlokalna-lp-121"));

        Assert.Equal("00-925", gus.PostalCode);
        Assert.Equal("02-672", unit.PostalCode);
    }

    [Fact]
    public void Empty_and_missing_values_map_to_null()
    {
        var entity = RegonMapper.MapEntity(Row(("Nazwa", "X"), ("StatusNip", ""), ("Ulica", ""), ("NrLokalu", "   ")));
        var details = Details(
            RegonEntityType.LegalPerson,
            RegonSilo.LegalPerson,
            Row(("praw_nazwa", "X"), ("praw_nip", ""), ("praw_nazwaSkrocona", ""), ("praw_dataPowstania", ""), ("praw_liczbaJednLokalnych", ""), ("praw_adSiedzUlica_Nazwa", "  ")));

        Assert.Equal("X", entity.Name);
        Assert.Null(entity.StatusNip);
        Assert.Null(entity.Address.Street);
        Assert.Null(entity.Address.ApartmentNumber);
        Assert.Null(entity.Address.County);
        Assert.Null(entity.Regon);
        Assert.Null(entity.EndDate);
        Assert.Equal(RegonEntityType.Unknown, entity.Type);
        Assert.Equal("X", details.Name);
        Assert.Null(details.Nip);
        Assert.Null(details.ShortName);
        Assert.Null(details.FormationDate);
        Assert.Null(details.LocalUnitCount);
        Assert.Null(details.Address.Street);
        Assert.Equal(string.Empty, RegonMapper.MapEntity(Row()).Name);
        Assert.Equal(string.Empty, Details(RegonEntityType.LegalPerson, RegonSilo.LegalPerson).Name);
    }

    [Fact]
    public void Dates_use_the_iso_format_and_invalid_dates_are_null()
    {
        Assert.Equal(new DateOnly(2005, 3, 1), RegonMapper.ParseDate("2005-03-01"));
        Assert.Equal(new DateOnly(1975, 12, 15), RegonMapper.ParseDate("1975-12-15"));
        Assert.Equal(new DateOnly(1700, 6, 15), RegonMapper.ParseDate("1700-06-15"));   // GUS allows dates before 1753 since 2025 (api-facts.md section 10)
        foreach (var invalid in new[] { null, string.Empty, " ", "2005-3-1", "01-03-2005", "2005/03/01", "2005-02-30", "2005-13-01", "2005-03-01 00:00:00", "20050301", "garbage" })
        {
            Assert.Null(RegonMapper.ParseDate(invalid));
        }

        var details = Details(
            RegonEntityType.LegalPerson,
            RegonSilo.LegalPerson,
            Row(("praw_dataPowstania", "2020-13-45"), ("praw_dataRozpoczeciaDzialalnosci", "garbage"), ("praw_dataZakonczeniaDzialalnosci", "2020-12-31")));

        Assert.Null(details.FormationDate);
        Assert.Null(details.ActivityStartDate);
        Assert.Equal(new DateOnly(2020, 12, 31), details.EndDate);
    }

    [Fact]
    public void Codes_keep_their_leading_zeros()
    {
        var details = Details(RegonEntityType.LegalPerson, RegonSilo.LegalPerson, OneRow("synthetic-report-osprawna-gus"));

        Assert.Equal("000", details.RegisterTypeCode);
        Assert.Equal("0918123", details.Address.CityCode);
        Assert.Equal("050000000", details.Fields["praw_organZalozycielski_Symbol"]);
        Assert.Equal("0000135115", details.RegisterNumber);
    }

    [Fact]
    public void Maps_a_natural_person_by_merging_the_general_and_the_activity_row()
    {
        var details = Details(
            RegonEntityType.NaturalPerson,
            RegonSilo.Ceidg,
            OneRow("synthetic-report-fiz-daneogolne"),
            OneRow("synthetic-report-fiz-ceidg"));

        Assert.Equal(RegonEntityType.NaturalPerson, details.Type);
        Assert.Equal(RegonSilo.Ceidg, details.Silo);
        Assert.Equal(Regon.Parse("999999990"), details.Regon);
        Assert.Equal(Nip.Parse("9999999999"), details.Nip);
        Assert.Equal("JAN KOWALSKI", details.Name);
        Assert.Equal("214", details.LegalFormCode);
        Assert.Equal("OSOBY FIZYCZNE PROWADZĄCE DZIAŁALNOŚĆ GOSPODARCZĄ", details.LegalFormName);
        Assert.Equal(1, details.LocalUnitCount);
        Assert.True(details.HasCeidgActivity);
        Assert.True(details.HasAgriculturalActivity);
        Assert.False(details.HasOtherActivity);
        Assert.False(details.HasActivityRemovedBefore2014);
        Assert.False(details.ActivityNotStarted);
        Assert.Equal(new DateOnly(2005, 2, 14), details.FormationDate);
        Assert.Equal(new DateOnly(2005, 3, 1), details.ActivityStartDate);
        Assert.Equal(new DateOnly(2005, 3, 1), details.RegonEntryDate);
        Assert.Equal(new DateOnly(2012, 5, 1), details.SuspensionDate);
        Assert.Equal(new DateOnly(2012, 9, 1), details.ResumptionDate);
        Assert.Equal(new DateOnly(2005, 2, 14), details.RegisterEntryDate);
        Assert.Equal("123456", details.RegisterNumber);
        Assert.Equal("223", details.RegisterTypeCode);
        Assert.Equal("CEIDG", details.RegisterTypeName);
        Assert.Equal("PL", details.Address.CountryCode);
        Assert.Equal("14", details.Address.VoivodeshipCode);
        Assert.Equal("65", details.Address.CountyCode);
        Assert.Equal("108", details.Address.CommuneCode);
        Assert.Equal("0918123", details.Address.CityCode);
        Assert.Equal("88888", details.Address.StreetCode);
        Assert.Equal("MAZOWIECKIE", details.Address.Voivodeship);
        Assert.Equal("Śródmieście", details.Address.Commune);
        Assert.Equal("Warszawa", details.Address.City);
        Assert.Equal("ul. Test-Prosta", details.Address.Street);
        Assert.Equal("00-001", details.Address.PostalCode);
        Assert.Equal("1", details.Address.BuildingNumber);
        Assert.Equal("2", details.Address.ApartmentNumber);
        Assert.Equal("1", details.Fields["fiz_dzialalnoscCeidg"]);
        Assert.Equal("2005-02-14", details.Fields["fiz_dataPowstania"]);
    }

    [Fact]
    public void The_misspelt_rolnicza_removal_date_is_mapped()
    {
        // GUS spells the field fiz_dataSkresleniaDzialalanosciZRegon (extra "a") in the agricultural report and
        // fiz_dataSkresleniaDzialalnosciZRegon in the CEIDG and the other-activity reports (api-facts.md section 5.4).
        var misspelt = Details(RegonEntityType.NaturalPerson, RegonSilo.Agricultural, Row(("fiz_dataSkresleniaDzialalanosciZRegon", "2020-12-31")));
        var correct = Details(RegonEntityType.NaturalPerson, RegonSilo.Ceidg, Row(("fiz_dataSkresleniaDzialalnosciZRegon", "2019-01-31")));
        var fixture = Details(RegonEntityType.NaturalPerson, RegonSilo.Agricultural, OneRow("synthetic-report-fiz-rolnicza"));

        Assert.Equal(new DateOnly(2020, 12, 31), misspelt.RegonRemovalDate);
        Assert.Equal(new DateOnly(2019, 1, 31), correct.RegonRemovalDate);
        Assert.Equal(new DateOnly(2020, 12, 31), fixture.RegonRemovalDate);
        Assert.Equal(new DateOnly(2020, 12, 31), fixture.EndDate);
        Assert.Equal("05-500", fixture.Address.PostalCode);
        Assert.Equal("piaseczyński", fixture.Address.County);
        Assert.Equal("7", fixture.Address.BuildingNumber);
    }

    [Fact]
    public void Maps_a_local_unit_of_a_legal_person_with_nip_from_bir121()
    {
        var details = Details(RegonEntityType.LegalPersonLocalUnit, RegonSilo.LegalPerson, OneRow("synthetic-report-jednlokalna-lp-121"));

        Assert.Equal(Regon.Parse("14726216300027"), details.Regon);
        Assert.Equal(Nip.Parse("5260251049"), details.Nip);
        Assert.Null(details.StatusNip);
        Assert.Equal("ODDZIAŁ TESTOWY W WARSZAWIE", details.Name);
        Assert.Equal("0000135115", details.RegisterNumber);   // lokpraw_numerWrejestrzeEwidencji (lowercase r)
        Assert.Equal("000", details.RegisterTypeCode);
        Assert.Equal(new DateOnly(2003, 7, 1), details.FormationDate);
        Assert.Equal(new DateOnly(2003, 7, 1), details.ActivityStartDate);
        Assert.Equal(new DateOnly(2003, 7, 2), details.RegonEntryDate);
        Assert.Equal("02-672", details.Address.PostalCode);
        Assert.Equal("Warszawa", details.Address.City);
        Assert.Equal("ul. Test-Domaniewska", details.Address.Street);
        Assert.Equal("39", details.Address.BuildingNumber);
    }

    [Fact]
    public void Maps_a_local_unit_of_a_natural_person_with_the_quirky_register_number()
    {
        var details = Details(RegonEntityType.NaturalPersonLocalUnit, RegonSilo.Ceidg, OneRow("synthetic-report-jednlokalna-lf"));

        Assert.Equal(Regon.Parse("99999999000026"), details.Regon);
        Assert.Equal("JAN KOWALSKI", details.Name);
        Assert.Equal("654321", details.RegisterNumber);   // lokfiz_numerwRejestrzeEwidencji (lowercase w)
        Assert.Equal(new DateOnly(2006, 1, 10), details.FormationDate);
        Assert.Equal("00-001", details.Address.PostalCode);
        Assert.Equal("Warszawa", details.Address.City);
        Assert.False(details.ActivityNotStarted);
    }

    [Fact]
    public void Maps_pkd_rows_with_version_and_primary_flag()
    {
        var pkd = Rows("synthetic-report-osprawna-pkd").Select(RegonMapper.MapPkd).ToArray();

        Assert.Equal(3, pkd.Length);
        Assert.Equal("8411Z", pkd[0].Code);
        Assert.Equal("Działalność testowa A", pkd[0].Name);
        Assert.True(pkd[0].IsPrimary);
        Assert.Equal("2007", pkd[0].Version);
        Assert.Null(pkd[0].Silo);
        Assert.Equal("7220Z", pkd[1].Code);
        Assert.False(pkd[1].IsPrimary);
        Assert.Equal("2007", pkd[1].Version);
        Assert.Equal("6201Z", pkd[2].Code);
        Assert.False(pkd[2].IsPrimary);
        Assert.Equal("2025", pkd[2].Version);
    }

    [Fact]
    public void Pkd_silo_comes_from_the_silo_id()
    {
        var pkd = Rows("synthetic-report-fiz-pkd").Select(RegonMapper.MapPkd).ToArray();

        Assert.Equal(2, pkd.Length);
        Assert.Equal("6201Z", pkd[0].Code);
        Assert.Equal("Działalność testowa C", pkd[0].Name);
        Assert.True(pkd[0].IsPrimary);
        Assert.Equal(RegonSilo.Ceidg, pkd[0].Silo);
        Assert.Equal("2025", pkd[0].Version);
        Assert.Equal("0111Z", pkd[1].Code);
        Assert.Equal("Uprawy testowe", pkd[1].Name);
        Assert.Equal(RegonSilo.Agricultural, pkd[1].Silo);
        Assert.Equal("2007", pkd[1].Version);
        Assert.Equal(RegonSilo.Unknown, RegonMapper.MapPkd(Row(("fiz_pkd_Kod", "6201Z"), ("fiz_SilosID", "x"))).Silo);
        Assert.Equal(string.Empty, RegonMapper.MapPkd(Row()).Code);
    }

    [Fact]
    public void Maps_local_unit_list_rows()
    {
        var units = Rows("synthetic-report-osprawna-lista-jednostek").Select(RegonMapper.MapLocalUnit).ToArray();

        Assert.Equal(2, units.Length);
        Assert.Equal(Regon.Parse("00033150100017"), units[0].Regon);
        Assert.Equal(Regon.Parse("00033150100024"), units[1].Regon);
        Assert.Equal("ODDZIAŁ TESTOWY 1", units[0].Name);
        Assert.Equal("ODDZIAŁ TESTOWY 2", units[1].Name);
        Assert.Equal(new DateOnly(1990, 1, 2), units[0].FormationDate);
        Assert.Equal(new DateOnly(2001, 5, 10), units[1].FormationDate);
        Assert.Equal("00-925", units[0].Address.PostalCode);
        Assert.Equal("80-001", units[1].Address.PostalCode);
        Assert.Equal("Warszawa", units[0].Address.City);
        Assert.Equal("Gdańsk", units[1].Address.City);
        Assert.Null(units[0].EndDate);
        Assert.Equal(new DateOnly(2020, 1, 31), units[1].EndDate);
        Assert.Equal(RegonSilo.Unknown, units[0].Silo);
        Assert.Equal(RegonSilo.Ceidg, RegonMapper.MapLocalUnit(Row(("lokfiz_regon14", "99999999000026"), ("lokfiz_silosID", "1"))).Silo);
    }

    [Fact]
    public void Maps_partner_rows()
    {
        var partners = Rows("synthetic-report-osprawna-wspolnicy").Select(RegonMapper.MapPartner).ToArray();

        Assert.Equal(2, partners.Length);
        Assert.Equal(Regon.Parse("123456785"), partners[0].Regon);
        Assert.Equal("JAN", partners[0].FirstName);
        Assert.Null(partners[0].SecondName);
        Assert.Equal("KOWALSKI", partners[0].LastName);
        Assert.Null(partners[0].CompanyName);
        Assert.Equal(Regon.Parse("610188201"), partners[1].Regon);
        Assert.Null(partners[1].FirstName);
        Assert.Null(partners[1].LastName);
        Assert.Equal("PRZYKŁADOWA SPÓŁKA TESTOWA", partners[1].CompanyName);
    }

    [Fact]
    public void Details_fields_hold_only_non_empty_values()
    {
        var details = Details(RegonEntityType.LegalPerson, RegonSilo.LegalPerson, OneRow("synthetic-report-osprawna-gus"));
        var record = RegonMapper.MapRecord(OneRow("synthetic-report-osprawna-gus"));

        Assert.NotEmpty(details.Fields);
        Assert.All(details.Fields.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(record.Fields.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.Equal("5261040828", details.Fields["praw_nip"]);
        Assert.False(details.Fields.ContainsKey("praw_statusNip"));
        Assert.False(details.Fields.ContainsKey("praw_dataZakonczeniaDzialalnosci"));
        Assert.Equal("5261040828", record["praw_nip"]);
        Assert.Null(record["praw_statusNip"]);
        Assert.Equal(details.Fields.Count, record.Fields.Count);
    }

    [Fact]
    public void Contact_and_person_name_fields_stay_raw_only()
    {
        var person = Details(
            RegonEntityType.NaturalPerson,
            RegonSilo.Ceidg,
            OneRow("synthetic-report-fiz-daneogolne"),
            OneRow("synthetic-report-fiz-ceidg"));
        var legal = Details(RegonEntityType.LegalPerson, RegonSilo.LegalPerson, OneRow("synthetic-report-osprawna-gus"));

        Assert.Equal("KOWALSKI", person.Fields["fiz_nazwisko"]);
        Assert.Equal("JAN", person.Fields["fiz_imie1"]);
        Assert.Equal("jan.kowalski@example.test", person.Fields["fiz_adresEmail"]);
        Assert.Equal("6083000", legal.Fields["praw_numerTelefonu"]);
        Assert.Equal("stat.gov.pl", legal.Fields["praw_adresStronyinternetowej"]);
        foreach (var name in new[] { "fiz_nazwisko", "fiz_imie1", "fiz_imie2", "fiz_adresEmail", "fiz_numerTelefonu", "praw_numerTelefonu", "praw_adresStronyinternetowej" })
        {
            Assert.Contains(name, RegonFields.RawOnly);
            Assert.DoesNotContain(name, RegonFields.Mapped);
        }

        // no typed property of the models exists for them (the partner models carry names by contract, they are not looked at here)
        var typedNames = typeof(RegonDetails).GetProperties().Concat(typeof(RegonEntity).GetProperties()).Concat(typeof(RegonAddress).GetProperties())
            .Select(property => property.Name)
            .ToArray();
        foreach (var forbidden in new[] { "Phone", "Fax", "Mail", "Website", "Www", "FirstName", "SecondName", "LastName", "Surname" })
        {
            foreach (var typedName in typedNames)
            {
                Assert.DoesNotContain(forbidden, typedName, StringComparison.OrdinalIgnoreCase);
            }
        }

        // and no typed value of the details of the sole trader equals one of them
        var values = typeof(RegonDetails).GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => (string?)property.GetValue(person))
            .ToArray();
        Assert.DoesNotContain("KOWALSKI", values);
        Assert.DoesNotContain("JAN", values);
        Assert.DoesNotContain("jan.kowalski@example.test", values);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("p")]
    [InlineData("5")]
    [InlineData("0")]
    [InlineData("LPF")]
    public void Unknown_type_and_silo_map_to_unknown(string? value)
    {
        Assert.Equal(RegonEntityType.Unknown, RegonMapper.ParseType(value));
        Assert.Equal(RegonSilo.Unknown, RegonMapper.ParseSilo(value));
        var entity = value is null
            ? RegonMapper.MapEntity(Row())
            : RegonMapper.MapEntity(Row(("Typ", value), ("SilosID", value)));
        Assert.Equal(RegonEntityType.Unknown, entity.Type);
        Assert.Equal(RegonSilo.Unknown, entity.Silo);
    }

    [Theory]
    [InlineData("P", RegonEntityType.LegalPerson)]
    [InlineData("F", RegonEntityType.NaturalPerson)]
    [InlineData("LP", RegonEntityType.LegalPersonLocalUnit)]
    [InlineData("LF", RegonEntityType.NaturalPersonLocalUnit)]
    public void Known_types_map(string value, RegonEntityType expected) => Assert.Equal(expected, RegonMapper.ParseType(value));

    [Theory]
    [InlineData("1", RegonSilo.Ceidg)]
    [InlineData("2", RegonSilo.Agricultural)]
    [InlineData("3", RegonSilo.Other)]
    [InlineData("4", RegonSilo.RemovedBefore2014)]
    [InlineData("6", RegonSilo.LegalPerson)]
    public void Known_silos_map(string value, RegonSilo expected) => Assert.Equal(expected, RegonMapper.ParseSilo(value));

    [Fact]
    public void An_invalid_regon_maps_to_null()
    {
        // 123456789 and 14726216300028 fail the mod-11 check, 1234567890 is not a NIP; Core's Regon and Nip are strict
        var entity = RegonMapper.MapEntity(Row(("Regon", "123456789"), ("Nip", "1234567890"), ("Nazwa", "X")));
        var details = Details(RegonEntityType.LegalPerson, RegonSilo.LegalPerson, Row(("praw_regon9", "123456789"), ("praw_nip", "123")));
        var unit = RegonMapper.MapLocalUnit(Row(("lokpraw_regon14", "14726216300028")));
        var partner = RegonMapper.MapPartner(Row(("wspolsc_regonWspolnikSpolki", "not a regon")));

        Assert.Null(entity.Regon);
        Assert.Null(entity.Nip);
        Assert.Equal("X", entity.Name);
        Assert.Null(details.Regon);
        Assert.Null(details.Nip);
        Assert.Null(unit.Regon);
        Assert.Null(partner.Regon);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("TRUE", true)]
    [InlineData("False", false)]
    [InlineData("2", null)]
    [InlineData("-1", null)]
    [InlineData("yes", null)]
    [InlineData("", null)]
    public void Flags_parse_0_1_true_false_and_garbage(string value, bool? expected)
    {
        var details = Details(
            RegonEntityType.NaturalPerson,
            RegonSilo.Ceidg,
            Row(("fiz_dzialalnoscCeidg", value), ("fizC_NiePodjetoDzialalnosci", value)));
        var pkd = RegonMapper.MapPkd(Row(("praw_pkdKod", "8411Z"), ("praw_pkdPrzewazajace", value)));

        Assert.Equal(expected, details.HasCeidgActivity);
        Assert.Equal(expected, details.ActivityNotStarted);
        Assert.Equal(expected == true, pkd.IsPrimary);
    }

    [Theory]
    [InlineData("19-10-2018", 2018, 10, 19)]
    [InlineData("2018-10-19", 2018, 10, 19)]
    [InlineData("19.10.2018", 2018, 10, 19)]
    [InlineData("19-10-2018 00:00:00", 2018, 10, 19)]
    [InlineData("2018-10-19 12:34:56", 2018, 10, 19)]
    [InlineData("19.10.2018 23:59:59", 2018, 10, 19)]
    [InlineData("2018-10-19T08:00:00", 2018, 10, 19)]
    [InlineData("  19-10-2018  ", 2018, 10, 19)]
    [InlineData("01-03-1999", 1999, 3, 1)]
    public void ParseDataDate_accepts_the_known_formats(string text, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), RegonMapper.ParseDataDate(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("32-10-2018")]
    [InlineData("19/10/2018")]
    [InlineData("2018-13-01")]
    public void ParseDataDate_rejects_everything_else(string? text)
    {
        Assert.Null(RegonMapper.ParseDataDate(text));
    }

    [Theory]
    [InlineData("00925", "00-925")]
    [InlineData("02672", "02-672")]
    [InlineData("00-925", "00-925")]
    [InlineData(" 00925 ", "00-925")]
    [InlineData("80001", "80-001")]
    [InlineData("0092", "0092")]
    [InlineData("009250", "009250")]
    [InlineData("ABCDE", "ABCDE")]
    [InlineData("  AB 12  ", "AB 12")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizePostalCode_cases(string? value, string? expected)
    {
        Assert.Equal(expected, RegonMapper.NormalizePostalCode(value));
    }

    [Fact]
    public void Merge_lets_later_rows_win()
    {
        var first = Row(("a", "1"), ("b", "x"));
        var second = Row(("a", "2"), ("c", "3"), ("b", string.Empty));

        var merged = RegonMapper.Merge([first, second]);

        Assert.Equal("2", merged["a"]);
        Assert.Equal("x", merged["b"]);   // an empty value never replaces a filled one
        Assert.Equal("3", merged["c"]);
        Assert.Equal("1", first["a"]);    // the inputs are left alone
        Assert.Equal(0, RegonMapper.Merge([]).Count);
        Assert.Equal("1", RegonMapper.Merge([first])["a"]);
    }

    [Fact]
    public void The_mapper_never_throws_on_odd_data()
    {
        var odd = Row(
            ("praw_regon9", "\u0000\u0001"),
            ("praw_dataPowstania", "9999-99-99"),
            ("praw_liczbaJednLokalnych", "99999999999999999999"),
            ("praw_nazwa", "<script>alert(1)</script>"),
            ("praw_adSiedzKodPocztowy", "\t"));

        var details = Details(RegonEntityType.Unknown, RegonSilo.Unknown, odd);

        Assert.Null(details.Regon);
        Assert.Null(details.FormationDate);
        Assert.Null(details.LocalUnitCount);
        Assert.Equal("<script>alert(1)</script>", details.Name);   // returned as published: untrusted text, not sanitised
        Assert.Null(details.Address.PostalCode);
    }
}
