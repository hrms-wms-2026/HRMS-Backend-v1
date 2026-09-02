using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public interface IAttendanceTodayStateService
{
    Task<Result<AttendanceTodayResponse>> GetTodayAsync(CancellationToken ct = default);

    Task<Result<AttendanceTodayContext>> ResolveContextAsync(CancellationToken ct = default);
}

public sealed record AttendanceTodayContext(
    Employee Employee,
    LegalEntity LegalEntity,
    string Timezone,
    TimeZoneInfo TimeZone,
    DateOnly WorkDate,
    DateTimeOffset UtcNow,
    DateTimeOffset LocalNow,
    AttendanceSchedule Schedule,
    string ExpectedWorkArea,
    string ExpectedWorkAreaSource,
    ClockInPolicy? EffectivePolicy,
    string PolicyStatus,
    AllowedClockInMethods AllowedClockInMethods,
    AttendanceLocalDayWindow LocalDayWindow);

public sealed record AttendanceSchedule(
    string Status,
    bool IsWorkingDay,
    TimeOnly? Start,
    TimeOnly? End,
    int? RequiredWorkMinutes);

public sealed record AttendanceLocalDayWindow(DateTimeOffset Start, DateTimeOffset End);
