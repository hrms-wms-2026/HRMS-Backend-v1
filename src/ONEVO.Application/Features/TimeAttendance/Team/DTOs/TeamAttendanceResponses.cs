namespace ONEVO.Application.Features.TimeAttendance.Team.DTOs;

/// <summary>Employee-day counts per bucket (TeamAttendanceBuckets). Off days are never counted.</summary>
public sealed record TeamAttendanceCounts(int Present, int Late, int Absent, int OnLeave, int Pending);

public sealed record TeamAttendanceTrendPoint(DateOnly Date, int Present, int Late, int Absent, int OnLeave, int Pending, decimal? Rate);

public sealed record TeamAttendancePreviewRow(
    Guid EmployeeId, string Name, Guid? AvatarFileId, string? Department, string Bucket, DateTimeOffset? ClockInAt);

public sealed record TeamAttendanceDepartment(Guid Id, string Name);

/// <summary>
/// The Team attendance widget. Today* fields are today's snapshot (Team Status's engine); Period*, Trend and the
/// comparison cover the selected week or month. Rates are (Present + Late) / (Present + Late + Absent) as a
/// percentage, null when nothing was expected; DeltaPp is in percentage points.
/// </summary>
public sealed record TeamAttendanceSummaryResponse(
    string Mode,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    string Timezone,
    DateOnly Today,
    int EligibleToday,
    TeamAttendanceCounts TodayCounts,
    decimal? TodayRate,
    TeamAttendanceCounts PeriodCounts,
    decimal? PeriodRate,
    decimal? PreviousRate,
    decimal? DeltaPp,
    IReadOnlyList<TeamAttendanceTrendPoint> Trend,
    IReadOnlyList<TeamAttendancePreviewRow> TodayPreview,
    IReadOnlyList<TeamAttendanceDepartment> Departments,
    int TeamSize,
    DateTimeOffset CalculatedAtUtc,
    bool IsProvisional);

public sealed record TeamAttendanceDayCell(DateOnly Date, string Bucket);

public sealed record TeamAttendanceEmployeeRow(
    Guid EmployeeId,
    string Name,
    Guid? AvatarFileId,
    string? Department,
    IReadOnlyList<TeamAttendanceDayCell> Days,
    int Attended,
    int EligibleDays,
    decimal? Rate);
