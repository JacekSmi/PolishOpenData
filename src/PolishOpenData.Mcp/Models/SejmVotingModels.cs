using System;
using System.Collections.Generic;

namespace PolishOpenData.Mcp;

/// <summary>search_sejm_votings result.</summary>
internal sealed record SejmVotingSearchResult
{
    public required int Term { get; init; }

    public required string Query { get; init; }

    public required IReadOnlyList<SejmVotingRow> Items { get; init; }

    /// <summary>True when another page exists (exact: one extra item is requested). The API reports no total.</summary>
    public required bool HasMore { get; init; }

    public string? Hint { get; init; }

    public required string Source { get; init; }
}

/// <summary>How one club voted.</summary>
internal sealed record SejmClubRow(string Club, int Seats, int Yes, int No, int Abstain, int Absent, int Present, int Other);

/// <summary>An option in a list voting and whether it reached the required votes (inferred).</summary>
internal sealed record SejmOptionRow(int Index, string Option, int Votes, string Outcome);

/// <summary>One deputy's vote: name, club, vote. Never the deputy's id.</summary>
internal sealed record SejmMpVoteRow(string Name, string? Club, string Vote);

/// <summary>get_sejm_voting result.</summary>
internal sealed record SejmVotingResult
{
    public required int Term { get; init; }

    public required int Sitting { get; init; }

    public required int Number { get; init; }

    public required DateOnly Date { get; init; }

    public required string Title { get; init; }

    public string? Topic { get; init; }

    /// <summary>ELECTRONIC, TRADITIONAL or ON_LIST.</summary>
    public required string Kind { get; init; }

    public required string MajorityType { get; init; }

    /// <summary>Yes votes needed to pass (the API's majorityVotes).</summary>
    public required int Required { get; init; }

    public required int Yes { get; init; }

    public required int No { get; init; }

    public required int Abstain { get; init; }

    public required int Present { get; init; }

    public required int NotParticipating { get; init; }

    /// <summary>"passed (inferred: yes >= required)" or "failed (inferred: yes &lt; required)"; null for quorum and list votings. Not an official field.</summary>
    public string? Outcome { get; init; }

    /// <summary>Result per club, summed from the individual votes, biggest club first.</summary>
    public required IReadOnlyList<SejmClubRow> Clubs { get; init; }

    /// <summary>False when the club sums do not equal the official totals: do not present the club table as exact.</summary>
    public required bool SumsMatchTotals { get; init; }

    /// <summary>For a list voting, the options (candidates for public office, as published) and their votes; otherwise null.</summary>
    public IReadOnlyList<SejmOptionRow>? Options { get; init; }

    public required IReadOnlyList<string> Prints { get; init; }

    public string? Hint { get; init; }

    /// <summary>Individual votes with names, only with includeMpVotes; otherwise null. Personal data.</summary>
    public IReadOnlyList<SejmMpVoteRow>? MpVotes { get; init; }

    public string? PersonalDataNote { get; init; }

    public required string Source { get; init; }
}

/// <summary>A club or committee in a list: how many members, never their names.</summary>
internal sealed record SejmBodyRow(string Code, string Name, string? Type, int Members);

/// <summary>Seats of one club in a committee.</summary>
internal sealed record SejmSeatRow(string Club, int Seats);

/// <summary>A deputy as a member of a club or committee (public-role data).</summary>
internal sealed record SejmMemberRow(string Name, string? Club, string? Function, DateOnly? Since);

/// <summary>One club or committee.</summary>
internal sealed record SejmBodyDetail
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? Type { get; init; }

    public string? Scope { get; init; }

    public DateOnly? Appointed { get; init; }

    public DateOnly? Dismissed { get; init; }

    public required IReadOnlyList<string> SubCommittees { get; init; }

    /// <summary>Seats per club (committees); empty for a club.</summary>
    public required IReadOnlyList<SejmSeatRow> Composition { get; init; }

    public required int MembersCount { get; init; }

    /// <summary>Current members, only with includeMembers; otherwise null. At most 80.</summary>
    public IReadOnlyList<SejmMemberRow>? Members { get; init; }

    public bool? MembersTruncated { get; init; }

    public string? MembersNote { get; init; }
}

/// <summary>get_sejm_body result: a list (items) or one body (detail).</summary>
internal sealed record SejmBodyResult
{
    public required int Term { get; init; }

    public required string Kind { get; init; }

    public required IReadOnlyList<SejmBodyRow> Items { get; init; }

    public SejmBodyDetail? Detail { get; init; }

    public string? Hint { get; init; }

    public required string Source { get; init; }
}
