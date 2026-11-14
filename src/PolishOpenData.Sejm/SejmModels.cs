using System;
using System.Collections.Generic;

namespace PolishOpenData.Sejm;

/// <summary>A page of results.</summary>
/// <param name="Items">The items on this page.</param>
/// <param name="Total">Total matches when the API reports it (processes); null when it does not (votings).</param>
/// <param name="HasMore">True when another page exists. Exact for votings (one extra item is requested); for processes it follows <paramref name="Total"/>.</param>
public sealed record SejmPage<T>(IReadOnlyList<T> Items, int? Total, bool HasMore);

/// <summary>A parliamentary term (kadencja).</summary>
/// <param name="Number">Term number, for example 10.</param>
/// <param name="From">First day.</param>
/// <param name="To">Last day, or null for a running term.</param>
/// <param name="Current">True for the running term.</param>
public sealed record SejmTerm(int Number, DateOnly From, DateOnly? To, bool Current);

/// <summary>One legislative process in a list or at the top of its details.</summary>
/// <param name="Term">Term the process belongs to.</param>
/// <param name="Number">Number of the initiating print (a string: prints can carry suffixes such as <c>128-BA</c>).</param>
/// <param name="Title">Title as submitted.</param>
/// <param name="TitleFinal">Title as finally passed, when it changed.</param>
/// <param name="DocumentType">Kind of document in the API's Polish words (projekt ustawy, projekt uchwały, wniosek, ...); unseen values pass through.</param>
/// <param name="DocumentDate">Date of the document.</param>
/// <param name="StartDate">Date the process started.</param>
/// <param name="ClosureDate">Date the process was closed, or null while it runs.</param>
/// <param name="ChangeDate">When the API last changed the record (Warsaw local time, no offset).</param>
/// <param name="Passed">True when the Sejm passed it. This is not "became law": see <paramref name="Eli"/>.</param>
/// <param name="Urgent">True for an urgent process.</param>
/// <param name="Eli">ELI id of the resulting act (for example <c>DU/2024/878</c>), or null. Passed processes without one are motions, appointments and the like.</param>
/// <param name="DisplayAddress">Citation of the resulting act ("Dz.U. 2024 poz. 878"), or null.</param>
/// <param name="PrintsConsideredJointly">Other prints handled together with this one.</param>
/// <param name="Comments">Comment the API carries (for example a carry-over note), or null.</param>
public sealed record SejmProcessSummary(
    int Term,
    string Number,
    string Title,
    string? TitleFinal,
    string DocumentType,
    DateOnly? DocumentDate,
    DateOnly? StartDate,
    DateOnly? ClosureDate,
    DateTime ChangeDate,
    bool Passed,
    bool Urgent,
    string? Eli,
    string? DisplayAddress,
    IReadOnlyList<string> PrintsConsideredJointly,
    string? Comments);

/// <summary>A process with its stages.</summary>
/// <param name="Summary">The process data.</param>
/// <param name="RclNum">Number of the Government Legislation Centre record, or null.</param>
/// <param name="RclLink">Link to it, or null.</param>
/// <param name="Stages">Stages as the API nests them (readings, committee work, votings, Senate, President).</param>
public sealed record SejmProcess(SejmProcessSummary Summary, string? RclNum, Uri? RclLink, IReadOnlyList<SejmStage> Stages);

/// <summary>One stage of a process. One flat record for every stage type; fields a type does not have are null.</summary>
/// <param name="Type">The API's stage type (Start, ReadingReferral, Referral, SejmReading, CommitteeWork, CommitteeReport, Voting, SenatePosition, ToPresident, PresidentSignature, End...); unseen values pass through.</param>
/// <param name="Name">Stage name in Polish.</param>
/// <param name="Date">Date of the stage, or null (the End stage has none).</param>
/// <param name="Decision">Outcome text (for a reading its decision, for the Senate its position, for the End stage "Uchwalono" or "Odrzucono").</param>
/// <param name="Comment">Comment on the stage, or null.</param>
/// <param name="Sitting">Sitting number, or null.</param>
/// <param name="CommitteeCode">Committee code. Referral stages can carry <c>Sejm</c> (the whole chamber): that is not a committee.</param>
/// <param name="PrintNumber">Print the stage refers to, or null.</param>
/// <param name="Voting">The voting embedded in the stage, or null. Only some votings are embedded.</param>
/// <param name="TextAfterThirdReading">PDF of the text after the third reading, or null.</param>
/// <param name="Children">Nested stages.</param>
public sealed record SejmStage(
    string Type,
    string Name,
    DateOnly? Date,
    string? Decision,
    string? Comment,
    int? Sitting,
    string? CommitteeCode,
    string? PrintNumber,
    SejmVoting? Voting,
    Uri? TextAfterThirdReading,
    IReadOnlyList<SejmStage> Children);

