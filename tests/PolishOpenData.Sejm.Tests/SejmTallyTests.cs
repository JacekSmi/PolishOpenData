using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PolishOpenData.Sejm.Tests;

public class SejmTallyTests
{
    private static (SejmVoting Voting, IReadOnlyList<SejmMpVote> Votes) Load(string fixture)
    {
        var dto = JsonSerializer.Deserialize(PolishOpenData.Tests.Shared.Fixture.Read("sejm/" + fixture), SejmJson.Voting)!;
        return (SejmMapper.ToVoting(dto), SejmMapper.ToMpVotes(dto.Votes));
    }

    private static SejmMpVote V(int id, string? club, string vote) => new(id, "JAN KOWALSKI", club, vote);

    [Fact]
    public void Tally67_1_SumsMatchTotals()
    {
        var (voting, votes) = Load("voting-67-1.json");

        var clubs = SejmTally.ByClub(votes);

        Assert.True(SejmTally.SumsMatch(voting, clubs));
        Assert.Equal(460, clubs.Sum(c => c.Seats));
        Assert.Equal(389, clubs.Sum(c => c.Yes));
        var ko = clubs.Single(c => c.Club == "KO");
        Assert.Equal(156, ko.Seats);
    }

    [Fact]
    public void TamperedVote_SumsFalse()
    {
        var (voting, votes) = Load("voting-67-1.json");
        var tampered = votes.ToList();
        var i = tampered.FindIndex(v => v.Vote == "YES");
        tampered[i] = tampered[i] with { Vote = "NO" };

        Assert.False(SejmTally.SumsMatch(voting, SejmTally.ByClub(tampered)));
    }

    [Fact]
    public void Quorum67_2_CountsPresent()
    {
        var (voting, votes) = Load("voting-67-2-quorum.json");

        var clubs = SejmTally.ByClub(votes);

        Assert.Equal(407, clubs.Sum(c => c.Present));
        Assert.Equal(0, clubs.Sum(c => c.Yes));
        Assert.Equal(53, clubs.Sum(c => c.Absent));
        Assert.True(SejmTally.SumsMatch(voting, clubs));
    }

    [Fact]
    public void OnList_ValidVotesAreOther_ChecksTotalVoted()
    {
        var (voting, votes) = Load("synthetic-voting-on-list.json");

        var clubs = SejmTally.ByClub(votes);

        Assert.Equal(457, clubs.Sum(c => c.Other));
        Assert.Equal(3, clubs.Sum(c => c.Absent));
        Assert.Equal(0, clubs.Sum(c => c.Yes));
        Assert.True(SejmTally.SumsMatch(voting, clubs));
        Assert.False(SejmTally.SumsMatch(voting with { TotalVoted = 456 }, clubs));
    }

    [Fact]
    public void Tally_NullClub_IsNone_NiezKept()
    {
        var clubs = SejmTally.ByClub([V(1, null, "YES"), V(2, "niez.", "NO"), V(3, "  ", "ABSTAIN")]);

        var none = clubs.Single(c => c.Club == "(none)");
        Assert.Equal(2, none.Seats);
        Assert.Equal((1, 1), (none.Yes, none.Abstain));
        Assert.Contains(clubs, c => c.Club == "niez." && c.No == 1);
    }

    [Fact]
    public void Tally_SortedBySeatsThenClub()
    {
        var clubs = SejmTally.ByClub([V(1, "B", "YES"), V(2, "A", "YES"), V(3, "C", "YES"), V(4, "C", "NO")]);

        Assert.Equal(new[] { "C", "A", "B" }, clubs.Select(c => c.Club).ToArray());
    }

    [Fact]
    public void UnknownVoteValues_GoToOther()
    {
        var clubs = SejmTally.ByClub([V(1, "A", "NO_VOTE"), V(2, "A", "BRAND_NEW")]);

        Assert.Equal(2, Assert.Single(clubs).Other);
    }
}
