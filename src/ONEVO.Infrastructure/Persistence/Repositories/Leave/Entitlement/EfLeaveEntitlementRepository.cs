using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Entitlement;

public class EfLeaveEntitlementRepository : ILeaveEntitlementRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeaveEntitlementRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LeaveEntitlementRow>> ListRowsAsync(
        Guid tenantId,
        LeaveEntitlementListFilter filter,
        CancellationToken ct = default)
    {
        if (filter.EmployeeIds is { Count: 0 })
            return [];

        var query =
            from entitlement in _db.LeaveEntitlements.AsNoTracking()
            join employee in _db.Employees.AsNoTracking() on entitlement.EmployeeId equals employee.Id
            join leaveType in _db.LeaveTypes.AsNoTracking() on entitlement.LeaveTypeId equals leaveType.Id
            join department in _db.Departments.AsNoTracking() on employee.DepartmentId equals department.Id into departments
            from department in departments.DefaultIfEmpty()
            join legalEntity in _db.LegalEntities.AsNoTracking() on employee.LegalEntityId equals legalEntity.Id into legalEntities
            from legalEntity in legalEntities.DefaultIfEmpty()
            where entitlement.TenantId == tenantId && entitlement.Year == filter.Year
            select new { entitlement, employee, leaveType, department, legalEntity };

        if (filter.EmployeeId is { } employeeId)
            query = query.Where(x => x.entitlement.EmployeeId == employeeId);
        if (filter.EmployeeIds is { } employeeIds)
            query = query.Where(x => employeeIds.Contains(x.entitlement.EmployeeId));
        if (filter.LegalEntityId is { } legalEntityId)
            query = query.Where(x => x.employee.LegalEntityId == legalEntityId);
        if (filter.DepartmentId is { } departmentId)
            query = query.Where(x => x.employee.DepartmentId == departmentId);
        if (filter.LeaveTypeId is { } leaveTypeId)
            query = query.Where(x => x.entitlement.LeaveTypeId == leaveTypeId);
        if (filter.EmploymentStatusId is { } statusId)
            query = query.Where(x => x.employee.EmploymentStatusId == statusId);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(x =>
                x.employee.FirstName.ToLower().Contains(search) ||
                x.employee.LastName.ToLower().Contains(search) ||
                x.employee.EmployeeNumber.ToLower().Contains(search));
        }

        var rows = await query
            .OrderBy(x => x.employee.FirstName)
            .ThenBy(x => x.employee.LastName)
            .ThenBy(x => x.leaveType.Name)
            .ToListAsync(ct);

        return rows.Select(x => new LeaveEntitlementRow(
            x.entitlement,
            x.employee.EmployeeNumber,
            LeaveEntitlementMapper.EmployeeName(x.employee.FirstName, x.employee.LastName),
            x.employee.DepartmentId,
            x.department?.Name,
            x.employee.LegalEntityId,
            x.legalEntity?.Name,
            x.leaveType.Name,
            x.leaveType.Code,
            LeaveEntitlementMapper.Remaining(x.entitlement))).ToList();
    }

    public async Task<LeaveEntitlementRow?> GetRowByIdAsync(
        Guid tenantId, Guid entitlementId, CancellationToken ct = default)
    {
        var rows = await (
            from entitlement in _db.LeaveEntitlements.AsNoTracking()
            join employee in _db.Employees.AsNoTracking() on entitlement.EmployeeId equals employee.Id
            join leaveType in _db.LeaveTypes.AsNoTracking() on entitlement.LeaveTypeId equals leaveType.Id
            join department in _db.Departments.AsNoTracking() on employee.DepartmentId equals department.Id into departments
            from department in departments.DefaultIfEmpty()
            join legalEntity in _db.LegalEntities.AsNoTracking() on employee.LegalEntityId equals legalEntity.Id into legalEntities
            from legalEntity in legalEntities.DefaultIfEmpty()
            where entitlement.TenantId == tenantId && entitlement.Id == entitlementId
            select new { entitlement, employee, leaveType, department, legalEntity })
            .ToListAsync(ct);

        var x = rows.SingleOrDefault();
        if (x is null)
            return null;

        return new LeaveEntitlementRow(
            x.entitlement,
            x.employee.EmployeeNumber,
            LeaveEntitlementMapper.EmployeeName(x.employee.FirstName, x.employee.LastName),
            x.employee.DepartmentId,
            x.department?.Name,
            x.employee.LegalEntityId,
            x.legalEntity?.Name,
            x.leaveType.Name,
            x.leaveType.Code,
            LeaveEntitlementMapper.Remaining(x.entitlement));
    }

    public async Task<IReadOnlyList<LeaveEntitlement>> ListExistingAsync(
        Guid tenantId,
        int year,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return [];

        return await _db.LeaveEntitlements.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Year == year && employeeIds.Contains(e.EmployeeId))
            .ToListAsync(ct);
    }

    public async Task<LeaveEntitlement?> GetTrackedByIdAsync(
        Guid tenantId, Guid entitlementId, CancellationToken ct = default)
    {
        return await _db.LeaveEntitlements
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == entitlementId, ct);
    }

    public async Task<LeaveEntitlement?> GetTrackedByEmployeeTypeYearAsync(
        Guid tenantId,
        Guid employeeId,
        Guid leaveTypeId,
        int year,
        CancellationToken ct = default)
    {
        return await _db.LeaveEntitlements
            .FirstOrDefaultAsync(
                e => e.TenantId == tenantId
                    && e.EmployeeId == employeeId
                    && e.LeaveTypeId == leaveTypeId
                    && e.Year == year,
                ct);
    }

    public async Task<IReadOnlyDictionary<(Guid EmployeeId, Guid LeaveTypeId), LeaveEntitlement>> ListPreviousYearAsync(
        Guid tenantId,
        int previousYear,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<(Guid, Guid), LeaveEntitlement>();

        var rows = await _db.LeaveEntitlements.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Year == previousYear && employeeIds.Contains(e.EmployeeId))
            .ToListAsync(ct);

        return rows.ToDictionary(e => (e.EmployeeId, e.LeaveTypeId));
    }

    public async Task AddGeneratedAsync(
        IReadOnlyCollection<LeaveEntitlementWriteSet> writeSets, CancellationToken ct = default)
    {
        await WriteAsync(
            () =>
            {
                _db.LeaveEntitlements.AddRange(writeSets.Select(x => x.Entitlement));
                _db.LeaveBalanceAudits.AddRange(writeSets.SelectMany(x => x.Audits));
                return Task.CompletedTask;
            },
            ct);
    }

    public Task AddManualAsync(LeaveEntitlement entitlement, LeaveBalanceAudit audit, CancellationToken ct = default) =>
        SaveWithAuditAsync(entitlement, audit, ct);

    public async Task SaveWithAuditAsync(LeaveEntitlement entitlement, LeaveBalanceAudit audit, CancellationToken ct = default)
    {
        await WriteAsync(
            () =>
            {
                if (_db.Entry(entitlement).State == EntityState.Detached)
                    _db.LeaveEntitlements.Add(entitlement);

                _db.LeaveBalanceAudits.Add(audit);
                return Task.CompletedTask;
            },
            ct);
    }

    private async Task WriteAsync(Func<Task> mutate, CancellationToken ct)
    {
        // EnableRetryOnFailure configured - EF Core forbids a user-initiated BeginTransactionAsync
        // under a retrying execution strategy unless it runs inside ExecuteAsync (same wrapping as
        // EfLeavePolicyRepository.AddAggregateWithReplacementAsync).
        var executionStrategy = _db.Database.CreateExecutionStrategy();
        try
        {
            await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = _db.Database.IsRelational()
                    ? await _db.Database.BeginTransactionAsync(ct)
                    : null;

                await mutate();
                await _db.SaveChangesAsync(ct);

                if (transaction is not null)
                    await transaction.CommitAsync(ct);
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new UniqueConstraintConflictException(ex);
        }
    }
}
