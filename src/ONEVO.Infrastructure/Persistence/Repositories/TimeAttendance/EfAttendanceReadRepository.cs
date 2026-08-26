using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public sealed class EfAttendanceReadRepository(ApplicationDbContext db) : IAttendanceReadRepository
{
    public Task<AttendanceRecord?> GetRecordAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct = default)
        => db.AttendanceRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Date == date,
                ct);

    public Task<AttendanceRecord?> GetTrackedRecordAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct = default)
        => db.AttendanceRecords
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Date == date,
                ct);

    public async Task<IReadOnlyList<AttendanceRecord>> ListRecordsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
        => await db.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && employeeIds.Contains(x.EmployeeId)
                && x.Date >= from
                && x.Date <= to)
            .OrderByDescending(x => x.Date)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BreakRecord>> ListBreaksAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
        => await db.BreakRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && x.EmployeeId == employeeId
                && x.BreakStart < to
                && (x.BreakEnd == null || x.BreakEnd > from))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BreakRecord>> ListBreaksForEmployeesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return Array.Empty<BreakRecord>();

        return await db.BreakRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && employeeIds.Contains(x.EmployeeId)
                && x.BreakStart < to
                && (x.BreakEnd == null || x.BreakEnd > from))
            .ToListAsync(ct);
    }

    public Task<bool> HasOpenBreakAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
        => db.BreakRecords
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId
                && x.EmployeeId == employeeId
                && x.BreakStart < to
                && x.BreakEnd == null,
                ct);

    public Task<BreakRecord?> GetOpenBreakTrackedAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
        => db.BreakRecords
            .Where(x => x.TenantId == tenantId
                && x.EmployeeId == employeeId
                && x.BreakStart < to
                && x.BreakEnd == null
                && x.BreakStart >= from)
            .OrderByDescending(x => x.BreakStart)
            .FirstOrDefaultAsync(ct);

    public Task<BreakRecord?> GetAnyOpenBreakTrackedAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct = default)
        => db.BreakRecords
            .Where(x => x.TenantId == tenantId
                && x.EmployeeId == employeeId
                && x.BreakEnd == null)
            .OrderByDescending(x => x.BreakStart)
            .FirstOrDefaultAsync(ct);

    public async Task<int> SumCompletedBreakMinutesAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var breaks = await db.BreakRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && x.EmployeeId == employeeId
                && x.BreakEnd != null
                && x.BreakStart < to
                && x.BreakEnd > from)
            .Select(x => new { x.BreakStart, x.BreakEnd })
            .ToListAsync(ct);

        return breaks.Sum(b =>
        {
            var start = b.BreakStart < from ? from : b.BreakStart;
            var end = b.BreakEnd!.Value > to ? to : b.BreakEnd.Value;
            return end <= start ? 0 : (int)Math.Max(0, (end - start).TotalMinutes);
        });
    }

    public Task AddRecordAsync(AttendanceRecord record, CancellationToken ct = default)
        => db.AttendanceRecords.AddAsync(record, ct).AsTask();

    public Task AddBreakAsync(BreakRecord record, CancellationToken ct = default)
        => db.BreakRecords.AddAsync(record, ct).AsTask();

    public Task DeleteBreakAsync(Guid breakId, CancellationToken ct = default)
        => db.BreakRecords.Where(x => x.Id == breakId).ExecuteDeleteAsync(ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new UniqueConstraintConflictException(ex);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, AttendanceHistoryEmployee>> ListEmployeeIdentitiesAsync(
        Guid tenantId,
        Guid legalEntityId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0) return new Dictionary<Guid, AttendanceHistoryEmployee>();

        var activeAssignments = db.PositionAssignments.AsNoTracking().Where(x =>
            x.TenantId == tenantId
            && x.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
            && x.AssignmentStatus == PositionAssignmentStatus.Active);

        var rows = await (from employee in db.Employees.AsNoTracking()
                          where employee.TenantId == tenantId
                              && employee.LegalEntityId == legalEntityId
                              && employeeIds.Contains(employee.Id)
                          join department in db.Departments.AsNoTracking()
                              on employee.DepartmentId equals department.Id into departmentJoin
                          from department in departmentJoin.DefaultIfEmpty()
                          join assignment in activeAssignments
                              on employee.Id equals assignment.EmployeeId into assignmentJoin
                          from assignment in assignmentJoin.DefaultIfEmpty()
                          join position in db.Positions.AsNoTracking()
                              on assignment!.PositionId equals position.Id into positionJoin
                          from position in positionJoin.DefaultIfEmpty()
                          select new AttendanceHistoryEmployee(
                              employee.Id,
                              employee.FirstName + " " + employee.LastName,
                              employee.EmployeeNumber,
                              position == null ? null : position.Name,
                              department == null ? null : department.Name,
                              employee.AvatarFileId)).ToListAsync(ct);

        return rows.GroupBy(x => x.EmployeeId).ToDictionary(x => x.Key, x => x.First());
    }
}
