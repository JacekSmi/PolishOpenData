using System;
using System.Linq;
using PolishOpenData.Krs;
using PolishOpenData.Tests.Shared;

namespace PolishOpenData.Krs.Tests;

public class KrsModelTests
{
    private static KrsCurrentExtract Current(string file) => KrsJson.DeserializeCurrent(Fixture.Read("krs/" + file))!;

    private static KrsFullExtract Full(string file) => KrsJson.DeserializeFull(Fixture.Read("krs/" + file))!;

    [Theory]
    [InlineData("current-P-0000028860-orlen.json")]
    [InlineData("current-P-0001268296.json")]
    [InlineData("current-S-0000030897-wosp.json")]
    public void Models_cover_every_field_of_the_fixtures(string file)
    {
        Assert.Empty(UnknownFields.Find(Current(file)));
    }

    [Fact]
    public void Orlen_header_and_sections()
    {
        var extract = Current("current-P-0000028860-orlen.json");
        var header = extract.NaglowekA!;
        Assert.Equal("Aktualny", extract.Rodzaj);
        Assert.Equal("RejP", header.Rejestr);
        Assert.Equal("0000028860", header.NumerKrs);
        Assert.Equal(new DateOnly(2026, 9, 17), header.StanZDnia);
        Assert.Equal(new DateOnly(2001, 7, 19), header.DataRejestracjiWKrs);
        Assert.Equal(220, header.NumerOstatniegoWpisu);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 32, 16, TimeSpan.Zero), header.DataCzasOdpisu!.Value.ToUniversalTime());

        var dzial1 = extract.Dane!.Dzial1!;
        Assert.Equal("ORLEN SPÓŁKA AKCYJNA", dzial1.DanePodmiotu!.Nazwa);
        Assert.Equal("61018820100000", dzial1.DanePodmiotu.Identyfikatory!.Regon);
        Assert.Equal("1451177561,25", dzial1.Kapital!.WysokoscKapitaluZakladowego!.Wartosc);
        Assert.True(dzial1.EmisjeAkcji.HasValue);

        var dzial2 = extract.Dane.Dzial2!;
        Assert.Equal("ZARZĄD", dzial2.Reprezentacja!.NazwaOrganu);
        Assert.Equal(9, dzial2.Reprezentacja.Sklad!.Count);
        Assert.Equal("PREZES ZARZĄDU", dzial2.Reprezentacja.Sklad[0].FunkcjaWOrganie);
        var supervisory = Assert.Single(dzial2.OrganNadzoru!);
        Assert.Equal("RADA NADZORCZA", supervisory.Nazwa);
        Assert.Equal(10, supervisory.Sklad!.Count);
        Assert.Equal(5, dzial2.Prokurenci!.Count);

        var main = Assert.Single(extract.Dane.Dzial3!.PrzedmiotDzialalnosci!.PrzedmiotPrzewazajacejDzialalnosci!);
        Assert.Equal("19.20.Z", main.Code);
    }

    [Fact]
    public void Small_company_has_shareholders()
    {
        var dzial1 = Current("current-P-0001268296.json").Dane!.Dzial1!;
        Assert.Equal(2, dzial1.WspolnicySpzoo!.Count);
        Assert.Equal("MAJKA GROUP SPÓŁKA Z OGRANICZONĄ ODPOWIEDZIALNOŚCIĄ", dzial1.WspolnicySpzoo[0].Nazwa);
        Assert.Equal("0001223769", dzial1.WspolnicySpzoo[0].Krs!.Krs);
        Assert.Equal("C*****", dzial1.WspolnicySpzoo[1].Nazwisko!.NazwiskoICzlon);
        Assert.Equal("10000,00", dzial1.Kapital!.WysokoscKapitaluZakladowego!.Wartosc);
    }

    [Fact]
    public void Foundation_in_register_s()
    {
        var extract = Current("current-S-0000030897-wosp.json");
        Assert.Equal("RejS", extract.NaglowekA!.Rejestr);
        Assert.Null(extract.Dane!.Dzial1!.Kapital);
        Assert.True(extract.Dane.Dzial1.OrganSprawujacyNadzor.HasValue);
        Assert.True(extract.Dane.Dzial1.DanePodmiotu!.CzyPosiadaStatusOpp);
        Assert.Equal(16, extract.Dane.Dzial3!.PrzedmiotDzialalnosciOpp!.NieodplatnyPkd!.Count);
    }

    [Fact]
    public void Full_extract_of_active_company()
    {
        var extract = Full("full-P-0001268296.json");
        Assert.Equal("Pełny", extract.Rodzaj);
        Assert.Equal(3, extract.NaglowekP!.Wpis!.Count);
        Assert.Equal(new DateOnly(2026, 9, 22), extract.RegisteredOn);
        Assert.False(extract.IsRemoved);
        Assert.Null(extract.RemovedOn);
        Assert.True(extract.Dane.HasValue);
        Assert.Empty(UnknownFields.Find(extract));
    }

    [Fact]
    public void Full_extract_of_removed_company()
    {
        var extract = Full("full-P-0000106150-removed-trimmed.json");
        Assert.Equal(148, extract.NaglowekP!.Wpis!.Count);
        Assert.True(extract.IsRemoved);
        Assert.Equal(new DateOnly(2022, 8, 12), extract.RemovedOn);
        Assert.Equal(new DateOnly(2022, 9, 2), extract.NaglowekP.Wpis[extract.NaglowekP.Wpis.Count - 1].DataUprawomocnienia);
    }

    [Fact]
    public void Unknown_fields_are_captured_not_rejected()
    {
        var extract = KrsJson.DeserializeCurrent("""{"odpis":{"rodzaj":"Aktualny","naglowekA":{"numerKRS":"0000000001","nowePole":1}}}""")!;
        Assert.Equal(new[] { "$.NaglowekA.nowePole" }, UnknownFields.Find(extract));
    }
}
