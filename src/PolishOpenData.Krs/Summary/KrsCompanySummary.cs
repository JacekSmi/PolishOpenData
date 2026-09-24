using System;
using System.Collections.Generic;

namespace PolishOpenData.Krs;

/// <summary>An English, flattened view of a current KRS extract — what most callers need.</summary>
public sealed record KrsCompanySummary
{
    /// <summary>KRS number.</summary>
    public KrsNumber Krs { get; init; }

    /// <summary>Register (P or S).</summary>
    public KrsRegister Register { get; init; }

    /// <summary>Registered name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Legal form as written in KRS, e.g. <c>SPÓŁKA AKCYJNA</c>.</summary>
    public string? LegalForm { get; init; }

    /// <summary>Tax number, when present and valid.</summary>
    public Nip? Nip { get; init; }

    /// <summary>9-digit REGON (normalised from KRS's padded 14-digit form).</summary>
    public Regon? Regon { get; init; }

    /// <summary>Postal address, e.g. <c>UL. OSTATNIA 1D, 31-444 KRAKÓW</c>.</summary>
    public string? Address { get; init; }

    /// <summary>Seat locality.</summary>
    public string? Locality { get; init; }

    /// <summary>Seat voivodeship.</summary>
    public string? Voivodeship { get; init; }

    /// <summary>Registered e-mail address.</summary>
    public string? Email { get; init; }

    /// <summary>Registered website.</summary>
    public string? Website { get; init; }

    /// <summary>Address for electronic delivery (e-Doręczenia, BAE).</summary>
    public string? EDeliveryAddress { get; init; }

    /// <summary>Share capital amount.</summary>
    public decimal? ShareCapital { get; init; }

    /// <summary>Share capital currency.</summary>
    public string? ShareCapitalCurrency { get; init; }

    /// <summary>Date of registration in KRS.</summary>
    public DateOnly? RegisteredOn { get; init; }

    /// <summary>Number of the latest entry.</summary>
    public int? LastEntryNumber { get; init; }

    /// <summary>Date of the latest entry.</summary>
    public DateOnly? LastEntryOn { get; init; }

    /// <summary>Date the extract's data is valid for.</summary>
    public DateOnly? StateAsOf { get; init; }

    /// <summary>When KRS generated the extract.</summary>
    public DateTimeOffset? ExtractedAt { get; init; }

    /// <summary>Public-benefit organisation status (OPP).</summary>
    public bool? IsPublicBenefitOrganization { get; init; }

    /// <summary>The body that represents the entity (e.g. the management board).</summary>
    public KrsBody? Representation { get; init; }

    /// <summary>Supervisory bodies.</summary>
    public IReadOnlyList<KrsBody> SupervisoryBodies { get; init; } = [];

    /// <summary>Proxies (<i>prokurenci</i>), masked names only.</summary>
    public IReadOnlyList<KrsPersonSummary> Proxies { get; init; } = [];

    /// <summary>Shareholders of a sp. z o.o.</summary>
    public IReadOnlyList<KrsShareholderSummary> Shareholders { get; init; } = [];

    /// <summary>Main PKD activity (register P).</summary>
    public KrsActivity? MainActivity { get; init; }

    /// <summary>Other PKD activities (register S: public-benefit activities).</summary>
    public IReadOnlyList<KrsActivity> OtherActivities { get; init; } = [];
}

/// <summary>A body such as a management or supervisory board.</summary>
/// <param name="Name">Name of the body, e.g. <c>ZARZĄD</c>.</param>
/// <param name="RepresentationMethod">How the entity is represented (free text), for the representing body.</param>
/// <param name="Members">Members with masked names.</param>
public sealed record KrsBody(string Name, string? RepresentationMethod, IReadOnlyList<KrsPersonSummary> Members);

/// <summary>A board member or proxy with the API's masked name.</summary>
/// <param name="MaskedName">Masked name as published, e.g. <c>D***** M****</c>.</param>
/// <param name="Function">Function in the body, e.g. <c>PREZES ZARZĄDU</c>.</param>
/// <param name="IsSuspended">Whether the member is suspended (register P only).</param>
public sealed record KrsPersonSummary(string MaskedName, string? Function, bool? IsSuspended);

/// <summary>A shareholder of a sp. z o.o.</summary>
/// <param name="Name">Entity name, or the masked name of a natural person.</param>
/// <param name="IsLegalEntity">True for companies and other legal entities.</param>
/// <param name="Krs">KRS number of a legal-entity shareholder.</param>
/// <param name="Regon">REGON of a legal-entity shareholder.</param>
/// <param name="Shares">Shares held (free text).</param>
/// <param name="HoldsAllShares">Whether the shareholder holds all shares.</param>
public sealed record KrsShareholderSummary(string Name, bool IsLegalEntity, KrsNumber? Krs, Regon? Regon, string? Shares, bool? HoldsAllShares);

/// <summary>A PKD activity.</summary>
/// <param name="Code">PKD code, e.g. <c>93.13.Z</c>.</param>
/// <param name="Description">Description in Polish.</param>
public sealed record KrsActivity(string Code, string Description);
