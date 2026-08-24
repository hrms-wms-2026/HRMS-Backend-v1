using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Type;

public class EfLeaveTypeRepository : ILeaveTypeRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeaveTypeRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LeaveType>> ListAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default)
    {
        var query = _db.LeaveTypes.AsNoTracking().Where(t => t.TenantId == tenantId);
        if (!includeInactive)
            query = query.Where(t => t.IsActive);
        return await query.OrderBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<LeaveType?> GetByIdAsync(Guid tenantId, Guid leaveTypeId, CancellationToken ct = default)
        => await _db.LeaveTypes.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == leaveTypeId, ct);

    public async Task<bool> ExistsByNameAsync(Guid tenantId, string name, Guid? excludingLeaveTypeId, CancellationToken ct = default)
    {
        var normalized = name.ToLower();
        var query = _db.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Name.ToLower() == normalized);
        if (excludingLeaveTypeId is { } id)
            query = query.Where(t => t.Id != id);
        return await query.AnyAsync(ct);
    }

    public async Task<bool> ExistsByCodeAsync(Guid tenantId, string code, Guid? excludingLeaveTypeId, CancellationToken ct = default)
    {
        var normalized = code.ToUpperInvariant();
        var query = _db.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Code == normalized);
        if (excludingLeaveTypeId is { } id)
            query = query.Where(t => t.Id != id);
        return await query.AnyAsync(ct);
    }

    public async Task<int> CountPendingRequestsAsync(Guid tenantId, Guid leaveTypeId, CancellationToken ct = default)
        => await _db.LeaveRequests.AsNoTracking()
            .CountAsync(r => r.TenantId == tenantId
                && r.LeaveTypeId == leaveTypeId
                && r.Status == LeaveRequestStatuses.Pending, ct);

    public Task AddAsync(LeaveType leaveType, CancellationToken ct = default)
    {
        _db.LeaveTypes.Add(leaveType);
        return Task.CompletedTask;
    }

    public void Update(LeaveType leaveType) => _db.LeaveTypes.Update(leaveType);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
