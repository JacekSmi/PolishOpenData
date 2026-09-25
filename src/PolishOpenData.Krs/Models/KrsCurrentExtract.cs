using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolishOpenData.Krs.Serialization;

#pragma warning disable CS1591 // wire model: properties mirror the KRS JSON field names one to one

namespace PolishOpenData.Krs;

/// <summary>Current extract (<i>odpis aktualny</i>) — the <c>odpis</c> object of OdpisAktualny.</summary>
public sealed class KrsCurrentExtract : KrsNode
{
    public string? Rodzaj { get; set; }

    public KrsCurrentHeader? NaglowekA { get; set; }

    public KrsCurrentData? Dane { get; set; }
}

/// <summary>Header of a current extract (<c>naglowekA</c>).</summary>
public sealed class KrsCurrentHeader : KrsNode
{
    /// <summary><c>RejP</c> or <c>RejS</c>.</summary>
    public string? Rejestr { get; set; }

    public string? NumerKrs { get; set; }

    /// <summary>When the extract was generated (Warsaw time, exposed with its offset).</summary>
    [JsonConverter(typeof(KrsTimestampJsonConverter))]
    public DateTimeOffset? DataCzasOdpisu { get; set; }

    [JsonConverter(typeof(KrsDateJsonConverter))]
    public DateOnly? StanZDnia { get; set; }

    [JsonConverter(typeof(KrsDateJsonConverter))]
    public DateOnly? DataRejestracjiWKrs { get; set; }

    public int? NumerOstatniegoWpisu { get; set; }

    [JsonConverter(typeof(KrsDateJsonConverter))]
    public DateOnly? DataOstatniegoWpisu { get; set; }

    public string? SygnaturaAktSprawyDotyczacejOstatniegoWpisu { get; set; }

    public string? OznaczenieSaduDokonujacegoOstatniegoWpisu { get; set; }

    public int? StanPozycji { get; set; }
}

/// <summary>Sections 1–6 of a current extract (<c>dane</c>).</summary>
public sealed class KrsCurrentData : KrsNode
{
    public KrsDzial1? Dzial1 { get; set; }

    public KrsDzial2? Dzial2 { get; set; }

    public KrsDzial3? Dzial3 { get; set; }

    public JsonElement? Dzial4 { get; set; }

    public JsonElement? Dzial5 { get; set; }

    public JsonElement? Dzial6 { get; set; }
}
