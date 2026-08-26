using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.Leave.Cancellation.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Cancellation;

public class EfLeaveCancellationRepository : ILeaveCancellationRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeaveCancellationRepository(ApplicationDbContext db) => _db = db;

    public async Task<LeaveCancellationState?> GetStateAsync(Guid tenantId, Guid requestId, CancellationToken ct = default)
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

        var legalEntity = employee.LegalEntityId is Guid legalEntityId
            ? await _db.LegalEntities.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == legalEntityId, ct)
            : null;

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

        var approverIds = approvers.Select(x => x.ApproverEmployeeId).Distinct().ToList();
        var recipients = approverIds.Count == 0
            ? []
            : await _db.Employees.AsNoTracking()
                .Where(x => x.TenantId == tenantId && approverIds.Contains(x.Id))
                .Select(x => new LeaveCancellationRecipient(
                    x.Id,
                    x.UserId == Guid.Empty ? null : x.UserId,
                    LeaveEntitlementMapper.EmployeeName(x.FirstName, x.LastName)))
                .ToListAsync(ct);

        return new LeaveCancellationState(
            request,
            entitlement,
            employee,
            legalEntity,
            leaveType.Name,
            leaveType.Code,
            approvers,
            recipients);
    }

    public async Task<IReadOnlyList<LeaveRequestDayAllocation>> ListAllocationsAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default)
        => await _db.LeaveRequestDayAllocations
            .Where(x => x.TenantId == tenantId && x.LeaveRequestId == requestId)
            .OrderBy(x => x.LeaveDate)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

    public Task AddAllocationsAsync(IReadOnlyList<LeaveRequestDayAllocation> allocations, CancellationToken ct = default)
    {
        _db.LeaveRequestDayAllocations.AddRange(allocations);
        return Task.CompletedTask;
    }

    public Task AddBalanceAuditAsync(LeaveBalanceAudit audit, CancellationToken ct = default)
    {
        _db.LeaveBalanceAudits.Add(audit);
        return Task.CompletedTask;
    }

    public void SetExpectedVersion(LeaveRequest request, string? expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion)
            || !uint.TryParse(expectedVersion, out var expectedXmin))
        {
            return;
        }

        _db.Entry(request).Property("xmin").OriginalValue = expectedXmin;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
    }
}
