using System;
using System.Collections.Generic;

namespace PolishOpenData.Mcp;

/// <summary>One REGON register entry as lookup_regon returns it: no contact data, owners' personal details or local-unit lists.</summary>
internal sealed record RegonEntityView
{
    /// <summary>"legal_person", "natural_person", "legal_person_local_unit", "natural_person_local_unit" or "unknown".</summary>
    public required string Type { get; init; }

    /// <summary>Name as GUS holds it; untrusted text (may contain markup).</summary>
    public required string Name { get; init; }

    public string? Regon { get; init; }

    public string? Nip { get; init; }

    public string? StatusNip { get; init; }

    /// <summary>One line: street and number, postal code, town.</summary>
    public string? Address { get; init; }
}

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
