using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PolishOpenData.Bir;

/// <summary>An activity code (PKD) of an entity.</summary>
public sealed class RegonPkd
{
    /// <summary>The code, for example <c>6201Z</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>The name of the activity.</summary>
    public string? Name { get; init; }

    /// <summary>True for the main activity.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>The PKD edition as GUS sends it (<c>2007</c> or <c>2025</c>); <c>null</c> when it sent none.</summary>
    public string? Version { get; init; }

    /// <summary>For a natural person, the register part the code belongs to; <c>null</c> for every other kind of entity.</summary>
    public RegonSilo? Silo { get; init; }
}

/// <summary>A local unit (branch, plant) as listed in the report of its entity.</summary>
public sealed class RegonLocalUnit
{
    /// <summary>The 14-digit REGON; <c>null</c> when GUS sent none or an invalid one.</summary>
    public Regon? Regon { get; init; }

    /// <summary>The name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The register part of the unit of a natural person; <see cref="RegonSilo.Unknown"/> for every other unit.</summary>
    public RegonSilo Silo { get; init; }

    /// <summary>The address of the unit.</summary>
    public RegonAddress Address { get; init; } = new();

    /// <summary>The date the unit was formed.</summary>
    public DateOnly? FormationDate { get; init; }

    /// <summary>The date the activity started.</summary>
    public DateOnly? ActivityStartDate { get; init; }

    /// <summary>The date the unit was entered into REGON.</summary>
    public DateOnly? RegonEntryDate { get; init; }

    /// <summary>The date the activity was suspended.</summary>
    public DateOnly? SuspensionDate { get; init; }

    /// <summary>The date the activity was resumed.</summary>
    public DateOnly? ResumptionDate { get; init; }

    /// <summary>The date the activity ended.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>The date the unit was removed from REGON.</summary>
    public DateOnly? RegonRemovalDate { get; init; }
}

/// <summary>A partner of a civil partnership. The data is personal data as GUS publishes it.</summary>
public sealed class RegonPartner
{
    /// <summary>The REGON of the partner; <c>null</c> when GUS sent none or an invalid one.</summary>
    public Regon? Regon { get; init; }

    /// <summary>First name of a person.</summary>
    public string? FirstName { get; init; }

    /// <summary>Second name of a person.</summary>
    public string? SecondName { get; init; }

    /// <summary>Last name of a person.</summary>
    public string? LastName { get; init; }

    /// <summary>Company name of a partner that is a company.</summary>
    public string? CompanyName { get; init; }
}

/// <summary>
/// The details of one entity from its full reports: a typed core of the fields every kind of entity shares, and
/// <see cref="Fields"/> with every non-empty field exactly as GUS sent it. A natural person who is active in several
/// register parts has one <see cref="RegonDetails"/> for each part. Contact data (phone, fax, e-mail, website) and the
/// name fields of a person are not typed on purpose; they are in <see cref="Fields"/> only.
/// </summary>
public sealed class RegonDetails
{
    /// <summary>The kind of entity.</summary>
    public RegonEntityType Type { get; init; }

    /// <summary>The register part these details come from.</summary>
    public RegonSilo Silo { get; init; }

    /// <summary>The REGON (9 digits, 14 for a local unit); <c>null</c> when GUS sent none or an invalid one.</summary>
    public Regon? Regon { get; init; }

    /// <summary>The NIP of a legal person, of a natural person, and of a local unit of a legal person (report <c>BIR121JednLokalnaOsPrawnej</c>).</summary>
    public Nip? Nip { get; init; }

    /// <summary><c>null</c> when the NIP is valid; otherwise the status as GUS sent it.</summary>
    public string? StatusNip { get; init; }

    /// <summary>The name; for a sole trader it is the name of the person. Untrusted text: GUS warns that it can contain HTML or SQL-like text.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The short name.</summary>
    public string? ShortName { get; init; }

    /// <summary>Code of the basic legal form (<c>podstawowaFormaPrawna_Symbol</c>), a string that keeps its leading zeros.</summary>
    public string? LegalFormCode { get; init; }

    /// <summary>Name of the basic legal form.</summary>
    public string? LegalFormName { get; init; }

    /// <summary>Code of the kind of register or records the entity is entered in.</summary>
    public string? RegisterTypeCode { get; init; }

    /// <summary>Name of the kind of register or records.</summary>
    public string? RegisterTypeName { get; init; }

    /// <summary>
    /// The number in that register (<c>numerWRejestrzeEwidencji</c>). It looks like a KRS number for many legal persons,
    /// but GUS does not say so and this library does not assume it.
    /// </summary>
    public string? RegisterNumber { get; init; }

    /// <summary>The date of the entry in that register.</summary>
    public DateOnly? RegisterEntryDate { get; init; }

    /// <summary>The date the entity was formed.</summary>
    public DateOnly? FormationDate { get; init; }

    /// <summary>The date the activity started.</summary>
    public DateOnly? ActivityStartDate { get; init; }

    /// <summary>The date the entity was entered into REGON.</summary>
    public DateOnly? RegonEntryDate { get; init; }

    /// <summary>The date the activity was suspended.</summary>
    public DateOnly? SuspensionDate { get; init; }

    /// <summary>The date the activity was resumed.</summary>
    public DateOnly? ResumptionDate { get; init; }

    /// <summary>The date the activity ended.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>The date the entity was removed from REGON.</summary>
    public DateOnly? RegonRemovalDate { get; init; }

    /// <summary>The date a bankruptcy was declared.</summary>
    public DateOnly? BankruptcyDate { get; init; }

    /// <summary>The number of local units GUS reports.</summary>
    public int? LocalUnitCount { get; init; }

    /// <summary>Natural persons: whether there is an activity in CEIDG (<c>fiz_dzialalnoscCeidg</c>); <c>null</c> when GUS did not say.</summary>
    public bool? HasCeidgActivity { get; init; }

    /// <summary>Natural persons: whether there is an agricultural activity.</summary>
    public bool? HasAgriculturalActivity { get; init; }

    /// <summary>Natural persons: whether there is another activity.</summary>
    public bool? HasOtherActivity { get; init; }

    /// <summary>Natural persons: whether there is an activity that was removed before 2014-11-08.</summary>
    public bool? HasActivityRemovedBefore2014 { get; init; }

    /// <summary>Whether the activity was never started (<c>NiePodjetoDzialalnosci</c>).</summary>
    public bool? ActivityNotStarted { get; init; }

    /// <summary>The address of the seat, or of the activity.</summary>
    public RegonAddress Address { get; init; } = new();

    /// <summary>The PKD codes; <c>null</c> unless <see cref="RegonInclude.Pkd"/> was requested.</summary>
    public IReadOnlyList<RegonPkd>? Pkd { get; init; }

    /// <summary>The local units; <c>null</c> unless <see cref="RegonInclude.LocalUnits"/> was requested and applies to this kind of entity.</summary>
    public IReadOnlyList<RegonLocalUnit>? LocalUnits { get; init; }

    /// <summary>The partners of a civil partnership; <c>null</c> unless <see cref="RegonInclude.Partners"/> was requested and applies.</summary>
    public IReadOnlyList<RegonPartner>? Partners { get; init; }

    /// <summary>
    /// Every non-empty field of the core reports by its GUS name, for example <c>praw_numerTelefonu</c>. The contact data and
    /// the person names of this entity are here only, not in typed properties (partners have their own <see cref="RegonPartner"/>).
    /// Empty by default.
    /// </summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = EmptyFields;

    private static readonly IReadOnlyDictionary<string, string> EmptyFields = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}