/// <summary>A print (druk).</summary>
/// <param name="Term">Term.</param>
/// <param name="Number">Print number (a string).</param>
/// <param name="Title">Title.</param>
/// <param name="DocumentDate">Date of the document, or null.</param>
/// <param name="DeliveryDate">Date it was delivered, or null.</param>
/// <param name="ProcessNumber">Number of the process this print belongs to (the first of the API's <c>processPrint</c>), or null.</param>
/// <param name="Attachments">File names of the attachments (URLs are not followed).</param>
/// <param name="AdditionalPrints">Additional prints (autopoprawki, sprawozdania).</param>
public sealed record SejmPrint(
    int Term,
    string Number,
    string Title,
    DateOnly? DocumentDate,
    DateOnly? DeliveryDate,
    string? ProcessNumber,
    IReadOnlyList<string> Attachments,
    IReadOnlyList<SejmPrint> AdditionalPrints);

/// <summary>A bill record. There is no status on purpose: the API's status does not follow the outcome (a bill published as an act can still read ACTIVE).</summary>
/// <param name="Number">Bill number.</param>
/// <param name="Print">The print of the bill, or null.</param>
/// <param name="Title">Title.</param>
/// <param name="SubmissionType">How it was submitted (as the API words it).</param>
/// <param name="ApplicantType">Who submitted it (as the API words it): government, deputies, citizens...</param>
/// <param name="DateOfReceipt">Date received.</param>
/// <param name="EuRelated">True for a bill related to EU law.</param>
/// <param name="PublicConsultation">True when public consultation was held.</param>
public sealed record SejmBill(
    string Number,
    string? Print,
    string Title,
    string SubmissionType,
    string ApplicantType,
    DateOnly DateOfReceipt,
    bool EuRelated,
    bool PublicConsultation);

/// <summary>A day with votings.</summary>
/// <param name="Date">The date.</param>
/// <param name="Sitting">Sitting number.</param>
/// <param name="VotingCount">How many votings.</param>
public sealed record SejmVotingDay(DateOnly Date, int Sitting, int VotingCount);

/// <summary>A voting with its official totals (no club split: see <see cref="SejmVotingDetails"/>).</summary>
/// <param name="Term">Term.</param>
/// <param name="Sitting">Sitting number.</param>
/// <param name="SittingDay">Day of the sitting.</param>
/// <param name="Number">Voting number within the sitting.</param>
/// <param name="Date">When it took place (Warsaw local time, no offset).</param>
/// <param name="Title">Title.</param>
/// <param name="Topic">Topic, or null.</param>
/// <param name="Description">Description, or null.</param>
/// <param name="Kind">ELECTRONIC, TRADITIONAL or ON_LIST; unseen values pass through.</param>
/// <param name="MajorityType">As the API names it (SIMPLE_MAJORITY, ABSOLUTE_MAJORITY, STATUTORY_MAJORITY, MAJORITY_TWO_THIRDS...).</param>
/// <param name="MajorityVotes">Yes votes needed to pass. The API has no passed/failed field: comparing it with <paramref name="Yes"/> is an inference.</param>
/// <param name="Yes">Votes for.</param>
/// <param name="No">Votes against.</param>
/// <param name="Abstain">Abstentions.</param>
/// <param name="Present">Present (quorum votings count presence).</param>
/// <param name="NotParticipating">Deputies not taking part.</param>
/// <param name="TotalVoted">Total who voted.</param>
/// <param name="AgainstAll">For a list voting, votes against all; otherwise null.</param>
/// <param name="Options">For a list voting, the options and their votes; otherwise empty.</param>
public sealed record SejmVoting(
    int Term,
    int Sitting,
    int SittingDay,
    int Number,
    DateTime Date,
    string Title,
    string? Topic,
    string? Description,
    string Kind,
    string MajorityType,
    int MajorityVotes,
    int Yes,
    int No,
    int Abstain,
    int Present,
    int NotParticipating,
    int TotalVoted,
    int? AgainstAll,
    IReadOnlyList<SejmVotingOption> Options);

/// <summary>An option in a list voting (for example a candidate for a public office, as published).</summary>
/// <param name="Index">Position in the list.</param>
/// <param name="Option">The option text.</param>
/// <param name="Votes">Votes for it.</param>
public sealed record SejmVotingOption(int Index, string Option, int Votes);

/// <summary>One voting with its club tallies.</summary>
/// <param name="Voting">The official totals.</param>
/// <param name="Clubs">Tally per club, summed from the individual votes (the API has no club totals). Biggest club first.</param>
/// <param name="SumsMatchTotals">False when the club sums do not equal the official totals: the API changed shape or the data is inconsistent.</param>
/// <param name="MpVotes">Individual votes with names, only when asked for; otherwise null. Personal data.</param>
public sealed record SejmVotingDetails(SejmVoting Voting, IReadOnlyList<SejmClubTally> Clubs, bool SumsMatchTotals, IReadOnlyList<SejmMpVote>? MpVotes);

