namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

public sealed record ActivityHourBucket(int Hour, int ActiveMinutes);

/// <summary>Hours are 0..23 in Timezone (the employee's legal-entity zone, IANA/Windows id or "UTC").</summary>
public sealed record EmployeeActivityByHourResponse(
    DateOnly From,
    DateOnly To,
    bool ActivityMonitoringEnabled,
    string Timezone,
    DateTimeOffset? LastActivityAt,
    IReadOnlyList<ActivityHourBucket> Hours);

public sealed record EmployeeAppUsageItem(string AppName, int ActiveMinutes, int Sessions, DateTimeOffset LastUsedAt);

public sealed record EmployeeAppUsageResponse(
    DateOnly From,
    DateOnly To,
    bool ApplicationTrackingEnabled,
    int TotalMinutes,
    IReadOnlyList<EmployeeAppUsageItem> Apps);

public sealed record EmployeeIdlePeriod(DateTimeOffset Start, DateTimeOffset End, int Minutes);

public sealed record EmployeeWorkPatternSummaryResponse(
    DateOnly From,
    DateOnly To,
    bool ActivityMonitoringEnabled,
    int FocusBlocks,
    string? TopApp,
    int TopAppMinutes,
    EmployeeIdlePeriod? LongestIdle);
