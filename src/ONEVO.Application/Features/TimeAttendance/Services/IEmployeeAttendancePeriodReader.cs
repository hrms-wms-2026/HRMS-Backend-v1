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
    IReadOnlySet<int>? WorkingWeekdays = null);

public interface IEmployeeAttendancePeriodReader
{
    Task<AttendancePeriodData> LoadAsync(
        Guid tenantId, Guid employeeId, Guid? legalEntityId, EmployeePeriod period, CancellationToken ct = default);
}
