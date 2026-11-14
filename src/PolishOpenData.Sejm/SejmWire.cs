using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PolishOpenData.Sejm;

// Wire DTOs mirror the live API. Optional fields are omitted by the API (not null); dates stay strings and are parsed by the mapper so a bad
// value becomes a FormatException (an unreadable response), not a silent default. Property names bind case-insensitively (Web defaults), so
// ELI and MP (upper case on the wire) bind to Eli and Mp. Personal fields that no model uses (rapporteurName, rapporteurID, option
// description) are deliberately not declared.

internal sealed class SejmTermDto
{
    public int? Num { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public bool Current { get; set; }
}

internal sealed class SejmProcessDto
{
    public int? Term { get; set; }

    public string? Number { get; set; }

    public string? Title { get; set; }

    public string? TitleFinal { get; set; }

    public string? DocumentType { get; set; }

    public string? DocumentDate { get; set; }

    public string? ProcessStartDate { get; set; }

    public string? ClosureDate { get; set; }

    public string? ChangeDate { get; set; }

    public bool Passed { get; set; }

    public string? UrgencyStatus { get; set; }

    public string? Eli { get; set; }

    public string? DisplayAddress { get; set; }

    public List<string>? PrintsConsideredJointly { get; set; }

    public string? Comments { get; set; }

    public string? RclNum { get; set; }

    public string? RclLink { get; set; }

    public List<SejmStageDto>? Stages { get; set; }
}

internal sealed class SejmStageDto
{
    public string? StageType { get; set; }

    public string? StageName { get; set; }

    public string? Date { get; set; }

    public string? Decision { get; set; }

    public string? Position { get; set; }

    public string? Comment { get; set; }

    public int? SittingNum { get; set; }

    public string? CommitteeCode { get; set; }

    public string? PrintNumber { get; set; }

    public SejmVotingDto? Voting { get; set; }

    public string? TextAfter3 { get; set; }

    public List<SejmStageDto>? Children { get; set; }
}

internal sealed class SejmPrintDto
{
    public int? Term { get; set; }

    public string? Number { get; set; }

    public string? Title { get; set; }

    public string? DocumentDate { get; set; }

    public string? DeliveryDate { get; set; }

    public List<string>? ProcessPrint { get; set; }

    public List<string>? Attachments { get; set; }

    public List<SejmPrintDto>? AdditionalPrints { get; set; }
}

internal sealed class SejmBillDto
{
    public string? Number { get; set; }

    public string? Print { get; set; }

    public string? Title { get; set; }

    public string? SubmissionType { get; set; }

    public string? ApplicantType { get; set; }

    public string? DateOfReceipt { get; set; }

    public bool EuRelated { get; set; }

    public bool PublicConsultation { get; set; }
}

internal sealed class SejmVotingDayDto
{
    public string? Date { get; set; }

    public int? Proceeding { get; set; }

    public int? VotingsNum { get; set; }
}

internal sealed class SejmVotingDto
{
    public int? Term { get; set; }

    public int? Sitting { get; set; }

    public int? SittingDay { get; set; }

    public int? VotingNumber { get; set; }

    public string? Date { get; set; }

    public string? Title { get; set; }

    public string? Topic { get; set; }

    public string? Description { get; set; }

    public string? Kind { get; set; }

    public string? MajorityType { get; set; }

    public int MajorityVotes { get; set; }

    public int Yes { get; set; }

    public int No { get; set; }

    public int Abstain { get; set; }

    public int Present { get; set; }

    public int NotParticipating { get; set; }

    public int TotalVoted { get; set; }

    public int? AgainstAll { get; set; }

    public List<SejmVotingOptionDto>? VotingOptions { get; set; }

    public List<SejmMpVoteDto>? Votes { get; set; }
}

internal sealed class SejmVotingOptionDto
{
    public int? OptionIndex { get; set; }

    public string? Option { get; set; }

    public int Votes { get; set; }
}

internal sealed class SejmMpVoteDto
{
    public int? Mp { get; set; }

    public string? Club { get; set; }

    public string? FirstName { get; set; }

    public string? SecondName { get; set; }

    public string? LastName { get; set; }

    public string? Vote { get; set; }
}

internal sealed class SejmMemberDto
{
    public int? Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Club { get; set; }

    public string? Function { get; set; }

    public string? JoinDate { get; set; }
}

internal sealed class SejmClubDto
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public int MembersCount { get; set; }

