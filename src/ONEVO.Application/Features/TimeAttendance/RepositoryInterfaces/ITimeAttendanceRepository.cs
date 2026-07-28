using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

public interface ITimeAttendanceRepository
{
    Task<WorkAreaChangeRequest?> GetApprovedWorkAreaChangeAsync(
        Guid employeeId, DateOnly date, CancellationToken ct);
    Task<ScheduleAssignment?> ResolveScheduleAssignmentAsync(
        Employee employee, DateOnly date, CancellationToken ct);
    Task<WorkSchedule?> GetScheduleAsync(Guid id, CancellationToken ct);
    Task<WorkScheduleDay?> GetScheduleDayAsync(
        Guid scheduleId, short dayOfWeek, CancellationToken ct);
    Task<WorkScheduleHoliday?> GetScheduleHolidayAsync(
        Guid scheduleId, DateOnly date, CancellationToken ct);
    Task<ClockInPolicy?> ResolveClockInPolicyAsync(
        Employee employee, Guid legalEntityId, DateOnly date, CancellationToken ct);
    Task<AttendanceRecord?> GetAttendanceAsync(
        Guid employeeId, DateOnly date, CancellationToken ct);
    Task<PresenceSession?> GetPresenceAsync(
        Guid employeeId, DateOnly date, CancellationToken ct);
    Task<DeviceSession?> GetOpenDeviceSessionAsync(Guid agentId, CancellationToken ct);
    Task<BreakRecord?> GetOpenBreakAsync(Guid employeeId, CancellationToken ct);
    Task AddAttendanceAsync(AttendanceRecord record, CancellationToken ct);
    Task AddPresenceAsync(PresenceSession session, CancellationToken ct);
    Task AddDeviceSessionAsync(DeviceSession session, CancellationToken ct);
    Task AddBreakAsync(BreakRecord record, CancellationToken ct);
    Task<WorkAreaChangeRequest?> GetPendingWorkAreaChangeAsync(
        Guid employeeId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<WorkAreaChangeRequest>> GetPendingWorkAreaChangesAsync(
        int skip, int take, CancellationToken ct);
    Task<WorkAreaChangeRequest?> GetWorkAreaChangeAsync(Guid id, CancellationToken ct);
    Task AddWorkAreaChangeAsync(WorkAreaChangeRequest request, CancellationToken ct);
}