/// <summary>How one club voted.</summary>
/// <param name="Club">Club id as of the voting day (<c>(none)</c> when a vote has no club).</param>
/// <param name="Seats">Deputies counted for the club in this voting.</param>
/// <param name="Yes">Votes for.</param>
/// <param name="No">Votes against.</param>
/// <param name="Abstain">Abstentions.</param>
/// <param name="Absent">Did not vote.</param>
/// <param name="Present">Present (quorum votings).</param>
/// <param name="Other">Any other vote value (for a list voting, the valid votes).</param>
public sealed record SejmClubTally(string Club, int Seats, int Yes, int No, int Abstain, int Absent, int Present, int Other);

/// <summary>One deputy's vote. Personal data: only returned on request.</summary>
/// <param name="MpId">The deputy's id.</param>
/// <param name="Name">Name as the API writes it.</param>
/// <param name="Club">Club as of the voting day, or null.</param>
/// <param name="Vote">YES, NO, ABSTAIN, ABSENT, PRESENT, VOTE_VALID...; unseen values pass through.</param>
public sealed record SejmMpVote(int MpId, string Name, string? Club, string Vote);

/// <summary>A club's seat count in a committee.</summary>
/// <param name="Club">Club id.</param>
/// <param name="Seats">Number of seats.</param>
public sealed record SejmClubSeats(string Club, int Seats);

/// <summary>A deputy as a club or committee member (public-role data: name, club, function).</summary>
/// <param name="Id">The deputy's id.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="Club">Club, or null.</param>
/// <param name="Function">Function in the committee, or null.</param>
/// <param name="JoinDate">Date the deputy joined the committee, or null.</param>
public sealed record SejmMember(int Id, string FirstName, string LastName, string? Club, string? Function, DateOnly? JoinDate);

/// <summary>A club (klub parlamentarny). Current members only, and only when asked for.</summary>
/// <param name="Id">Club id (for example <c>KO</c>).</param>
/// <param name="Name">Full name.</param>
/// <param name="MembersCount">Number of members.</param>
/// <param name="Members">Members; empty unless members were requested.</param>
public sealed record SejmClub(string Id, string Name, int MembersCount, IReadOnlyList<SejmMember> Members);

/// <summary>A committee. Current members only, and only when asked for.</summary>
/// <param name="Code">Committee code (for example <c>ASW</c>).</param>
/// <param name="Name">Name.</param>
/// <param name="Type">Kind (standing, extraordinary, investigative).</param>
/// <param name="Scope">Scope of activity, or null.</param>
/// <param name="AppointmentDate">When appointed, or null.</param>
/// <param name="DismissalDate">When dismissed, or null.</param>
/// <param name="SubCommittees">Codes of its subcommittees.</param>
/// <param name="Composition">Seats per club, summed from the members whether or not they are returned.</param>
/// <param name="Members">Members; empty unless members were requested.</param>
public sealed record SejmCommittee(
    string Code,
    string Name,
    string Type,
    string? Scope,
    DateOnly? AppointmentDate,
    DateOnly? DismissalDate,
    IReadOnlyList<string> SubCommittees,
    IReadOnlyList<SejmClubSeats> Composition,
    IReadOnlyList<SejmMember> Members);

/// <summary>Criteria for processes search.</summary>
public sealed class SejmProcessSearch
{
    /// <summary>Term number.</summary>
    public int Term { get; set; }

    /// <summary>Text to find as a contiguous, case-insensitive part of the title.</summary>
    public string? Title { get; set; }

    /// <summary>Document type in the API's Polish words (for example "projekt ustawy"); passed through unchanged.</summary>
    public string? DocumentType { get; set; }

    /// <summary>When true, only processes the Sejm passed.</summary>
    public bool PassedOnly { get; set; }

    /// <summary>Only processes changed since this moment (Warsaw local time).</summary>
    public DateTime? ModifiedSince { get; set; }

    /// <summary>Page size, 1 to 500. Default 20.</summary>
    public int Limit { get; set; } = 20;

    /// <summary>Index of the first result, 0 or more. Results come in ascending number order; the client does not use the API's sort parameter.</summary>
    public int Offset { get; set; }
}

/// <summary>Criteria for votings search. At least a sitting, a title or a date is required.</summary>
public sealed class SejmVotingSearch
{
    /// <summary>Term number.</summary>
    public int Term { get; set; }

    /// <summary>Sitting number.</summary>
    public int? Sitting { get; set; }

    /// <summary>First day.</summary>
    public DateOnly? DateFrom { get; set; }

    /// <summary>Last day.</summary>
    public DateOnly? DateTo { get; set; }

    /// <summary>Text to find in the title or topic (a substring, so "1344" also matches "13440").</summary>
    public string? Title { get; set; }

    /// <summary>Page size, 1 to 500. Default 20.</summary>
    public int Limit { get; set; } = 20;

    /// <summary>Index of the first result, 0 or more. Results come oldest first and the API reports no total.</summary>
    public int Offset { get; set; }
}
