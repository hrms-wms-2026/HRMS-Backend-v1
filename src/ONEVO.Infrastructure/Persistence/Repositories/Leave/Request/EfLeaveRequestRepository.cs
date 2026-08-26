using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Request;

public class EfLeaveRequestRepository : ILeaveRequestRepository
{
    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public EfLeaveRequestRepository(ApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<bool> HasOverlappingPendingOrApprovedRequestAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        return await _db.LeaveRequests.AsNoTracking().AnyAsync(
            request =>
                request.TenantId == tenantId &&
                request.EmployeeId == employeeId &&
                (request.Status == LeaveRequestStatuses.Pending ||
                 request.Status == LeaveRequestStatuses.Approved) &&
                request.StartDate <= endDate &&
                request.EndDate >= startDate,
            ct);
    }

    public async Task<IReadOnlyList<LeaveRequestListRow>> ListOwnAsync(
        Guid tenantId,
        Guid employeeId,
        LeaveRequestListFilter filter,
        CancellationToken ct = default)
    {
        var query =
            from request in _db.LeaveRequests.AsNoTracking()
            join leaveType in _db.LeaveTypes.AsNoTracking() on request.LeaveTypeId equals leaveType.Id
            where request.TenantId == tenantId && request.EmployeeId == employeeId
            select new { request, leaveType };

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(x => x.request.Status == filter.Status);
        if (filter.FromDate is { } from)
            query = query.Where(x => x.request.EndDate >= from);
        if (filter.ToDate is { } to)
            query = query.Where(x => x.request.StartDate <= to);
        if (filter.LeaveTypeId is { } leaveTypeId)
            query = query.Where(x => x.request.LeaveTypeId == leaveTypeId);

        var rows = await query
            .OrderByDescending(x => x.request.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(x => new LeaveRequestListRow(x.request, x.leaveType.Name, x.leaveType.Code)).ToList();
    }

    public async Task<IReadOnlyList<LeaveApprovalDelegateRow>> ListActiveDelegatesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> approverEmployeeIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        if (approverEmployeeIds.Count == 0)
            return [];

        return await _db.LeaveApprovalDelegates.AsNoTracking()
            .Where(row =>
                row.TenantId == tenantId &&
                approverEmployeeIds.Contains(row.ApproverEmployeeId) &&
                row.StartDate <= endDate &&
                row.EndDate >= startDate)
            .Select(row => new LeaveApprovalDelegateRow(row.ApproverEmployeeId, row.DelegateEmployeeId))
            .ToListAsync(ct);
    }

    public async Task<int> CountDistinctEmployeesPendingOrApprovedInRangeAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return 0;

        return await _db.LeaveRequests.AsNoTracking()
            .Where(request =>
                request.TenantId == tenantId &&
                employeeIds.Contains(request.EmployeeId) &&
                (request.Status == LeaveRequestStatuses.Pending ||
                 request.Status == LeaveRequestStatuses.Approved) &&
                request.StartDate <= endDate &&
                request.EndDate >= startDate)
            .Select(request => request.EmployeeId)
            .Distinct()
            .CountAsync(ct);
    }

    public async Task AddPendingRequestAsync(LeaveRequestWriteSet writeSet, CancellationToken ct = default)
    {
        // EnableRetryOnFailure configured - EF Core forbids a user-initiated BeginTransactionAsync
        // under a retrying execution strategy unless it runs inside ExecuteAsync.
        var executionStrategy = _db.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(ct)
                : null;

            var overlaps = await _db.LeaveRequests.AnyAsync(
                request =>
                    request.TenantId == writeSet.Request.TenantId &&
                    request.EmployeeId == writeSet.Request.EmployeeId &&
                    (request.Status == LeaveRequestStatuses.Pending ||
                     request.Status == LeaveRequestStatuses.Approved) &&
                    request.StartDate <= writeSet.Request.EndDate &&
                    request.EndDate >= writeSet.Request.StartDate,
                ct);
            if (overlaps)
                throw new InvalidOperationException(LeaveRequestMessages.Overlap);

            writeSet.Entitlement.PendingDays += writeSet.Request.PaidDays;
            writeSet.Entitlement.UpdatedAt = _clock.UtcNow;

            await _db.LeaveRequests.AddAsync(writeSet.Request, ct);
            await _db.LeaveRequestApprovers.AddRangeAsync(writeSet.Approvers, ct);
            await _db.LeaveRequestDocuments.AddRangeAsync(writeSet.Documents, ct);
            await _db.LeaveRequestDayAllocations.AddRangeAsync(writeSet.DayAllocations, ct);
            await _db.SaveChangesAsync(ct);

            if (transaction is not null)
                await transaction.CommitAsync(ct);
        });
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
                && f.Status == FileRecordStatus.Available,
                ct);
        return available == distinct.Length;
    }
}
