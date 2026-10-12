using System;

namespace PolishOpenData.Bir;

/// <summary>The kind of entity a REGON row describes (<c>Typ</c> in the data of GUS).</summary>
public enum RegonEntityType
{
    /// <summary>GUS sent a kind this library does not know, or none.</summary>
    Unknown = 0,

    /// <summary><c>P</c>: a legal person or an organisational unit without legal personality (companies, foundations, public bodies, civil partnerships).</summary>
    LegalPerson = 1,

    /// <summary><c>F</c>: a natural person who runs an activity (a sole trader, a farmer, a notary and so on).</summary>
    NaturalPerson = 2,

    /// <summary><c>LP</c>: a local unit (branch, plant) of a legal person; it has a 14-digit REGON.</summary>
    LegalPersonLocalUnit = 3,

    /// <summary><c>LF</c>: a local unit of a natural person; it has a 14-digit REGON.</summary>
    NaturalPersonLocalUnit = 4,
}

/// <summary>
/// The register part (<c>SilosID</c> in the data of GUS) a row belongs to. A natural person has one row for each part
/// in which they are active, and each row has its own address.
/// </summary>
public enum RegonSilo
{
    /// <summary>GUS sent a value this library does not know, or none.</summary>
    Unknown = 0,

    /// <summary><c>1</c>: business activity registered in CEIDG.</summary>
    Ceidg = 1,

    /// <summary><c>2</c>: agricultural activity.</summary>
    Agricultural = 2,

    /// <summary><c>3</c>: other activity (for example a bailiff, a notary or agritourism that is not in CEIDG).</summary>
    Other = 3,

    /// <summary><c>4</c>: activity removed from REGON before 2014-11-08.</summary>
    RemovedBefore2014 = 4,

    /// <summary><c>6</c>: a legal person or a local unit of one.</summary>
    LegalPerson = 6,
}

/// <summary>Optional extra parts to fetch with the details of an entity. Each part costs one more request.</summary>
[Flags]
public enum RegonInclude
{
    /// <summary>Only the core report.</summary>
    None = 0,

    /// <summary>The PKD activity codes.</summary>
    Pkd = 1,

    /// <summary>The list of local units (legal persons and natural persons only).</summary>
    LocalUnits = 2,

    /// <summary>The partners of a civil partnership (legal persons only).</summary>
    Partners = 4,

    /// <summary>Everything above.</summary>
    All = 7,
}

/// <summary>
/// The full reports of the BIR1.2 service. The wire name is <c>BIR12</c> followed by the member name, except for
/// <see cref="JednLokalnaOsPrawnej121"/>. A report exists for one kind of entity only; asking for the wrong one makes
/// GUS answer "not found".
/// </summary>
public enum RegonReport
{
    /// <summary><c>BIR12OsPrawna</c>: a legal person (9-digit REGON).</summary>
    OsPrawna,

    /// <summary><c>BIR12OsPrawnaPkd</c>: PKD codes of a legal person (9 digits).</summary>
    OsPrawnaPkd,

    /// <summary><c>BIR12OsPrawnaListaJednLokalnych</c>: local units of a legal person (9 digits).</summary>
    OsPrawnaListaJednLokalnych,

    /// <summary><c>BIR12OsPrawnaSpCywilnaWspolnicy</c>: partners of a civil partnership (9 digits).</summary>
    OsPrawnaSpCywilnaWspolnicy,

    /// <summary><c>BIR12OsFizycznaDaneOgolne</c>: general data of a natural person (9 digits).</summary>
    OsFizycznaDaneOgolne,

    /// <summary><c>BIR12OsFizycznaDzialalnoscCeidg</c>: the CEIDG activity of a natural person (9 digits, silo 1).</summary>
    OsFizycznaDzialalnoscCeidg,

    /// <summary><c>BIR12OsFizycznaDzialalnoscRolnicza</c>: the agricultural activity of a natural person (9 digits, silo 2).</summary>
    OsFizycznaDzialalnoscRolnicza,

    /// <summary><c>BIR12OsFizycznaDzialalnoscPozostala</c>: other activity of a natural person (9 digits, silo 3).</summary>
    OsFizycznaDzialalnoscPozostala,

    /// <summary><c>BIR12OsFizycznaDzialalnoscSkreslonaDo20141108</c>: activity of a natural person removed before 2014-11-08 (9 digits, silo 4).</summary>
    OsFizycznaDzialalnoscSkreslonaDo20141108,

    /// <summary><c>BIR12OsFizycznaPkd</c>: PKD codes of a natural person (9 digits).</summary>
    OsFizycznaPkd,

    /// <summary><c>BIR12OsFizycznaListaJednLokalnych</c>: local units of a natural person (9 digits).</summary>
    OsFizycznaListaJednLokalnych,

    /// <summary><c>BIR12JednLokalnaOsPrawnej</c>: a local unit of a legal person (14 digits).</summary>
    JednLokalnaOsPrawnej,

    /// <summary><c>BIR121JednLokalnaOsPrawnej</c>: as <see cref="JednLokalnaOsPrawnej"/> plus the NIP of the unit (14 digits; GUS offers it since January 2026).</summary>
    JednLokalnaOsPrawnej121,

    /// <summary><c>BIR12JednLokalnaOsPrawnejPkd</c>: PKD codes of a local unit of a legal person (14 digits).</summary>
    JednLokalnaOsPrawnejPkd,

    /// <summary><c>BIR12JednLokalnaOsFizycznej</c>: a local unit of a natural person (14 digits).</summary>
    JednLokalnaOsFizycznej,

    /// <summary><c>BIR12JednLokalnaOsFizycznejPkd</c>: PKD codes of a local unit of a natural person (14 digits).</summary>
    JednLokalnaOsFizycznejPkd,

    /// <summary><c>BIR12TypPodmiotu</c>: only the kind of the entity (9 or 14 digits).</summary>
    TypPodmiotu,
}
