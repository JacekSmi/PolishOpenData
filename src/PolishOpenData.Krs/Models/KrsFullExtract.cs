using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolishOpenData.Internal;

#pragma warning disable CS1591 // wire model: properties mirror the KRS JSON field names one to one

namespace PolishOpenData.Krs;

/// <summary>
/// Full extract (<i>odpis pełny</i>). Only the header (entry history) is typed; <see cref="Dane"/> stays raw JSON
/// because the full extract wraps every value in versioned arrays.
/// </summary>
public sealed class KrsFullExtract : KrsNode
{
    /// <summary>Description of the entry that removes an entity from the register.</summary>
    public const string RemovalEntryDescription = "WYKREŚLENIE Z KRAJOWEGO REJESTRU SĄDOWEGO";

    public string? Rodzaj { get; set; }

    public KrsFullHeader? NaglowekP { get; set; }

    /// <summary>Sections 1–6 with version history, as raw JSON.</summary>
    public JsonElement? Dane { get; set; }

    /// <summary>True when the last entry removed the entity from KRS.</summary>
    [JsonIgnore]
    public bool IsRemoved => LastEntry is { Opis: RemovalEntryDescription };

    /// <summary>Date of the removal entry, when <see cref="IsRemoved"/>.</summary>
    [JsonIgnore]
    public DateOnly? RemovedOn => IsRemoved ? LastEntry!.DataWpisu : null;

    /// <summary>Date of the first entry (registration in KRS).</summary>
    [JsonIgnore]
    public DateOnly? RegisteredOn => NaglowekP?.Wpis is { Count: > 0 } entries ? entries[0].DataWpisu : null;

    private KrsWpis? LastEntry => NaglowekP?.Wpis is { Count: > 0 } entries ? entries[entries.Count - 1] : null;
}

/// <summary>Header of a full extract (<c>naglowekP</c>).</summary>
public sealed class KrsFullHeader : KrsNode
{
    public string? Rejestr { get; set; }

    public string? NumerKrs { get; set; }

    [JsonConverter(typeof(DottedWarsawDateTimeConverter))]
    public DateTimeOffset? DataCzasOdpisu { get; set; }

    [JsonConverter(typeof(DottedDateOnlyConverter))]
    public DateOnly? StanZDnia { get; set; }

    public IReadOnlyList<KrsWpis>? Wpis { get; set; }

    public int? StanPozycji { get; set; }
}

/// <summary>One register entry (<c>wpis</c>).</summary>
public sealed class KrsWpis : KrsNode
{
    public int? NumerWpisu { get; set; }

    public string? Opis { get; set; }

    [JsonConverter(typeof(DottedDateOnlyConverter))]
    public DateOnly? DataWpisu { get; set; }

    public string? SygnaturaAktSprawyDotyczacejWpisu { get; set; }

    public string? OznaczenieSaduDokonujacegoWpisu { get; set; }

    [JsonConverter(typeof(DottedDateOnlyConverter))]
    public DateOnly? DataUprawomocnienia { get; set; }
}
