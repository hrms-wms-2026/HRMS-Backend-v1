namespace ONEVO.Application.Features.TimeAttendance.Team.DTOs;

/// <summary>My Team's Team Status widget (spec §8.1.3), shown as Working / Break / Leave since the 2026-10-29 My Team
/// design. summary covers the whole covered population; members is capped at the request's limit and ordered per §8.1.2
/// rule 7. AsOf = the server time the statuses were resolved at.</summary>
public sealed record TeamTodayResponse(
    DateOnly WorkDate,
    string Timezone,
    Guid LegalEntityId,
    TeamTodaySummary Summary,
    IReadOnlyList<TeamTodayMember> Members,
    int TotalMembers,
    DateTimeOffset AsOf);

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

/// <param name="StateSince">Working: clock-in; on break: the open break's start; otherwise null.</param>
/// <param name="BreakRemainingMinutes">On break with a configured allowance only.</param>
/// <param name="ShiftEndsAt">Working or on break with a scheduled end only.</param>
/// <param name="AttendanceMinutes">PlannerActuals.AttendanceMinutes of today's record; 0 without one.</param>
/// <param name="ExpectedMinutes">The legal entity's work window for the date; 0 on leave, null without working hours.</param>
/// <param name="Breaks">Today's break intervals ordered by start; an open break has a null End.</param>
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
    TeamTodayLeave? Leave,
    DateTimeOffset? StateSince = null,
    int? BreakRemainingMinutes = null,
    DateTimeOffset? ShiftEndsAt = null,
    int AttendanceMinutes = 0,
    int? ExpectedMinutes = null,
    IReadOnlyList<TeamTodayBreak>? Breaks = null);

public sealed record TeamTodayBreak(DateTimeOffset Start, DateTimeOffset? End);

/// <summary>Present only when the mapper's ShowLeaveDetail is true - i.e. only for a subject the
/// caller is Leave-authorized for (spec §8.1.2 rule 5 / D6 masking).</summary>
public sealed record TeamTodayLeave(string LeaveTypeName, DateOnly EndsOn);
