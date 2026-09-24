using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable CS1591 // wire model: properties mirror the KRS JSON field names one to one

namespace PolishOpenData.Krs;

/// <summary>Section 3: business activities (PKD), filed documents, fiscal year.</summary>
public sealed class KrsDzial3 : KrsNode
{
    public KrsPrzedmiotDzialalnosci? PrzedmiotDzialalnosci { get; set; }

    /// <summary>Activities of a public-benefit organisation (register S).</summary>
    public KrsPrzedmiotDzialalnosciOpp? PrzedmiotDzialalnosciOpp { get; set; }

    public JsonElement? WzmiankiOZlozonychDokumentach { get; set; }

    public JsonElement? SprawozdaniaGrupyKapitalowej { get; set; }

    public JsonElement? InformacjaODniuKonczacymRokObrotowy { get; set; }

    public JsonElement? CelDzialaniaOrganizacji { get; set; }
}

/// <summary>Main and other activities (register P).</summary>
public sealed class KrsPrzedmiotDzialalnosci : KrsNode
{
    public IReadOnlyList<KrsPkd>? PrzedmiotPrzewazajacejDzialalnosci { get; set; }

    public IReadOnlyList<KrsPkd>? PrzedmiotPozostalejDzialalnosci { get; set; }
}

/// <summary>Paid and unpaid public-benefit activities (register S).</summary>
public sealed class KrsPrzedmiotDzialalnosciOpp : KrsNode
{
    public IReadOnlyList<KrsPkd>? OdplatnyPkd { get; set; }

    public IReadOnlyList<KrsPkd>? NieodplatnyPkd { get; set; }
}

/// <summary>One PKD (Polish classification of activities) entry.</summary>
public sealed class KrsPkd : KrsNode
{
    public string? Opis { get; set; }

    public string? KodDzial { get; set; }

    public string? KodKlasa { get; set; }

    public string? KodPodklasa { get; set; }

    /// <summary>The PKD code in the usual form, e.g. <c>19.20.Z</c>.</summary>
    [JsonIgnore]
    public string Code => string.Join(".", new[] { KodDzial, KodKlasa, KodPodklasa }.Where(p => !string.IsNullOrEmpty(p)));
}
