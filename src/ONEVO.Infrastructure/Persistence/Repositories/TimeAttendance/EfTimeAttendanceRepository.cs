using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public sealed class EfTimeAttendanceRepository : ITimeAttendanceRepository
{
    private readonly ApplicationDbContext _db;

    public EfTimeAttendanceRepository(ApplicationDbContext db) => _db = db;

    public Task<WorkAreaChangeRequest?> GetApprovedWorkAreaChangeAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        _db.WorkAreaChangeRequests
            .AsNoTracking()
            .OrderByDescending(request => request.ReviewedAt)
            .FirstOrDefaultAsync(
                request => request.EmployeeId == employeeId &&
                           request.Date == date &&
                           request.Status == "approved",
                ct);

    public Task<ScheduleAssignment?> ResolveScheduleAssignmentAsync(
        Employee employee, DateOnly date, CancellationToken ct)
    {
        var legalEntityId = employee.LegalEntityId;
        var departmentId = employee.DepartmentId;
        var positionId = employee.JobTitleId;

        return _db.ScheduleAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.LegalEntityId == legalEntityId &&
                assignment.EffectiveFrom <= date &&
                (assignment.EffectiveTo == null || assignment.EffectiveTo >= date) &&
                (
                    (assignment.AssignmentType == "employee" &&
                     assignment.EmployeeId == employee.Id) ||
                    (assignment.AssignmentType == "position" &&
                     positionId != null &&
                     assignment.PositionId == positionId) ||
                    (assignment.AssignmentType == "department" &&
                     departmentId != null &&
                     assignment.DepartmentId == departmentId) ||
                    assignment.AssignmentType == "full_company"
                ))
            .OrderBy(assignment =>
                assignment.AssignmentType == "employee" ? 0 :
                assignment.AssignmentType == "position" ? 1 :
                assignment.AssignmentType == "department" ? 2 : 3)
            .ThenByDescending(assignment => assignment.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
    }

    public Task<WorkSchedule?> GetScheduleAsync(Guid id, CancellationToken ct) =>
        _db.WorkSchedules
            .AsNoTracking()
            .SingleOrDefaultAsync(schedule => schedule.Id == id && schedule.IsActive, ct);

    public Task<WorkScheduleDay?> GetScheduleDayAsync(
        Guid scheduleId, short dayOfWeek, CancellationToken ct) =>
        _db.WorkScheduleDays
            .AsNoTracking()
            .SingleOrDefaultAsync(
                day => day.WorkScheduleId == scheduleId && day.DayOfWeek == dayOfWeek,
                ct);

    public Task<WorkScheduleHoliday?> GetScheduleHolidayAsync(
        Guid scheduleId, DateOnly date, CancellationToken ct) =>
        _db.WorkScheduleHolidays
            .AsNoTracking()
            .SingleOrDefaultAsync(
                holiday => holiday.WorkScheduleId == scheduleId && holiday.Date == date,
                ct);

    public Task<ClockInPolicy?> ResolveClockInPolicyAsync(
        Employee employee,
        Guid legalEntityId,
        DateOnly date,
        CancellationToken ct)
    {
        var departmentId = employee.DepartmentId;
        var positionId = employee.JobTitleId;

        return _db.ClockInPolicies
            .AsNoTracking()
            .Where(policy =>
                policy.LegalEntityId == legalEntityId &&
                policy.IsActive &&
                policy.EffectiveFrom <= date &&
                (policy.EffectiveTo == null || policy.EffectiveTo >= date) &&
                (
                    (policy.ScopeType == "employee" &&
                     policy.EmployeeIds != null &&
                     policy.EmployeeIds.Contains(employee.Id)) ||
                    (policy.ScopeType == "position" &&
                     positionId != null &&
                     policy.PositionIds != null &&
                     policy.PositionIds.Contains(positionId.Value)) ||
                    (policy.ScopeType == "department" &&
                     departmentId != null &&
                     policy.DepartmentIds != null &&
                     policy.DepartmentIds.Contains(departmentId.Value)) ||
                    policy.ScopeType == "full_company"
                ))
            .OrderBy(policy =>
                policy.ScopeType == "employee" ? 0 :
                policy.ScopeType == "position" ? 1 :
                policy.ScopeType == "department" ? 2 : 3)
            .ThenByDescending(policy => policy.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
    }

    public Task<AttendanceRecord?> GetAttendanceAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        _db.AttendanceRecords.SingleOrDefaultAsync(
            record => record.EmployeeId == employeeId && record.Date == date,
            ct);

    public Task<PresenceSession?> GetPresenceAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        _db.PresenceSessions.SingleOrDefaultAsync(
            session => session.EmployeeId == employeeId && session.Date == date,
            ct);

    public Task<DeviceSession?> GetOpenDeviceSessionAsync(
        Guid agentId, CancellationToken ct) =>
        _db.DeviceSessions.SingleOrDefaultAsync(
            session => session.DeviceId == agentId && session.SessionEnd == null,
            ct);

    public Task<BreakRecord?> GetOpenBreakAsync(Guid employeeId, CancellationToken ct) =>
        _db.BreakRecords.SingleOrDefaultAsync(
            record => record.EmployeeId == employeeId && record.BreakEnd == null,
            ct);

    public async Task AddAttendanceAsync(AttendanceRecord record, CancellationToken ct) =>
        await _db.AttendanceRecords.AddAsync(record, ct);

    public async Task AddPresenceAsync(PresenceSession session, CancellationToken ct) =>
        await _db.PresenceSessions.AddAsync(session, ct);

    public async Task AddDeviceSessionAsync(DeviceSession session, CancellationToken ct) =>
        await _db.DeviceSessions.AddAsync(session, ct);

    public async Task AddBreakAsync(BreakRecord record, CancellationToken ct) =>
        await _db.BreakRecords.AddAsync(record, ct);

    public Task<WorkAreaChangeRequest?> GetPendingWorkAreaChangeAsync(
        Guid employeeId, DateOnly date, CancellationToken ct) =>
        _db.WorkAreaChangeRequests.SingleOrDefaultAsync(
            request => request.EmployeeId == employeeId &&
                       request.Date == date &&
                       request.Status == "pending",
            ct);

    public Task<WorkAreaChangeRequest?> GetWorkAreaChangeAsync(
        Guid id, CancellationToken ct) =>
        _db.WorkAreaChangeRequests.SingleOrDefaultAsync(request => request.Id == id, ct);

    public async Task AddWorkAreaChangeAsync(
        WorkAreaChangeRequest request, CancellationToken ct) =>
        await _db.WorkAreaChangeRequests.AddAsync(request, ct);
}
