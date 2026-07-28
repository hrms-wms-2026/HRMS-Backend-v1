using ONEVO.Application.Features.AgentGateway.Location;

namespace ONEVO.Application.Features.TimeAttendance.Context;

public sealed record ResolvedClockInContext(
    Guid TenantId,
    Guid AgentId,
    Guid EmployeeId,
    Guid LegalEntityId,
    DateOnly WorkDate,
    string Timezone,
    Guid? WorkScheduleId,
    string? WorkScheduleName,
    TimeOnly? ScheduledStart,
    TimeOnly? ScheduledEnd,
    int? RequiredWorkMinutes,
    string ExpectedWorkArea,
    string WorkAreaSource,
    Guid ClockInPolicyId,
    bool LocationRequired,
    IReadOnlyList<LocationTarget> LocationTargets,
    bool PhotoRequired,
    bool ReferenceReady,
    bool RemoteProfileReady,
    bool IsHoliday,
    bool IsWorkingDay,
    bool IsClockedIn,
    bool CanClockIn,
    string ReasonCode,
    DateTimeOffset? MonitoringHardStopAt);

