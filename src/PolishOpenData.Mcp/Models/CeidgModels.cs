using System;
using System.Collections.Generic;

namespace PolishOpenData.Mcp;

internal sealed record CeidgPkdView(string Code, string? Name);

/// <summary>One CEIDG entry as lookup_ceidg returns it: a sole trader's business, no contact data.</summary>
internal sealed record CeidgBusinessView
{
    /// <summary>The CEIDG entry id.</summary>
    public required string Id { get; init; }

    /// <summary>Business name as CEIDG holds it; untrusted text.</summary>
    public required string Name { get; init; }

    /// <summary>"active", "suspended", "deleted", "pending_start", "partnership_only" or "unknown".</summary>
    public required string Status { get; init; }

    /// <summary>The status exactly as CEIDG sent it.</summary>
    public string? StatusRaw { get; init; }

    /// <summary>The owner's first name and surname: personal data.</summary>
    public string? Owner { get; init; }

    public string? Nip { get; init; }

    public string? Regon { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? SuspendedOn { get; init; }

    public DateOnly? ResumedOn { get; init; }

    public DateOnly? EndedOn { get; init; }

    /// <summary>One line: street and number, postal code, town.</summary>
    public string? Address { get; init; }

    public CeidgPkdView? MainPkd { get; init; }

    /// <summary>The PKD codes (at most 10).</summary>
    public IReadOnlyList<CeidgPkdView> Pkd { get; init; } = [];

    public required string Note { get; init; }
}

internal sealed record CeidgOverview
{
    /// <summary>What was asked, for example "NIP 3563457932".</summary>
    public required string Query { get; init; }

    public required bool Found { get; init; }

    public required IReadOnlyList<CeidgBusinessView> Businesses { get; init; }

    public required IReadOnlyList<SourceInfo> Sources { get; init; }
}
