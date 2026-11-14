using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PolishOpenData.Sejm;

/// <summary>Maps wire DTOs to public models. An unreadable required value or date throws <see cref="FormatException"/> (the client wraps it).</summary>
internal static class SejmMapper
{
    public const string NoClub = "(none)";

    public static SejmTerm ToTerm(SejmTermDto dto) => new(
        dto.Num ?? throw new FormatException("Sejm term has no number."),
        Date(dto.From) ?? throw new FormatException("Sejm term has no start date."),
        Date(dto.To),
        dto.Current);

    public static SejmProcessSummary ToProcessSummary(SejmProcessDto dto) => new(
        dto.Term ?? throw new FormatException("Sejm process has no term."),
        Required(dto.Number, "process number"),
        Required(dto.Title, "process title"),
        Text(dto.TitleFinal),
        dto.DocumentType ?? string.Empty,
        Date(dto.DocumentDate),
        Date(dto.ProcessStartDate),
        Date(dto.ClosureDate),
        DateTimeValue(dto.ChangeDate) ?? throw new FormatException("Sejm process has no change date."),
        dto.Passed,
        !string.IsNullOrEmpty(dto.UrgencyStatus) && !string.Equals(dto.UrgencyStatus, "NORMAL", StringComparison.OrdinalIgnoreCase),
        Text(dto.Eli),
        Text(dto.DisplayAddress),
        Texts(dto.PrintsConsideredJointly),
        Text(dto.Comments));

    public static SejmProcess ToProcess(SejmProcessDto dto) => new(
        ToProcessSummary(dto),
        Text(dto.RclNum),
        Uri(dto.RclLink),
        Stages(dto.Stages));

    public static SejmPrint ToPrint(SejmPrintDto dto) => new(
        dto.Term ?? throw new FormatException("Sejm print has no term."),
        Required(dto.Number, "print number"),
        dto.Title ?? string.Empty,
        Date(dto.DocumentDate),
        Date(dto.DeliveryDate),
        dto.ProcessPrint?.FirstOrDefault(static p => !string.IsNullOrWhiteSpace(p))?.Trim(),
        Texts(dto.Attachments),
        (dto.AdditionalPrints ?? []).Where(static p => p is not null).Select(ToPrint).ToList());

    public static SejmBill ToBill(SejmBillDto dto) => new(
        Required(dto.Number, "bill number"),
        Text(dto.Print),
        dto.Title ?? string.Empty,
        dto.SubmissionType ?? string.Empty,
        dto.ApplicantType ?? string.Empty,
        Date(dto.DateOfReceipt) ?? throw new FormatException("Sejm bill has no date of receipt."),
        dto.EuRelated,
        dto.PublicConsultation);

    public static SejmVotingDay ToVotingDay(SejmVotingDayDto dto) => new(
        Date(dto.Date) ?? throw new FormatException("Sejm voting day has no date."),
        dto.Proceeding ?? throw new FormatException("Sejm voting day has no sitting."),
        dto.VotingsNum ?? 0);

    public static SejmVoting ToVoting(SejmVotingDto dto) => new(
        dto.Term ?? throw new FormatException("Sejm voting has no term."),
        dto.Sitting ?? throw new FormatException("Sejm voting has no sitting."),
        dto.SittingDay ?? 0,
        dto.VotingNumber ?? throw new FormatException("Sejm voting has no number."),
        DateTimeValue(dto.Date) ?? throw new FormatException("Sejm voting has no date."),
        dto.Title ?? string.Empty,
        Text(dto.Topic),
        Text(dto.Description),
        dto.Kind ?? string.Empty,
        dto.MajorityType ?? string.Empty,
        dto.MajorityVotes,
        dto.Yes,
        dto.No,
        dto.Abstain,
        dto.Present,
        dto.NotParticipating,
        dto.TotalVoted,
        dto.AgainstAll,
        (dto.VotingOptions ?? []).Where(static o => o is not null).Select(static o => new SejmVotingOption(o.OptionIndex ?? 0, o.Option ?? string.Empty, o.Votes)).ToList());

