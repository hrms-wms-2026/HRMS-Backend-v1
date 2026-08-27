using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Approval;

public class EfLeaveApprovalRepository : ILeaveApprovalRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ILeavePolicyRepository _policies;

    public EfLeaveApprovalRepository(ApplicationDbContext db, ILeavePolicyRepository policies)
    {
        _db = db;
        _policies = policies;
    }

    public async Task<LeaveApprovalState?> GetStateAsync(Guid tenantId, Guid requestId, CancellationToken ct = default)
    {
        var request = await _db.LeaveRequests
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == requestId, ct);
        if (request is null)
            return null;

        var employee = await _db.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId, ct);
        if (employee is null)
            return null;

        var leaveType = await _db.LeaveTypes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.LeaveTypeId, ct);
        if (leaveType is null)
            return null;

        var entitlement = await _db.LeaveEntitlements
            .SingleOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.EmployeeId == request.EmployeeId &&
                x.LeaveTypeId == request.LeaveTypeId &&
                x.Year == request.StartDate.Year, ct);

        var approvers = await _db.LeaveRequestApprovers
            .Where(x => x.TenantId == tenantId && x.LeaveRequestId == request.Id)
            .OrderBy(x => x.SequenceOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        var messages = await _db.LeaveRequestInfoMessages.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LeaveRequestId == request.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        string? approvalMode = null;
        if (employee.LegalEntityId is Guid legalEntityId)
        {
            var policies = await _policies.ListActiveAggregatesByLegalEntityIdsAsync(
                tenantId, [legalEntityId], request.StartDate.Year, ct);
            if (policies.TryGetValue(legalEntityId, out var policy))
                approvalMode = policy.Policy.ApprovalMode;
        }

        return new LeaveApprovalState(
            request,
            entitlement,
            employee,
            leaveType.Name,
            leaveType.Code,
            approvalMode,
            approvers,
            messages);
    }

    public async Task<IReadOnlyList<LeavePendingApprovalListRow>> ListPendingForApproverAsync(
        Guid tenantId,
        Guid approverEmployeeId,
        LeaveApprovalListFilter filter,
        CancellationToken ct = default)
    {
        var query =
            from approver in _db.LeaveRequestApprovers.AsNoTracking()
            join request in _db.LeaveRequests.AsNoTracking() on approver.LeaveRequestId equals request.Id
            join employee in _db.Employees.AsNoTracking() on request.EmployeeId equals employee.Id
            join leaveType in _db.LeaveTypes.AsNoTracking() on request.LeaveTypeId equals leaveType.Id
            where approver.TenantId == tenantId
                && request.TenantId == tenantId
                && approver.ApproverEmployeeId == approverEmployeeId
                && approver.Status == LeaveRequestApproverStatuses.Pending
                && request.Status == LeaveRequestStatuses.Pending
            select new { request, employee, leaveType };

        if (filter.DepartmentId is { } departmentId)
            query = query.Where(x => x.employee.DepartmentId == departmentId);
        if (filter.LeaveTypeId is { } leaveTypeId)
            query = query.Where(x => x.request.LeaveTypeId == leaveTypeId);
        if (filter.FromDate is { } from)
            query = query.Where(x => x.request.EndDate >= from);
        if (filter.ToDate is { } to)
            query = query.Where(x => x.request.StartDate <= to);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(x =>
                x.employee.FirstName.ToLower().Contains(search) ||
                x.employee.LastName.ToLower().Contains(search) ||
                x.employee.EmployeeNumber.ToLower().Contains(search));
        }

        var rows = await query.OrderByDescending(x => x.request.CreatedAt).ToListAsync(ct);
        return rows.Select(x => new LeavePendingApprovalListRow(
            x.request,
            LeaveEntitlementMapper.EmployeeName(x.employee.FirstName, x.employee.LastName),
            x.leaveType.Name,
            x.leaveType.Code)).ToList();
    }

    public async Task<IReadOnlyList<LeaveRequestAllListRow>> ListAllAsync(
        Guid tenantId,
        LeaveRequestAllListFilter filter,
        CancellationToken ct = default)
    {
        var query =
            from request in _db.LeaveRequests.AsNoTracking()
            join employee in _db.Employees.AsNoTracking() on request.EmployeeId equals employee.Id
            join leaveType in _db.LeaveTypes.AsNoTracking() on request.LeaveTypeId equals leaveType.Id
            join department in _db.Departments.AsNoTracking() on employee.DepartmentId equals department.Id into departments
            from department in departments.DefaultIfEmpty()
            where request.TenantId == tenantId
            select new { request, employee, leaveType, department };

        if (filter.DepartmentId is { } departmentId)
            query = query.Where(x => x.employee.DepartmentId == departmentId);
        if (filter.LeaveTypeId is { } leaveTypeId)
            query = query.Where(x => x.request.LeaveTypeId == leaveTypeId);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(x => x.request.Status == filter.Status);
        if (filter.FromDate is { } from)
            query = query.Where(x => x.request.EndDate >= from);
        if (filter.ToDate is { } to)
            query = query.Where(x => x.request.StartDate <= to);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(x =>
                x.employee.FirstName.ToLower().Contains(search) ||
                x.employee.LastName.ToLower().Contains(search) ||
                x.employee.EmployeeNumber.ToLower().Contains(search));
        }

        var rows = await query.OrderByDescending(x => x.request.CreatedAt).ToListAsync(ct);
        return rows.Select(x => new LeaveRequestAllListRow(
            x.request,
            LeaveEntitlementMapper.EmployeeName(x.employee.FirstName, x.employee.LastName),
            x.employee.DepartmentId,
            x.department?.Name,
            x.leaveType.Name)).ToList();
    }

    public Task AddInfoMessageAsync(LeaveRequestInfoMessage message, CancellationToken ct = default)
    {
        _db.LeaveRequestInfoMessages.Add(message);
        return Task.CompletedTask;
    }

    public Task AddBalanceAuditAsync(LeaveBalanceAudit audit, CancellationToken ct = default)
    {
        _db.LeaveBalanceAudits.Add(audit);
        return Task.CompletedTask;
    }

    public Task AddDocumentsAsync(IReadOnlyCollection<LeaveRequestDocument> documents, CancellationToken ct = default)
    {
        _db.LeaveRequestDocuments.AddRange(documents);
        return Task.CompletedTask;
    }

    public async Task<bool> AreAvailableFileRecordsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileRecordIds,
        CancellationToken ct = default)
    {
        if (fileRecordIds.Count == 0)
            return true;

        var distinct = fileRecordIds.Distinct().ToArray();
        var available = await _db.FileRecords.AsNoTracking()
            .CountAsync(f => f.TenantId == tenantId
                && distinct.Contains(f.Id)
                && f.Status == FileRecordStatus.Available, ct);
        return available == distinct.Length;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
