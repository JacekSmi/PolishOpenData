using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PolishOpenData.Sejm.Tests;

public class SejmMapperTests
{
    private static string Fixture(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "tests", "Fixtures", "sejm")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return File.ReadAllText(Path.Combine(dir ?? throw new FileNotFoundException("tests/Fixtures/sejm not found"), "tests", "Fixtures", "sejm", name));
    }

    private static T Read<T>(string name, JsonTypeInfo<T> info) => JsonSerializer.Deserialize(Fixture(name), info)!;

    [Fact]
    public void Process318_EliAndDisplayAddress()
    {
        var p = SejmMapper.ToProcess(Read("process-318.json", SejmJson.Process));

        Assert.Equal("318", p.Summary.Number);
        Assert.Equal("DU/2024/878", p.Summary.Eli);
        Assert.Equal("Dz.U. 2024 poz. 878", p.Summary.DisplayAddress);
        Assert.True(p.Summary.Passed);
        Assert.False(p.Summary.Urgent);
        Assert.Equal(new DateOnly(2024, 4, 18), p.Summary.StartDate);
        Assert.Equal(new DateTime(2024, 6, 27, 19, 35, 17, DateTimeKind.Unspecified), p.Summary.ChangeDate);
        Assert.Equal("RM-0610-29-24", p.RclNum);
        Assert.NotNull(p.RclLink);
    }

    [Fact]
    public void Process318_NoPropertyContainsRapporteur()
    {
        var p = SejmMapper.ToProcess(Read("process-318.json", SejmJson.Process));

        var json = JsonSerializer.Serialize(p);

        Assert.DoesNotContain("KOWALSKI", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rapporteur", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Process318_MapsNestedStagesAndEmbeddedVoting()
    {
        var p = SejmMapper.ToProcess(Read("process-318.json", SejmJson.Process));

        var third = p.Stages.Single(s => s.Name.StartsWith("III czytanie", StringComparison.Ordinal));
        Assert.Equal("SejmReading", third.Type);
        Assert.Equal("uchwalono", third.Decision);
        Assert.Equal("wniosek o odrzucenie", third.Comment);
        Assert.Equal(12, third.Sitting);
        Assert.NotNull(third.TextAfterThirdReading);
        var voting = Assert.Single(third.Children).Voting;
        Assert.NotNull(voting);
        Assert.Equal((12, 28, 408), (voting.Sitting, voting.Number, voting.Yes));
        var referral = p.Stages.First(s => s.Type == "ReadingReferral").Children.Single();
        Assert.Equal("Sejm", referral.CommitteeCode);
    }

    [Fact]
    public void Process_EndStageWithoutDate()
    {
        var p = SejmMapper.ToProcess(Read("process-318.json", SejmJson.Process));

        var end = p.Stages[p.Stages.Count - 1];

        Assert.Equal("End", end.Type);
        Assert.Null(end.Date);
        Assert.Equal("Uchwalono", end.Name);
    }

    [Fact]
    public void Process_SenatePositionGoesToDecision()
    {
        var p = SejmMapper.ToProcess(Read("process-318.json", SejmJson.Process));

        var senate = p.Stages.Single(s => s.Type == "SenatePosition");

        Assert.Equal("nie wniósł poprawek", senate.Decision);
    }

    [Fact]
    public void Process_UnknownStageTypeKept_AndUrgentRead()
    {
        var p = SejmMapper.ToProcess(Read("synthetic-process-unknown-stage.json", SejmJson.Process));

        Assert.Equal("BrandNewStageType", Assert.Single(p.Stages).Type);
        Assert.True(p.Summary.Urgent);
        Assert.Null(p.Summary.Eli);
    }

    [Fact]
    public void ProcessPage_MapsFiveSummaries()
    {
        var page = Read("processes-page.json", SejmJson.Processes).Select(SejmMapper.ToProcessSummary).ToList();

        Assert.Equal(5, page.Count);
        Assert.All(page, p => Assert.False(string.IsNullOrEmpty(p.Number)));
    }

    [Fact]
    public void Print401_ProcessNumberIs318()
    {
        var p = SejmMapper.ToPrint(Read("print-401.json", SejmJson.Print));

        Assert.Equal("401", p.Number);
        Assert.Equal("318", p.ProcessNumber);
        Assert.Contains("401.pdf", p.Attachments);
    }

    [Fact]
    public void Print1153A_EmptyProcessPrint_IsNullProcess()
    {
        var p = SejmMapper.ToPrint(Read("synthetic-print-1153-A.json", SejmJson.Print));

        Assert.Equal("1153-A", p.Number);
        Assert.Null(p.ProcessNumber);
    }

    [Fact]
    public void Bill_Maps()
    {
        var bill = SejmMapper.ToBill(Assert.Single(Read("bills-print-318.json", SejmJson.Bills)));

        Assert.Equal("318", bill.Print);
        Assert.Equal("GOVERNMENT", bill.ApplicantType);
        Assert.True(bill.EuRelated);
        Assert.Equal(new DateOnly(2024, 4, 18), bill.DateOfReceipt);
    }

    [Fact]
    public void Terms_OneCurrent()
    {
        var terms = Read("terms.json", SejmJson.Terms).Select(SejmMapper.ToTerm).ToList();

        Assert.Equal(10, Assert.Single(terms, t => t.Current).Number);
        Assert.Null(terms.Single(t => t.Current).To);
    }

    [Fact]
    public void VotingIndex_Maps()
    {
        var days = Read("votings-index.json", SejmJson.VotingDays).Select(SejmMapper.ToVotingDay).ToList();

        Assert.Equal(6, days.Count);
        Assert.All(days, d => Assert.True(d.VotingCount > 0));
    }

    [Fact]
    public void Voting67_1_Has460Votes_AndMapsTotals()
    {
        var dto = Read("voting-67-1.json", SejmJson.Voting);
        var voting = SejmMapper.ToVoting(dto);
        var votes = SejmMapper.ToMpVotes(dto.Votes);

        Assert.Equal(460, votes.Count);
        Assert.Equal((67, 1, 389, 9, 2, 60, 400), (voting.Sitting, voting.Number, voting.Yes, voting.No, voting.Abstain, voting.NotParticipating, voting.TotalVoted));
        Assert.Equal("SIMPLE_MAJORITY", voting.MajorityType);
        Assert.Equal(10, voting.MajorityVotes);
        Assert.Empty(voting.Options);
        Assert.Equal("JAN KOWALSKI", votes[0].Name);
    }

    [Fact]
    public void OnList_OptionsMapped_DescriptionDropped()
    {
        var voting = SejmMapper.ToVoting(Read("synthetic-voting-on-list.json", SejmJson.Voting));

        Assert.Equal("ON_LIST", voting.Kind);
        Assert.Equal(8, voting.Options.Count);
        Assert.Equal("KANDYDAT A", voting.Options[0].Option);
        Assert.Equal(18, voting.AgainstAll);
        Assert.DoesNotContain("KOWALSKI", JsonSerializer.Serialize(voting.Options));
    }

    [Fact]
    public void Committee_WithoutMembers_HasCompositionOnly()
    {
        var c = SejmMapper.ToCommittee(Read("committee-asw.json", SejmJson.Committee), includeMembers: false);

        Assert.Equal("ASW", c.Code);
        Assert.Empty(c.Members);
        Assert.Equal(40, c.Composition.Sum(s => s.Seats));
        Assert.Equal(8, c.SubCommittees.Count);
        Assert.Equal(new DateOnly(2023, 11, 21), c.AppointmentDate);
    }

    [Fact]
    public void Committee_WithMembers()
    {
        var c = SejmMapper.ToCommittee(Read("committee-asw.json", SejmJson.Committee), includeMembers: true);

        Assert.Equal(40, c.Members.Count);
        Assert.Contains(c.Members, m => m.Function == "zastępca przewodniczącego");
    }

    [Fact]
    public void Clubs_DefaultHasCountButNoMembers()
    {
        var clubs = Read("clubs.json", SejmJson.Clubs).Select(c => SejmMapper.ToClub(c, includeMembers: false)).ToList();

        Assert.Equal(12, clubs.Count);
        Assert.All(clubs, c => Assert.Empty(c.Members));
        Assert.All(clubs, c => Assert.True(c.MembersCount > 0));
    }

    [Fact]
    public void Clubs_WithMembers()
    {
        var club = SejmMapper.ToClub(Read("clubs.json", SejmJson.Clubs)[0], includeMembers: true);

        Assert.Equal(3, club.Members.Count);
    }

    [Theory]
    [InlineData("""{"number":"1","title":"t","changeDate":"2024-13-45T00:00:00","term":10}""")]
    [InlineData("""{"number":"1","title":"t","changeDate":"2024-01-01T00:00:00","term":10,"closureDate":"31.12.2024"}""")]
    [InlineData("""{"title":"t","changeDate":"2024-01-01T00:00:00","term":10}""")]
    public void BadDateOrMissingNumber_Throws(string json)
    {
        var dto = JsonSerializer.Deserialize(json, SejmJson.Process)!;

        Assert.Throws<FormatException>(() => SejmMapper.ToProcessSummary(dto));
    }

    [Fact]
    public void UnknownFields_Ignored()
    {
        var dto = JsonSerializer.Deserialize("""{"number":"1","title":"t","changeDate":"2024-01-01T00:00:00","term":10,"brandNew":{"x":1}}""", SejmJson.Process)!;

        Assert.Equal("1", SejmMapper.ToProcessSummary(dto).Number);
    }
}
