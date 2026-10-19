using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

/// <summary>Everything the attendance Overview cards need for one employee and period, loaded once.</summary>
public sealed record AttendancePeriodData(
    IReadOnlyList<AttendanceRecord> Records,
    TimeZoneInfo Timezone,
    DateTimeOffset Now,
    DateOnly Today,
    int? BreakAllowanceMinutes,
    IReadOnlyDictionary<DateOnly, int> BreakMinutesByDate,
    IReadOnlyList<LeaveRequest> ApprovedLeaves,
    DateTimeOffset RangeStartUtc,
    DateTimeOffset RangeEndUtc,
    ExpectedWorkdays Workdays,
    // Per alert signal, the dates whose alert was resolved as excused or converted to leave (plan E1).
    // Null = nothing excused, as in the raw classification the alert materialiser runs on.
    IReadOnlyDictionary<string, IReadOnlySet<DateOnly>>? Excused = null);

public interface IEmployeeAttendancePeriodReader
{
    Task<AttendancePeriodData> LoadAsync(
        Guid tenantId, Guid employeeId, Guid? legalEntityId, EmployeePeriod period, CancellationToken ct = default);
}
