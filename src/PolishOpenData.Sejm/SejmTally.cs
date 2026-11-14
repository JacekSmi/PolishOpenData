using System;
using System.Collections.Generic;
using System.Linq;

namespace PolishOpenData.Sejm;

/// <summary>
/// Sums individual votes into club tallies. The API gives no club totals, only one entry per deputy with the club as of the voting day,
/// so the club is read from each vote and never joined to the current membership.
/// </summary>
public static class SejmTally
{
    /// <summary>
    /// Groups votes by club. A vote without a club is counted under <c>(none)</c>. YES, NO, ABSTAIN, ABSENT and PRESENT have their own
    /// columns; any other value (VOTE_VALID in a list voting, NO_VOTE, unseen values) goes to <c>Other</c>. Biggest club first, then by name.
    /// </summary>
    public static IReadOnlyList<SejmClubTally> ByClub(IReadOnlyList<SejmMpVote> votes)
    {
        ArgumentNullException.ThrowIfNull(votes);
        return votes
            .GroupBy(static v => string.IsNullOrWhiteSpace(v.Club) ? SejmMapper.NoClub : v.Club!.Trim(), StringComparer.Ordinal)
            .Select(static g => new SejmClubTally(
                g.Key,
                g.Count(),
                g.Count(static v => Is(v, "YES")),
                g.Count(static v => Is(v, "NO")),
                g.Count(static v => Is(v, "ABSTAIN")),
                g.Count(static v => Is(v, "ABSENT")),
                g.Count(static v => Is(v, "PRESENT")),
                g.Count(static v => !Is(v, "YES") && !Is(v, "NO") && !Is(v, "ABSTAIN") && !Is(v, "ABSENT") && !Is(v, "PRESENT"))))
            .OrderByDescending(static c => c.Seats)
            .ThenBy(static c => c.Club, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// True when the club sums agree with the official totals. For a list voting (<c>ON_LIST</c>) the deputies who voted must equal
    /// <see cref="SejmVoting.TotalVoted"/>; for every other kind the sums of yes, no, abstain and present must equal the official counts.
    /// False means the API changed shape or the data is inconsistent: do not present the club table as exact.
    /// </summary>
    public static bool SumsMatch(SejmVoting voting, IReadOnlyList<SejmClubTally> clubs)
    {
        ArgumentNullException.ThrowIfNull(voting);
        ArgumentNullException.ThrowIfNull(clubs);
        if (string.Equals(voting.Kind, "ON_LIST", StringComparison.OrdinalIgnoreCase))
        {
            return clubs.Sum(static c => c.Seats - c.Absent) == voting.TotalVoted;
        }

        return clubs.Sum(static c => c.Yes) == voting.Yes
            && clubs.Sum(static c => c.No) == voting.No
            && clubs.Sum(static c => c.Abstain) == voting.Abstain
            && clubs.Sum(static c => c.Present) == voting.Present;
    }

    private static bool Is(SejmMpVote vote, string value) => string.Equals(vote.Vote, value, StringComparison.OrdinalIgnoreCase);
}
