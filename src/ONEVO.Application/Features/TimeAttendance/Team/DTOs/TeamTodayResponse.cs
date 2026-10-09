namespace ONEVO.Application.Features.TimeAttendance.Team.DTOs;

/// <summary>My Team's Team Status widget (spec §8.1.3). summary covers the whole covered
/// population; members is capped at the request's limit and ordered per §8.1.2 rule 7.</summary>
public sealed record TeamTodayResponse(
    DateOnly WorkDate,
    string Timezone,
    Guid LegalEntityId,
    TeamTodaySummary Summary,
    IReadOnlyList<TeamTodayMember> Members,
    int TotalMembers);

public sealed record TeamTodaySummary(
    int Total,
    int Working,
    int OnBreak,
    int ClockedOut,
    int Late,
    int Absent,
    int NotStarted,
    int OnLeave,
    int NotScheduled,
    int NeedsAttention);

public sealed record TeamTodayMember(
    Guid EmployeeId,
    string DisplayName,
    Guid? AvatarFileId,
    string? PositionTitle,
    string Status,
    string StatusLabel,
    bool IsLate,
    DateTimeOffset? ClockInAt,
    DateTimeOffset? ClockOutAt,
    string? AttentionType,
    string? AttentionLabel,
    string? AttentionSeverity,
    TeamTodayLeave? Leave);

/// <summary>Present only when the mapper's ShowLeaveDetail is true - i.e. only for a subject the
/// caller is Leave-authorized for (spec §8.1.2 rule 5 / D6 masking).</summary>
public sealed record TeamTodayLeave(string LeaveTypeName, DateOnly EndsOn);
