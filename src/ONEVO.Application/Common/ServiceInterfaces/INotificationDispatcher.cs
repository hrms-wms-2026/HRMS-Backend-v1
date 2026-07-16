namespace ONEVO.Application.Common.ServiceInterfaces;

public interface INotificationDispatcher
{
    Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default);
    Task SendToTenantAsync(Guid tenantId, string eventName, object payload, CancellationToken ct = default);
    Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken ct = default);
}