    public static IReadOnlyList<SejmMpVote> ToMpVotes(List<SejmMpVoteDto>? dtos) =>
        (dtos ?? []).Where(static v => v is not null).Select(static v => new SejmMpVote(
            v.Mp ?? throw new FormatException("Sejm vote has no deputy id."),
            FullName(v.FirstName, v.SecondName, v.LastName),
            Text(v.Club),
            v.Vote ?? string.Empty)).ToList();

    public static SejmClub ToClub(SejmClubDto dto, bool includeMembers)
    {
        var members = (dto.Members ?? []).Where(static m => m is not null).ToList();
        return new SejmClub(
            Required(dto.Id, "club id"),
            dto.Name ?? string.Empty,
            dto.MembersCount > 0 ? dto.MembersCount : members.Count,
            includeMembers ? members.Select(ToMember).ToList() : []);
    }

    public static SejmCommittee ToCommittee(SejmCommitteeDto dto, bool includeMembers)
    {
        var members = (dto.Members ?? []).Where(static m => m is not null).ToList();
        var composition = members
            .GroupBy(static m => string.IsNullOrWhiteSpace(m.Club) ? NoClub : m.Club!.Trim(), StringComparer.Ordinal)
            .Select(static g => new SejmClubSeats(g.Key, g.Count()))
            .OrderByDescending(static c => c.Seats)
            .ThenBy(static c => c.Club, StringComparer.Ordinal)
            .ToList();
        return new SejmCommittee(
            Required(dto.Code, "committee code"),
            dto.Name ?? string.Empty,
            dto.Type ?? string.Empty,
            Text(dto.Scope),
            Date(dto.AppointmentDate),
            Date(dto.DismissalDate),
            Texts(dto.SubCommittees),
            composition,
            includeMembers ? members.Select(ToMember).ToList() : []);
    }

    private static SejmMember ToMember(SejmMemberDto dto) => new(
        dto.Id ?? throw new FormatException("Sejm member has no id."),
        dto.FirstName ?? string.Empty,
        dto.LastName ?? string.Empty,
        Text(dto.Club),
        Text(dto.Function),
        Date(dto.JoinDate));

    private static List<SejmStage> Stages(List<SejmStageDto>? dtos) =>
        (dtos ?? []).Where(static s => s is not null).Select(ToStage).ToList();

    private static SejmStage ToStage(SejmStageDto dto) => new(
        dto.StageType ?? string.Empty,
        dto.StageName ?? string.Empty,
        Date(dto.Date),
        // a Senate stage carries its outcome as "position"; every other stage as "decision"
        Text(dto.Decision) ?? Text(dto.Position),
        Text(dto.Comment),
        dto.SittingNum,
        Text(dto.CommitteeCode),
        Text(dto.PrintNumber),
        dto.Voting is null ? null : ToVoting(dto.Voting),
        Uri(dto.TextAfter3),
        Stages(dto.Children));

    private static string FullName(string? first, string? second, string? last) =>
        string.Join(" ", new[] { first, second, last }.Where(static p => !string.IsNullOrWhiteSpace(p)).Select(static p => p!.Trim()));

    private static string Required(string? text, string what) =>
        string.IsNullOrWhiteSpace(text) ? throw new FormatException("Sejm " + what + " is missing.") : text!.Trim();

    private static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text!.Trim();

    private static List<string> Texts(List<string>? list) =>
        list is null ? [] : list.Where(static t => !string.IsNullOrWhiteSpace(t)).Select(static t => t.Trim()).ToList();

    private static Uri? Uri(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null
        : System.Uri.TryCreate(text!.Trim(), UriKind.Absolute, out var uri) ? uri
        : throw new FormatException("Sejm link is not an absolute URI.");

    private static DateOnly? Date(string? text) =>
        string.IsNullOrEmpty(text) ? null
        : DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : throw new FormatException("Sejm date is not in the format yyyy-MM-dd.");

    // Date-times carry no offset (Warsaw local time): kept as Unspecified.
    private static DateTime? DateTimeValue(string? text) =>
        string.IsNullOrEmpty(text) ? null
        : DateTime.TryParseExact(text, ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified)
            : throw new FormatException("Sejm date-time is not in the format yyyy-MM-ddTHH:mm:ss.");
}
