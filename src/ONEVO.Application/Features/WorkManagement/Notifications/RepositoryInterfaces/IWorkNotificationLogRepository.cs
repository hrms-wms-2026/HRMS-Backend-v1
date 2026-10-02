using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;

public interface IWorkNotificationLogRepository
{
    Task AddAsync(WorkNotificationLog log, CancellationToken ct = default);
    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<WorkNotificationLog>> ListForRecipientAsync(
        Guid tenantId, Guid projectId, Guid recipientEmployeeId, int skip, int take, CancellationToken ct = default);
}
