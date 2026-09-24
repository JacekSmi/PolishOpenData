using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PolishOpenData.BialaLista;

/// <summary>VAT registration status (<c>statusVat</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VatStatus>))]
public enum VatStatus
{
    /// <summary>A value this library does not know; see <see cref="VatSubject.VatStatusRaw"/>.</summary>
    Unknown,

    /// <summary><c>Czynny</c> — active VAT payer.</summary>
    Active,

    /// <summary><c>Zwolniony</c> — exempt from VAT.</summary>
    Exempt,

    /// <summary><c>Niezarejestrowany</c> — not registered (or removed) as a VAT payer.</summary>
    NotRegistered,
}

/// <summary>A representative, proxy or partner listed on the whitelist.</summary>
public sealed class VatPerson
{
    /// <summary>Company name, when the person is an entity.</summary>
    public string? CompanyName { get; init; }

    /// <summary>First name(s).</summary>
    public string? FirstName { get; init; }

    /// <summary>Last name.</summary>
    public string? LastName { get; init; }

    /// <summary>NIP, when published.</summary>
    public string? Nip { get; init; }

    /// <summary>PESEL, when published.</summary>
    public string? Pesel { get; init; }
}

/// <summary>A taxpayer from the VAT whitelist (<i>wykaz podatników VAT</i>).</summary>
public sealed class VatSubject
{
    /// <summary>Name (company name or full name).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>NIP, when present and valid.</summary>
    public Nip? Nip { get; init; }

    /// <summary>VAT status.</summary>
    public VatStatus VatStatus { get; init; }

    /// <summary>The status as returned (<c>Czynny</c>, <c>Zwolniony</c>, <c>Niezarejestrowany</c>).</summary>
    public string? VatStatusRaw { get; init; }

    /// <summary>REGON, when present and valid.</summary>
    public Regon? Regon { get; init; }

    /// <summary>PESEL, when published (sole traders).</summary>
    public string? Pesel { get; init; }

    /// <summary>KRS number, when the taxpayer is in KRS.</summary>
    public KrsNumber? Krs { get; init; }

    /// <summary>Residence address (natural persons); usually null for companies.</summary>
    public string? ResidenceAddress { get; init; }

    /// <summary>Seat or place of business; for companies this holds the seat address.</summary>
    public string? WorkingAddress { get; init; }

    /// <summary>Representatives.</summary>
    public IReadOnlyList<VatPerson> Representatives { get; init; } = [];

    /// <summary>Proxies (<i>prokurenci</i>).</summary>
    public IReadOnlyList<VatPerson> AuthorizedClerks { get; init; } = [];

    /// <summary>Partners (civil-law partnerships).</summary>
    public IReadOnlyList<VatPerson> Partners { get; init; } = [];

    /// <summary>Date of VAT registration.</summary>
    public DateOnly? RegistrationLegalDate { get; init; }

    /// <summary>Date registration was refused.</summary>
    public DateOnly? RegistrationDenialDate { get; init; }

    /// <summary>Legal basis of the refusal.</summary>
    public string? RegistrationDenialBasis { get; init; }

    /// <summary>Date of restoration.</summary>
    public DateOnly? RestorationDate { get; init; }

    /// <summary>Legal basis of the restoration.</summary>
    public string? RestorationBasis { get; init; }

    /// <summary>Date of removal from the register.</summary>
    public DateOnly? RemovalDate { get; init; }

    /// <summary>Legal basis of the removal.</summary>
    public string? RemovalBasis { get; init; }

    /// <summary>Start of the SME VAT exemption (added to the API in v1.6.0).</summary>
    public DateOnly? ExemptionSmeDate { get; init; }

    /// <summary>Bank accounts on the whitelist.</summary>
    public IReadOnlyList<Nrb> AccountNumbers { get; init; } = [];

    /// <summary>Whether the taxpayer uses virtual accounts (then an account may not be listed individually).</summary>
    public bool HasVirtualAccounts { get; init; }

    /// <summary>Names of fields this library does not model, or values it could not parse. Normally empty.</summary>
    public IReadOnlyList<string> UnknownFields { get; init; } = [];
}