    public List<SejmMemberDto>? Members { get; set; }
}

internal sealed class SejmCommitteeDto
{
    public string? Code { get; set; }

    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? Scope { get; set; }

    public string? AppointmentDate { get; set; }

    public string? DismissalDate { get; set; }

    public List<string>? SubCommittees { get; set; }

    public List<SejmMemberDto>? Members { get; set; }
}

#if NET
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<SejmTermDto>))]
[JsonSerializable(typeof(List<SejmProcessDto>))]
[JsonSerializable(typeof(SejmProcessDto))]
[JsonSerializable(typeof(SejmPrintDto))]
[JsonSerializable(typeof(List<SejmBillDto>))]
[JsonSerializable(typeof(List<SejmVotingDayDto>))]
[JsonSerializable(typeof(List<SejmVotingDto>))]
[JsonSerializable(typeof(SejmVotingDto))]
[JsonSerializable(typeof(List<SejmClubDto>))]
[JsonSerializable(typeof(SejmClubDto))]
[JsonSerializable(typeof(List<SejmCommitteeDto>))]
[JsonSerializable(typeof(SejmCommitteeDto))]
internal sealed partial class SejmJsonContext : JsonSerializerContext;
#endif

/// <summary>Type infos: source-generated on .NET, reflection on netstandard2.0.</summary>
internal static class SejmJson
{
#if NET
    public static JsonTypeInfo<List<SejmTermDto>> Terms => SejmJsonContext.Default.ListSejmTermDto;

    public static JsonTypeInfo<List<SejmProcessDto>> Processes => SejmJsonContext.Default.ListSejmProcessDto;

    public static JsonTypeInfo<SejmProcessDto> Process => SejmJsonContext.Default.SejmProcessDto;

    public static JsonTypeInfo<SejmPrintDto> Print => SejmJsonContext.Default.SejmPrintDto;

    public static JsonTypeInfo<List<SejmBillDto>> Bills => SejmJsonContext.Default.ListSejmBillDto;

    public static JsonTypeInfo<List<SejmVotingDayDto>> VotingDays => SejmJsonContext.Default.ListSejmVotingDayDto;

    public static JsonTypeInfo<List<SejmVotingDto>> Votings => SejmJsonContext.Default.ListSejmVotingDto;

    public static JsonTypeInfo<SejmVotingDto> Voting => SejmJsonContext.Default.SejmVotingDto;

    public static JsonTypeInfo<List<SejmClubDto>> Clubs => SejmJsonContext.Default.ListSejmClubDto;

    public static JsonTypeInfo<SejmClubDto> Club => SejmJsonContext.Default.SejmClubDto;

    public static JsonTypeInfo<List<SejmCommitteeDto>> Committees => SejmJsonContext.Default.ListSejmCommitteeDto;

    public static JsonTypeInfo<SejmCommitteeDto> Committee => SejmJsonContext.Default.SejmCommitteeDto;
#else
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    public static JsonTypeInfo<List<SejmTermDto>> Terms { get; } = Info<List<SejmTermDto>>();

    public static JsonTypeInfo<List<SejmProcessDto>> Processes { get; } = Info<List<SejmProcessDto>>();

    public static JsonTypeInfo<SejmProcessDto> Process { get; } = Info<SejmProcessDto>();

    public static JsonTypeInfo<SejmPrintDto> Print { get; } = Info<SejmPrintDto>();

    public static JsonTypeInfo<List<SejmBillDto>> Bills { get; } = Info<List<SejmBillDto>>();

    public static JsonTypeInfo<List<SejmVotingDayDto>> VotingDays { get; } = Info<List<SejmVotingDayDto>>();

    public static JsonTypeInfo<List<SejmVotingDto>> Votings { get; } = Info<List<SejmVotingDto>>();

    public static JsonTypeInfo<SejmVotingDto> Voting { get; } = Info<SejmVotingDto>();

    public static JsonTypeInfo<List<SejmClubDto>> Clubs { get; } = Info<List<SejmClubDto>>();

    public static JsonTypeInfo<SejmClubDto> Club { get; } = Info<SejmClubDto>();

    public static JsonTypeInfo<List<SejmCommitteeDto>> Committees { get; } = Info<List<SejmCommitteeDto>>();

    public static JsonTypeInfo<SejmCommitteeDto> Committee { get; } = Info<SejmCommitteeDto>();
#endif
}
