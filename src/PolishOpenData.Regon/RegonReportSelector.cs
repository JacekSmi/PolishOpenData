using System;
using System.Collections.Generic;

namespace PolishOpenData.Bir;

/// <summary>Chooses which BIR1.2 full reports describe an entity, from the <c>Typ</c> and <c>SilosID</c> of its search row.</summary>
// The table of reports is research-gus-bir/api-facts.md 5.4: a wrong choice (a legal-person report for a sole trader) answers ErrorCode 4.
internal static class RegonReportSelector
{
    private static readonly RegonReport[] None = [];
    private static readonly RegonReport[] LegalPersonCore = [RegonReport.OsPrawna];
    private static readonly RegonReport[] CeidgCore = [RegonReport.OsFizycznaDaneOgolne, RegonReport.OsFizycznaDzialalnoscCeidg];
    private static readonly RegonReport[] AgriculturalCore = [RegonReport.OsFizycznaDaneOgolne, RegonReport.OsFizycznaDzialalnoscRolnicza];
    private static readonly RegonReport[] OtherCore = [RegonReport.OsFizycznaDaneOgolne, RegonReport.OsFizycznaDzialalnoscPozostala];
    private static readonly RegonReport[] RemovedCore = [RegonReport.OsFizycznaDaneOgolne, RegonReport.OsFizycznaDzialalnoscSkreslonaDo20141108];
    private static readonly RegonReport[] LegalPersonLocalUnitCore = [RegonReport.JednLokalnaOsPrawnej121];
    private static readonly RegonReport[] NaturalPersonLocalUnitCore = [RegonReport.JednLokalnaOsFizycznej];

    /// <summary>The reports whose rows are merged into one <see cref="RegonDetails"/>; empty when the type or silo has none.</summary>
    public static IReadOnlyList<RegonReport> CoreReports(RegonEntityType type, RegonSilo silo)
    {
        switch (type)
        {
            case RegonEntityType.LegalPerson:
                return LegalPersonCore;
            case RegonEntityType.NaturalPerson:
                return silo switch
                {
                    RegonSilo.Ceidg => CeidgCore,
                    RegonSilo.Agricultural => AgriculturalCore,
                    RegonSilo.Other => OtherCore,
                    RegonSilo.RemovedBefore2014 => RemovedCore,
                    _ => None,
                };
            case RegonEntityType.LegalPersonLocalUnit:
                return LegalPersonLocalUnitCore;
            case RegonEntityType.NaturalPersonLocalUnit:
                return NaturalPersonLocalUnitCore;
            default:
                return None;
        }
    }

    /// <summary>The PKD report of the type, or null.</summary>
    public static RegonReport? PkdReport(RegonEntityType type) => type switch
    {
        RegonEntityType.LegalPerson => RegonReport.OsPrawnaPkd,
        RegonEntityType.NaturalPerson => RegonReport.OsFizycznaPkd,
        RegonEntityType.LegalPersonLocalUnit => RegonReport.JednLokalnaOsPrawnejPkd,
        RegonEntityType.NaturalPersonLocalUnit => RegonReport.JednLokalnaOsFizycznejPkd,
        _ => null,
    };

    /// <summary>The report that lists the local units of an entity, or null (a local unit has none).</summary>
    public static RegonReport? LocalUnitsReport(RegonEntityType type) => type switch
    {
        RegonEntityType.LegalPerson => RegonReport.OsPrawnaListaJednLokalnych,
        RegonEntityType.NaturalPerson => RegonReport.OsFizycznaListaJednLokalnych,
        _ => null,
    };

    /// <summary>The report that lists the partners of a civil partnership, or null (only legal persons have it).</summary>
    public static RegonReport? PartnersReport(RegonEntityType type) =>
        type == RegonEntityType.LegalPerson ? RegonReport.OsPrawnaSpCywilnaWspolnicy : null;

    /// <summary>The older report to use when GUS does not know <paramref name="report"/> (BIR121 appeared in 2026-01), or null.</summary>
    public static RegonReport? Fallback(RegonReport report) =>
        report == RegonReport.JednLokalnaOsPrawnej121 ? RegonReport.JednLokalnaOsPrawnej : null;

    /// <summary>The REGON text to send for <paramref name="report"/>: the 9-digit base number or the 14-digit number of a local unit.</summary>
    /// <exception cref="ArgumentException">The entity has no REGON, or has only 9 digits while the report needs 14.</exception>
    public static string RegonFor(RegonEntity entity, RegonReport report)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Regon is not { } regon)
        {
            throw new ArgumentException("The entity has no REGON.", nameof(entity));
        }

        switch (RegonReports.RegonLength(report))
        {
            case 9:
                return regon.BaseRegon.ToString();
            case 14:
                if (!regon.IsLocalUnit)
                {
                    throw new ArgumentException("The report " + RegonReports.WireName(report) + " needs the 14-digit REGON of a local unit.", nameof(entity));
                }

                return regon.ToString();
            default:
                return regon.ToString();
        }
    }
}
