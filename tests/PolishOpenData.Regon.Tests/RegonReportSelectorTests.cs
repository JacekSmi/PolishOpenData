using System;

namespace PolishOpenData.Bir.Tests;

public class RegonReportSelectorTests
{
    private static RegonEntity Entity(string? regon, RegonEntityType type = RegonEntityType.LegalPerson) =>
        new() { Regon = regon is null ? null : Regon.Parse(regon), Type = type };

    [Fact]
    public void Legal_person_uses_osprawna()
    {
        foreach (var silo in new[] { RegonSilo.LegalPerson, RegonSilo.Ceidg, RegonSilo.Unknown })
        {
            Assert.Equal([RegonReport.OsPrawna], RegonReportSelector.CoreReports(RegonEntityType.LegalPerson, silo));
        }
    }

    [Theory]
    [InlineData(RegonSilo.Ceidg, RegonReport.OsFizycznaDzialalnoscCeidg)]
    [InlineData(RegonSilo.Agricultural, RegonReport.OsFizycznaDzialalnoscRolnicza)]
    [InlineData(RegonSilo.Other, RegonReport.OsFizycznaDzialalnoscPozostala)]
    [InlineData(RegonSilo.RemovedBefore2014, RegonReport.OsFizycznaDzialalnoscSkreslonaDo20141108)]
    public void Natural_person_uses_general_and_activity_report_per_silo(RegonSilo silo, RegonReport activity)
    {
        Assert.Equal([RegonReport.OsFizycznaDaneOgolne, activity], RegonReportSelector.CoreReports(RegonEntityType.NaturalPerson, silo));
    }

    [Theory]
    [InlineData(RegonSilo.Unknown)]
    [InlineData(RegonSilo.LegalPerson)]
    public void Natural_person_with_an_unusable_silo_has_no_reports(RegonSilo silo)
    {
        Assert.Empty(RegonReportSelector.CoreReports(RegonEntityType.NaturalPerson, silo));
    }

    [Fact]
    public void Local_units_use_their_reports()
    {
        Assert.Equal([RegonReport.JednLokalnaOsPrawnej121], RegonReportSelector.CoreReports(RegonEntityType.LegalPersonLocalUnit, RegonSilo.LegalPerson));
        Assert.Equal([RegonReport.JednLokalnaOsFizycznej], RegonReportSelector.CoreReports(RegonEntityType.NaturalPersonLocalUnit, RegonSilo.Ceidg));
    }

    [Fact]
    public void Unknown_type_has_no_reports()
    {
        Assert.Empty(RegonReportSelector.CoreReports(RegonEntityType.Unknown, RegonSilo.Ceidg));
        Assert.Null(RegonReportSelector.PkdReport(RegonEntityType.Unknown));
        Assert.Null(RegonReportSelector.LocalUnitsReport(RegonEntityType.Unknown));
        Assert.Null(RegonReportSelector.PartnersReport(RegonEntityType.Unknown));
    }

    [Fact]
    public void Pkd_local_unit_and_partner_reports_by_type()
    {
        Assert.Equal(RegonReport.OsPrawnaPkd, RegonReportSelector.PkdReport(RegonEntityType.LegalPerson));
        Assert.Equal(RegonReport.OsFizycznaPkd, RegonReportSelector.PkdReport(RegonEntityType.NaturalPerson));
        Assert.Equal(RegonReport.JednLokalnaOsPrawnejPkd, RegonReportSelector.PkdReport(RegonEntityType.LegalPersonLocalUnit));
        Assert.Equal(RegonReport.JednLokalnaOsFizycznejPkd, RegonReportSelector.PkdReport(RegonEntityType.NaturalPersonLocalUnit));

        Assert.Equal(RegonReport.OsPrawnaListaJednLokalnych, RegonReportSelector.LocalUnitsReport(RegonEntityType.LegalPerson));
        Assert.Equal(RegonReport.OsFizycznaListaJednLokalnych, RegonReportSelector.LocalUnitsReport(RegonEntityType.NaturalPerson));
        Assert.Null(RegonReportSelector.LocalUnitsReport(RegonEntityType.LegalPersonLocalUnit));
        Assert.Null(RegonReportSelector.LocalUnitsReport(RegonEntityType.NaturalPersonLocalUnit));

        Assert.Equal(RegonReport.OsPrawnaSpCywilnaWspolnicy, RegonReportSelector.PartnersReport(RegonEntityType.LegalPerson));
        Assert.Null(RegonReportSelector.PartnersReport(RegonEntityType.NaturalPerson));
        Assert.Null(RegonReportSelector.PartnersReport(RegonEntityType.LegalPersonLocalUnit));
        Assert.Null(RegonReportSelector.PartnersReport(RegonEntityType.NaturalPersonLocalUnit));
    }

    [Fact]
    public void Bir121_falls_back_to_bir12()
    {
        Assert.Equal(RegonReport.JednLokalnaOsPrawnej, RegonReportSelector.Fallback(RegonReport.JednLokalnaOsPrawnej121));

        // The members are numbered 0 to 16 in declaration order (TypPodmiotu is the last one).
        for (var value = 0; value <= (int)RegonReport.TypPodmiotu; value++)
        {
            var report = (RegonReport)value;
            if (report != RegonReport.JednLokalnaOsPrawnej121)
            {
                Assert.Null(RegonReportSelector.Fallback(report));
            }
        }
    }

    [Fact]
    public void RegonFor_returns_9_or_14_digits_or_throws()
    {
        var localUnit = Entity("14726216300027", RegonEntityType.LegalPersonLocalUnit);
        var legalPerson = Entity("147262163");

        Assert.Equal("147262163", RegonReportSelector.RegonFor(legalPerson, RegonReport.OsPrawna));
        Assert.Equal("147262163", RegonReportSelector.RegonFor(localUnit, RegonReport.OsPrawna));   // the base number of a local unit
        Assert.Equal("14726216300027", RegonReportSelector.RegonFor(localUnit, RegonReport.JednLokalnaOsPrawnej121));
        Assert.Equal("14726216300027", RegonReportSelector.RegonFor(localUnit, RegonReport.JednLokalnaOsPrawnejPkd));
        Assert.Equal("14726216300027", RegonReportSelector.RegonFor(localUnit, RegonReport.TypPodmiotu));   // either length
        Assert.Equal("147262163", RegonReportSelector.RegonFor(legalPerson, RegonReport.TypPodmiotu));

        Assert.Throws<ArgumentException>(() => RegonReportSelector.RegonFor(legalPerson, RegonReport.JednLokalnaOsPrawnej));
        Assert.Throws<ArgumentException>(() => RegonReportSelector.RegonFor(Entity(null), RegonReport.OsPrawna));
        Assert.Throws<ArgumentNullException>(() => RegonReportSelector.RegonFor(null!, RegonReport.OsPrawna));
    }
}
