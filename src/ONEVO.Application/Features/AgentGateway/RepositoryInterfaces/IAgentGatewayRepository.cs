using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

public interface IAgentGatewayRepository
{
    // Enrollment challenges (no tenant filter)
    Task AddChallengeAsync(AgentEnrollmentChallenge challenge, CancellationToken ct);
    Task<AgentEnrollmentChallenge?> GetChallengeByIdAsync(Guid enrollmentId, CancellationToken ct);
    Task<bool> TryMarkChallengeConfirmedAsync(Guid enrollmentId, string authCodeHash,
        Guid tenantId, Guid employeeId, Guid confirmedByUserId, CancellationToken ct);
    Task<bool> TryMarkChallengeCompletedAsync(Guid enrollmentId, CancellationToken ct);

    // Registered agents (tenant-scoped via query filter)
    Task AddAgentAsync(RegisteredAgent agent, CancellationToken ct);
    Task<RegisteredAgent?> GetAgentByDeviceIdAsync(string deviceId, CancellationToken ct);
    Task<RegisteredAgent?> GetAgentByIdAsync(Guid agentId, CancellationToken ct);
    Task<RegisteredAgent?> GetActiveAgentByEmployeeIdAsync(Guid employeeId, CancellationToken ct);
    Task<bool> TouchHeartbeatAsync(Guid agentId, DateTimeOffset now, CancellationToken ct);

    // Device replacement requests (tenant-scoped via query filter)
    Task<AgentDeviceChangeRequest?> GetPendingDeviceChangeByEmployeeIdAsync(
        Guid employeeId, CancellationToken ct);
    Task AddDeviceChangeRequestAsync(AgentDeviceChangeRequest request, CancellationToken ct);

    // Agent sessions (tenant-scoped via query filter)
    Task AddSessionAsync(AgentSession session, CancellationToken ct);
    Task EndActiveSessionAsync(string deviceId, DateTimeOffset endedAt, CancellationToken ct);
    Task<AgentSession?> GetActiveSessionByDeviceIdAsync(string deviceId, CancellationToken ct);

    // Agent policies (tenant-scoped)
    Task AddOrUpdatePolicyAsync(AgentPolicy policy, CancellationToken ct);
    Task<AgentPolicy?> GetPolicyByAgentIdAsync(Guid agentId, CancellationToken ct);

    // Health logs (tenant-scoped)
    Task AddHealthLogAsync(AgentHealthLog log, CancellationToken ct);

    // Activity raw buffer (tenant-scoped)
    Task AddRawActivityBatchAsync(ActivityRawBuffer batch, CancellationToken ct);

    // Offline detection (cross-tenant, runs in system mode)
    Task<IReadOnlyList<Guid>> MarkAgentsInactiveAndReturnIdsAsync(DateTimeOffset threshold, CancellationToken ct);

    // Fleet health
    Task<IReadOnlyList<RegisteredAgent>> GetActiveAgentsAsync(CancellationToken ct);
    Task<IReadOnlyList<AgentHealthLog>> GetRecentHealthLogsAsync(Guid agentId, int count, CancellationToken ct);
}
