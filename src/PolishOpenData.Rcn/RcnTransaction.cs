using System;
using System.Collections.Generic;
using PolishOpenData.Geo;

namespace PolishOpenData.Rcn;

/// <summary>
/// One row of an RCN layer. A transaction that covers several premises appears once per premises, all rows sharing
/// <see cref="Id"/> and <see cref="TotalPrice"/>: use <see cref="PremisesPrice"/> for the price of one premises.
/// </summary>
public sealed class RcnTransaction
{
    /// <summary>The layer the row came from.</summary>
    public required RcnLayer Layer { get; init; }

    /// <summary>The transaction identifier (<c>TRAN_LOKALNY_ID_IIP</c>).</summary>
    public required string Id { get; init; }

    /// <summary>The date of the deed (<c>DOK_DATA</c>).</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Primary or secondary market (<c>TRAN_RODZAJ_RYNKU</c>); null when empty or unknown.</summary>
    public RcnMarket? Market { get; init; }

    /// <summary>The kind of transaction as published (<c>TRAN_RODZAJ_TRANS</c>), e.g. <c>wolnyRynek</c>; null when empty (about 60 % of rows).</summary>
    public string? Kind { get; init; }

    /// <summary>The seller's kind, e.g. <c>osobaFizyczna</c> (<c>TRAN_SPRZEDAJACY</c>).</summary>
    public string? SellerType { get; init; }

    /// <summary>The buyer's kind, e.g. <c>osobaFizyczna</c> (<c>TRAN_KUPUJACY</c>).</summary>
    public string? BuyerType { get; init; }

    /// <summary>The gross price of the whole transaction, in zloty (<c>TRAN_CENA_BRUTTO</c>); not the price of one premises.</summary>
    public required decimal TotalPrice { get; init; }

    /// <summary>The deed number (<c>DOK_OZNACZENIE</c>), e.g. <c>11038/2025</c>.</summary>
    public string? DocumentNumber { get; init; }

    /// <summary>The right transferred as published (<c>NIER_PRAWO</c>), e.g. <c>wlasnoscLokaluWrazZPrawemZwiazanym</c>.</summary>
    public string? Right { get; init; }

    /// <summary>The share transferred, e.g. <c>1/1</c> or <c>1/64</c> (<c>NIER_UDZIAL</c>); null when empty.</summary>
    public string? Share { get; init; }

    /// <summary>The centre of the feature's bounding box in PL-1992 (the point itself for premises).</summary>
    public required Pl1992Point Location { get; init; }

    /// <summary>The address, when the row has one that could be read.</summary>
    public RcnAddress? Address { get; init; }

    /// <summary>What the premises is used for (<c>LOK_FUNKCJA</c>); null when empty or unknown.</summary>
    public RcnPremisesFunction? PremisesFunction { get; init; }

    /// <summary>The number of rooms (<c>LOK_LICZBA_IZB</c>).</summary>
    public int? Rooms { get; init; }

    /// <summary>The floor (<c>LOK_NR_KOND</c>); <c>-1</c> is below ground.</summary>
    public int? Floor { get; init; }

    /// <summary>The usable area of the premises in square metres (<c>LOK_POW_UZYT</c>).</summary>
    public decimal? UsableArea { get; init; }

    /// <summary>The gross price of this premises, in zloty (<c>LOK_CENA_BRUTTO</c>).</summary>
    public decimal? PremisesPrice { get; init; }

    /// <summary><see cref="PremisesPrice"/> divided by <see cref="UsableArea"/>; null unless both are above zero.</summary>
    public decimal? PricePerSquareMetre => PremisesPrice > 0 && UsableArea > 0 ? PremisesPrice / UsableArea : null;

    /// <summary>The parcel (<c>DZI_ID_DZIALKI</c>), for the parcel layer.</summary>
    public ParcelId? ParcelId { get; init; }

    /// <summary>The registered area of the parcel in square metres (<c>DZI_POW_EWID</c>).</summary>
    public decimal? LandArea { get; init; }

    /// <summary>The gross price of the parcel, in zloty (<c>DZI_CENA_BRUTTO</c>).</summary>
    public decimal? ParcelPrice { get; init; }

    /// <summary>The building (<c>BUD_ID_BUDYNKU</c>), for the building layer.</summary>
    public string? BuildingId { get; init; }

    /// <summary>The kind of building, e.g. <c>mieszkalny</c> (<c>BUD_RODZAJ</c>).</summary>
    public string? BuildingKind { get; init; }

    /// <summary>The usable area of the building in square metres (<c>BUD_POW_UZYT</c>).</summary>
    public decimal? BuildingUsableArea { get; init; }

    /// <summary>The gross price of the building, in zloty (<c>BUD_CENA_BRUTTO</c>).</summary>
    public decimal? BuildingPrice { get; init; }

    /// <summary>Every non-empty property of the row except the geometry, keyed by the upstream upper-case name (the notary's name <c>DOK_TWORCA</c> is here and nowhere else).</summary>
    public required IReadOnlyDictionary<string, string> Fields { get; init; }
}
