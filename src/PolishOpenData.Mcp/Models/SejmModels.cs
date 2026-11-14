using System;
using System.Collections.Generic;

namespace PolishOpenData.Mcp;

/// <summary>A process in a search result.</summary>
internal sealed record SejmProcessItem(string Number, string Title, string DocumentType, string Status, DateOnly? StartDate, DateOnly LastChange, string? Eli);

/// <summary>search_sejm_processes result.</summary>
internal sealed record SejmProcessSearchResult
{
    public required int Term { get; init; }

    public required string Query { get; init; }

    /// <summary>All matches the API reports; null when it reports none.</summary>
    public required int? TotalMatches { get; init; }

    public required IReadOnlyList<SejmProcessItem> Items { get; init; }

    public required bool HasMore { get; init; }

    public string? Hint { get; init; }

    public required string Source { get; init; }
}

/// <summary>The act a process produced, for get_legal_act.</summary>
internal sealed record SejmActLink(string Eli, string? DisplayAddress, string Hint);

/// <summary>A voting embedded in a process stage.</summary>
internal sealed record SejmVotingBrief(int Sitting, int Number, int Yes, int No, int Abstain);

/// <summary>One stage of a process, in tree order.</summary>
internal sealed record SejmTimelineItem(DateOnly? Date, string Stage, string? Decision, int? Sitting, string? Committee, string? Print, SejmVotingBrief? Voting);

/// <summary>A voting in a list: official totals, no club split.</summary>
internal sealed record SejmVotingRow(int Sitting, int Number, DateOnly Date, string? Topic, string Title, string Kind, int Yes, int No, int Abstain, int NotParticipating, bool IsFinalPassage);

/// <summary>get_sejm_process result.</summary>
internal sealed record SejmProcessResult
{
    public required int Term { get; init; }

    public required string Number { get; init; }

    public required string Title { get; init; }

    public string? TitleFinal { get; init; }

    public required string DocumentType { get; init; }

    /// <summary>Who submitted the bill (government, deputies, citizens...), from the bill record; null when there is none.</summary>
    public string? SubmittedBy { get; init; }

    public required bool Urgent { get; init; }

    public DateOnly? Started { get; init; }

    public DateOnly? Closed { get; init; }

    public required string Status { get; init; }

    /// <summary>The act the process produced; null when it did not become an act.</summary>
    public SejmActLink? Act { get; init; }

    public required IReadOnlyList<SejmTimelineItem> Timeline { get; init; }

    public required bool TimelineTruncated { get; init; }

    public required IReadOnlyList<string> Prints { get; init; }

    public string? TextAfterThirdReading { get; init; }

    public string? RclLink { get; init; }

    /// <summary>Votings found by title, only with includeAllVotings; null otherwise.</summary>
    public IReadOnlyList<SejmVotingRow>? AllVotings { get; init; }

    /// <summary>True when the list of all votings is partial (more than 6 prints, a full search page, or more than 40 votes); null without includeAllVotings.</summary>
    public bool? AllVotingsTruncated { get; init; }

    public required string Note { get; init; }

    public required string Source { get; init; }
}
