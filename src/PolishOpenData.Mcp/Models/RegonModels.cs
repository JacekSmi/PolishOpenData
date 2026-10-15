using System;
using System.Collections.Generic;

namespace PolishOpenData.Mcp;

/// <summary>One REGON register entry as lookup_regon returns it: no contact data, owners' personal details or local-unit lists.</summary>
internal sealed record RegonEntityView
{
    /// <summary>"legal_person", "natural_person", "legal_person_local_unit", "natural_person_local_unit" or "unknown".</summary>
    public required string Type { get; init; }

    /// <summary>
    /// For sole traders GUS lists one row per activity type, all with the same REGON and name: "ceidg", "agricultural", "other" or
    /// "removed_before_2014". Tells those rows apart (each has its own status). Null for legal persons.
    /// </summary>
    public string? Activity { get; init; }

    /// <summary>Name as GUS holds it; untrusted text (may contain markup).</summary>
    public required string Name { get; init; }

    public string? Regon { get; init; }

    public string? Nip { get; init; }

    public string? StatusNip { get; init; }

    /// <summary>One line: street and number, postal code, town.</summary>
    public string? Address { get; init; }

    /// <summary>"active", "suspended" or "ended", derived from GUS's dates. Null when GUS gave no details.</summary>
    public string? Status { get; init; }

    public DateOnly? ActivityStartDate { get; init; }

    public DateOnly? SuspensionDate { get; init; }

    public DateOnly? ResumptionDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public DateOnly? RegonRemovalDate { get; init; }

    /// <summary>Legal persons only.</summary>
    public string? LegalForm { get; init; }

    /// <summary>Legal persons only: the register the entity is entered in (for example a court register).</summary>
    public string? RegisterType { get; init; }

    /// <summary>Legal persons only. Not necessarily a KRS number: do not treat it as one.</summary>
    public string? RegisterNumber { get; init; }

    public RegonPkdView? MainPkd { get; init; }

    /// <summary>How many local units GUS lists; the units themselves are never returned.</summary>
    public int? LocalUnitCount { get; init; }

    /// <summary>Set for sole traders: the business name contains the owner's name and the address may be a home address.</summary>
    public string? Note { get; init; }
}

internal sealed record RegonPkdView(string Code, string? Name, string? Version);

internal sealed record RegonOverview
{
    /// <summary>What was asked, for example "NIP 5260250995".</summary>
    public required string Query { get; init; }

    public required bool Found { get; init; }

    /// <summary>Always "production": the server never uses GUS's anonymised test service.</summary>
    public required string Environment { get; init; }

    /// <summary>The date of GUS's data, when known.</summary>
    public DateOnly? DataAsOf { get; init; }

    public required IReadOnlyList<RegonEntityView> Entities { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required IReadOnlyList<SourceInfo> Sources { get; init; }
}
