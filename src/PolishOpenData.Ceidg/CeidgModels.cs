using System;
using System.Collections.Generic;

namespace PolishOpenData.Ceidg;

/// <summary>The status of a CEIDG entry.</summary>
public enum CeidgStatus
{
    /// <summary>A value this library does not know; see <see cref="CeidgBusiness.StatusRaw"/>.</summary>
    Unknown = 0,

    /// <summary><c>AKTYWNY</c>: the business is running.</summary>
    Active,

    /// <summary><c>ZAWIESZONY</c>: suspended.</summary>
    Suspended,

    /// <summary><c>WYKRESLONY</c>: removed from the register.</summary>
    Deleted,

    /// <summary><c>OCZEKUJE_NA_ROZPOCZECIE_DZIALANOSCI</c>: registered, not yet started.</summary>
    PendingStart,

    /// <summary><c>WYLACZNIE_W_FORMIE_SPOLKI</c>: the person trades only through a partnership.</summary>
    PartnershipOnly,
}

/// <summary>A business address in CEIDG.</summary>
public sealed class CeidgAddress
{
    /// <summary>Street, with the prefix as CEIDG writes it (for example <c>ul. Zwierzyniecka</c>).</summary>
    public string? Street { get; init; }

    /// <summary>Building number.</summary>
    public string? Building { get; init; }

    /// <summary>Unit (flat or office) number.</summary>
    public string? Unit { get; init; }

    /// <summary>City or town.</summary>
    public string? City { get; init; }

    /// <summary>Voivodeship, upper case as CEIDG writes it.</summary>
    public string? Voivodeship { get; init; }

    /// <summary>County (powiat).</summary>
    public string? County { get; init; }

    /// <summary>Commune (gmina).</summary>
    public string? Commune { get; init; }

    /// <summary>Country code, usually <c>PL</c>.</summary>
    public string? Country { get; init; }

    /// <summary>Postal code, for example <c>15-333</c>.</summary>
    public string? PostalCode { get; init; }

    /// <summary>TERYT territorial code (<c>terc</c>).</summary>
    public string? TercCode { get; init; }

    /// <summary>TERYT locality code (<c>simc</c>).</summary>
    public string? SimcCode { get; init; }

    /// <summary>TERYT street code (<c>ulic</c>).</summary>
    public string? UlicCode { get; init; }
}

/// <summary>A PKD (activity classification) code.</summary>
public sealed class CeidgPkd
{
    /// <summary>The code, for example <c>6201Z</c>.</summary>
    public required string Code { get; init; }

    /// <summary>The description, when CEIDG sends one.</summary>
    public string? Name { get; init; }
}

/// <summary>One entry of the CEIDG register: a sole trader's business. The owner's name is personal data.</summary>
public sealed class CeidgBusiness
{
    /// <summary>The CEIDG entry id.</summary>
    public Guid Id { get; init; }

    /// <summary>The business name.</summary>
    public string? Name { get; init; }

    /// <summary>The status; <see cref="CeidgStatus.Unknown"/> for a value this library does not know.</summary>
    public CeidgStatus Status { get; init; }

    /// <summary>The status exactly as CEIDG sent it.</summary>
    public string? StatusRaw { get; init; }

    /// <summary>Owner's first name.</summary>
    public string? OwnerFirstName { get; init; }

    /// <summary>Owner's surname.</summary>
    public string? OwnerLastName { get; init; }

    /// <summary>Owner's NIP, when present and valid.</summary>
    public Nip? Nip { get; init; }

    /// <summary>Owner's REGON, when present and valid.</summary>
    public Regon? Regon { get; init; }

    /// <summary>The day the business started.</summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>The day the business was suspended.</summary>
    public DateOnly? SuspendedOn { get; init; }

    /// <summary>The day the business resumed after a suspension.</summary>
    public DateOnly? ResumedOn { get; init; }

    /// <summary>The day the business ended.</summary>
    public DateOnly? EndedOn { get; init; }

    /// <summary>The address of the business, when CEIDG has one.</summary>
    public CeidgAddress? Address { get; init; }

    /// <summary>The main PKD code.</summary>
    public CeidgPkd? MainPkd { get; init; }

    /// <summary>All PKD codes (empty when CEIDG sends none).</summary>
    public IReadOnlyList<CeidgPkd> Pkd { get; init; } = [];

    /// <summary>The API address of this entry.</summary>
    public string? Link { get; init; }
}
