using System;

namespace PolishOpenData.Bir;

// The report names and the REGON length each report needs: research-gus-bir/api-facts.md section 5.4 (table of the 17 reports).
internal static class RegonReports
{
    /// <summary>The report name GUS expects in <c>pNazwaRaportu</c>.</summary>
    public static string WireName(RegonReport report) => report switch
    {
        RegonReport.OsPrawna => "BIR12OsPrawna",
        RegonReport.OsPrawnaPkd => "BIR12OsPrawnaPkd",
        RegonReport.OsPrawnaListaJednLokalnych => "BIR12OsPrawnaListaJednLokalnych",
        RegonReport.OsPrawnaSpCywilnaWspolnicy => "BIR12OsPrawnaSpCywilnaWspolnicy",
        RegonReport.OsFizycznaDaneOgolne => "BIR12OsFizycznaDaneOgolne",
        RegonReport.OsFizycznaDzialalnoscCeidg => "BIR12OsFizycznaDzialalnoscCeidg",
        RegonReport.OsFizycznaDzialalnoscRolnicza => "BIR12OsFizycznaDzialalnoscRolnicza",
        RegonReport.OsFizycznaDzialalnoscPozostala => "BIR12OsFizycznaDzialalnoscPozostala",
        RegonReport.OsFizycznaDzialalnoscSkreslonaDo20141108 => "BIR12OsFizycznaDzialalnoscSkreslonaDo20141108",
        RegonReport.OsFizycznaPkd => "BIR12OsFizycznaPkd",
        RegonReport.OsFizycznaListaJednLokalnych => "BIR12OsFizycznaListaJednLokalnych",
        RegonReport.JednLokalnaOsPrawnej => "BIR12JednLokalnaOsPrawnej",
        RegonReport.JednLokalnaOsPrawnej121 => "BIR121JednLokalnaOsPrawnej",
        RegonReport.JednLokalnaOsPrawnejPkd => "BIR12JednLokalnaOsPrawnejPkd",
        RegonReport.JednLokalnaOsFizycznej => "BIR12JednLokalnaOsFizycznej",
        RegonReport.JednLokalnaOsFizycznejPkd => "BIR12JednLokalnaOsFizycznejPkd",
        RegonReport.TypPodmiotu => "BIR12TypPodmiotu",
        _ => throw new ArgumentOutOfRangeException(nameof(report), report, "Unknown REGON report."),
    };

    /// <summary>9 for the reports of an entity, 14 for the reports of a local unit, 0 when either length is accepted.</summary>
    public static int RegonLength(RegonReport report) => report switch
    {
        RegonReport.OsPrawna or RegonReport.OsPrawnaPkd or RegonReport.OsPrawnaListaJednLokalnych or RegonReport.OsPrawnaSpCywilnaWspolnicy
            or RegonReport.OsFizycznaDaneOgolne or RegonReport.OsFizycznaDzialalnoscCeidg or RegonReport.OsFizycznaDzialalnoscRolnicza
            or RegonReport.OsFizycznaDzialalnoscPozostala or RegonReport.OsFizycznaDzialalnoscSkreslonaDo20141108
            or RegonReport.OsFizycznaPkd or RegonReport.OsFizycznaListaJednLokalnych => 9,
        RegonReport.JednLokalnaOsPrawnej or RegonReport.JednLokalnaOsPrawnej121 or RegonReport.JednLokalnaOsPrawnejPkd
            or RegonReport.JednLokalnaOsFizycznej or RegonReport.JednLokalnaOsFizycznejPkd => 14,
        RegonReport.TypPodmiotu => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(report), report, "Unknown REGON report."),
    };
}
