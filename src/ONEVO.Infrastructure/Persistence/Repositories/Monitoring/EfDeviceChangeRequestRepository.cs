using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring;

public sealed class EfDeviceChangeRequestRepository(ApplicationDbContext db) : IDeviceChangeRequestRepository
{
    public async Task UpsertPendingAsync(DeviceChangeRequest request, CancellationToken ct = default)
    {
        var existing = await db.DeviceChangeRequests.SingleOrDefaultAsync(x =>
            x.TenantId == request.TenantId && x.EmployeeId == request.EmployeeId
            && x.Status == DeviceChangeRequest.StatusPending, ct);

        if (existing is null)
        {
            await db.DeviceChangeRequests.AddAsync(request, ct);
            return;
        }

        existing.NewDeviceFingerprint = request.NewDeviceFingerprint;
        existing.NewDeviceName = request.NewDeviceName;
        existing.NewDeviceOs = request.NewDeviceOs;
        existing.CurrentDeviceRegistrationId = request.CurrentDeviceRegistrationId;
        existing.LegalEntityId = request.LegalEntityId;
        existing.RequestedAt = request.RequestedAt;
    }

    public Task<DeviceChangeRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.DeviceChangeRequests.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public async Task<IReadOnlyList<Guid>> ListPendingEmployeeIdsAsync(
        Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
        => await db.DeviceChangeRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LegalEntityId == legalEntityId
                && x.Status == DeviceChangeRequest.StatusPending)
            .Select(x => x.EmployeeId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<DeviceChangeRequest> Items, int TotalCount)> ListApprovalInboxAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        int skip, int take, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return (Array.Empty<DeviceChangeRequest>(), 0);

        var query = db.DeviceChangeRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LegalEntityId == legalEntityId
                && employeeIds.Contains(x.EmployeeId)
                && x.Status == DeviceChangeRequest.StatusPending);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take).ToListAsync(ct);
        return (items, totalCount);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
