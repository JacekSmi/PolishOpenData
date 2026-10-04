using System;
using System.Collections.Generic;

namespace PolishOpenData.Bir;

/// <summary>
/// An address as the REGON register holds it. Every value is <c>null</c> when GUS sent nothing for it. Codes are
/// strings because they keep their leading zeros. A search row carries names only; the codes are filled for the
/// addresses of a full report.
/// </summary>
public sealed class RegonAddress
{
    /// <summary>Country code (<c>adSiedzKraj_Symbol</c>), for example <c>PL</c>.</summary>
    public string? CountryCode { get; init; }

    /// <summary>TERYT code of the voivodeship (<c>adSiedzWojewodztwo_Symbol</c>).</summary>
    public string? VoivodeshipCode { get; init; }

    /// <summary>TERYT code of the county (<c>adSiedzPowiat_Symbol</c>).</summary>
    public string? CountyCode { get; init; }

    /// <summary>TERYT code of the commune (<c>adSiedzGmina_Symbol</c>).</summary>
    public string? CommuneCode { get; init; }

    /// <summary>SIMC code of the city or village (<c>adSiedzMiejscowosc_Symbol</c>).</summary>
    public string? CityCode { get; init; }

    /// <summary>SIMC code of the post town (<c>adSiedzMiejscowoscPoczty_Symbol</c>).</summary>
    public string? PostTownCode { get; init; }

    /// <summary>ULIC code of the street (<c>adSiedzUlica_Symbol</c>).</summary>
    public string? StreetCode { get; init; }

    /// <summary>Name of the voivodeship.</summary>
    public string? Voivodeship { get; init; }

    /// <summary>Name of the county.</summary>
    public string? County { get; init; }

    /// <summary>Name of the commune.</summary>
    public string? Commune { get; init; }

    /// <summary>Name of the city or village.</summary>
    public string? City { get; init; }

    /// <summary>Name of the post town. GUS leaves it out more and more often.</summary>
    public string? PostTown { get; init; }

    /// <summary>Street name, with its prefix as GUS sends it (<c>ul.</c>). <c>null</c> when the address has no street.</summary>
    public string? Street { get; init; }

    /// <summary>Postal code, always as <c>NN-NNN</c> when it has five digits (search rows come with the dash, reports without).</summary>
    public string? PostalCode { get; init; }

    /// <summary>Building number.</summary>
    public string? BuildingNumber { get; init; }

    /// <summary>Apartment number.</summary>
    public string? ApartmentNumber { get; init; }
}

/// <summary>One row of a REGON search (<c>DaneSzukajPodmioty</c>): who the entity is and its registered address.</summary>
public sealed class RegonEntity
{
    /// <summary>The REGON (9 or 14 digits); <c>null</c> when GUS sent none or one that is not a valid REGON.</summary>
    public Regon? Regon { get; init; }

    /// <summary>The NIP; <c>null</c> when GUS sent none or one that is not a valid NIP.</summary>
    public Nip? Nip { get; init; }

    /// <summary><c>null</c> when the NIP is valid; otherwise <c>Uchylony</c> or <c>Unieważniony</c> as GUS sent it.</summary>
    public string? StatusNip { get; init; }

    /// <summary>
    /// The name. For a sole trader it is the name of the person. GUS warns that names can contain HTML or SQL-like text;
    /// the library returns the text as published and does not sanitise it, so treat it as untrusted.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The registered address. A search row has names only, no TERYT codes.</summary>
    public RegonAddress Address { get; init; } = new();

    /// <summary>The kind of entity.</summary>
    public RegonEntityType Type { get; init; }

    /// <summary>The register part this row belongs to.</summary>
    public RegonSilo Silo { get; init; }

    /// <summary>The date the activity ended; <c>null</c> when it has not.</summary>
    public DateOnly? EndDate { get; init; }
}

/// <summary>The answer of a batch search for one requested identifier.</summary>
public sealed class RegonBatchEntry
{
    internal RegonBatchEntry(string identifier, IReadOnlyList<RegonEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(entities);
        Identifier = identifier;
        Entities = entities;
    }

    /// <summary>The requested NIP, REGON or KRS number, as digits.</summary>
    public string Identifier { get; }

    /// <summary>The rows GUS answered for it; empty when GUS did not answer for it (GUS silently drops unknown identifiers).</summary>
    public IReadOnlyList<RegonEntity> Entities { get; }
}
