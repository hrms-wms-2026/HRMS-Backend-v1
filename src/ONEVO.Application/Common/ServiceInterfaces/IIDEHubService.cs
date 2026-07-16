namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IIDEHubService
{
    Task SendTagExecutedAsync(Guid userId, object payload, CancellationToken ct = default);
    Task SendAiActionPendingAsync(Guid userId, object payload, CancellationToken ct = default);
    Task SendContextDetectedAsync(Guid userId, object payload, CancellationToken ct = default);
    Task SendTaskUpdatedAsync(Guid userId, object payload, CancellationToken ct = default);
}
