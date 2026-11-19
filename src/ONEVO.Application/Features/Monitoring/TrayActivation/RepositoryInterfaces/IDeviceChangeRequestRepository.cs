using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;

public interface IDeviceChangeRequestRepository
{
    /// <summary>Inserts a new pending request, or if one is already pending for this
    /// employee, updates its NewDeviceFingerprint/NewDeviceName/NewDeviceOs/RequestedAt
    /// in place instead of inserting a duplicate.</summary>
    Task UpsertPendingAsync(DeviceChangeRequest request, CancellationToken ct = default);

    Task<DeviceChangeRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ListPendingEmployeeIdsAsync(
        Guid tenantId, Guid legalEntityId, CancellationToken ct = default);

    Task<(IReadOnlyList<DeviceChangeRequest> Items, int TotalCount)> ListApprovalInboxAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        int skip, int take, CancellationToken ct = default);

    /// <summary>Pending requests with no legal entity of their own (plan D-c: they are scoped through the employee's).</summary>
    Task<IReadOnlyList<DeviceChangeRequest>> ListPendingWithoutLegalEntityAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>The employee's own device-change requests, newest first (the Inbox Sent list).</summary>
    Task<IReadOnlyList<DeviceChangeRequest>> ListForEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
