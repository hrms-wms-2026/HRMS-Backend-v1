using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;

public interface IAgentCommandRepository
{
    void Add(AgentCommand command);

    Task<AgentCommand?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);

    Task<List<AgentCommand>> GetPendingForDeviceAsync(Guid deviceId, CancellationToken ct);

    Task<int> ExpireStaleCommandsAsync(DateTimeOffset now, CancellationToken ct);
}
