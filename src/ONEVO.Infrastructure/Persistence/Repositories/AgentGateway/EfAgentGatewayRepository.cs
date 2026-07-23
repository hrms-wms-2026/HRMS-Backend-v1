using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.AgentGateway;

public sealed class EfAgentGatewayRepository : IAgentGatewayRepository
{
    private readonly ApplicationDbContext _db;
    public EfAgentGatewayRepository(ApplicationDbContext db) => _db = db;

    // ── Enrollment challenges ──────────────────────────────────────────────────

    public async Task AddChallengeAsync(AgentEnrollmentChallenge challenge, CancellationToken ct) =>
        await _db.AgentEnrollmentChallenges.AddAsync(challenge, ct);

    public Task<AgentEnrollmentChallenge?> GetChallengeByIdAsync(Guid enrollmentId, CancellationToken ct) =>
        _db.AgentEnrollmentChallenges.FirstOrDefaultAsync(c => c.Id == enrollmentId, ct);

    public async Task<bool> TryMarkChallengeConfirmedAsync(
        Guid enrollmentId, string authCodeHash,
        Guid tenantId, Guid employeeId, Guid confirmedByUserId, CancellationToken ct)
    {
        var affected = await _db.AgentEnrollmentChallenges
            .Where(c => c.Id == enrollmentId && c.Status == "pending")
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, "confirmed")
                .SetProperty(c => c.AuthorizationCodeHash, authCodeHash)
                .SetProperty(c => c.TenantId, tenantId)
                .SetProperty(c => c.EmployeeId, employeeId)
                .SetProperty(c => c.ConfirmedByUserId, confirmedByUserId), ct);
        return affected > 0;
    }

    public async Task<bool> TryMarkChallengeCompletedAsync(Guid enrollmentId, CancellationToken ct)
    {
        var affected = await _db.AgentEnrollmentChallenges
            .Where(c => c.Id == enrollmentId && c.Status == "confirmed")
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "completed"), ct);
        return affected > 0;
    }

    // ── Registered agents ─────────────────────────────────────────────────────

    public async Task AddAgentAsync(RegisteredAgent agent, CancellationToken ct) =>
        await _db.RegisteredAgents.AddAsync(agent, ct);

    public Task<RegisteredAgent?> GetAgentByDeviceIdAsync(string deviceId, CancellationToken ct) =>
        _db.RegisteredAgents.FirstOrDefaultAsync(a => a.DeviceId == deviceId, ct);

    public Task<RegisteredAgent?> GetAgentByIdAsync(Guid agentId, CancellationToken ct) =>
        _db.RegisteredAgents.FirstOrDefaultAsync(a => a.Id == agentId, ct);

    public async Task<bool> TouchHeartbeatAsync(Guid agentId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await _db.RegisteredAgents
            .Where(a => a.Id == agentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.LastHeartbeatAt, now)
                .SetProperty(a => a.UpdatedAt, now), ct);
        return affected > 0;
    }

    // ── Agent sessions ────────────────────────────────────────────────────────

    public async Task AddSessionAsync(AgentSession session, CancellationToken ct) =>
        await _db.AgentSessions.AddAsync(session, ct);

    public async Task EndActiveSessionAsync(string deviceId, DateTimeOffset endedAt, CancellationToken ct) =>
        await _db.AgentSessions
            .Where(s => s.DeviceId == deviceId && s.IsActive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsActive, false)
                .SetProperty(x => x.EndedAt, endedAt), ct);

    public Task<AgentSession?> GetActiveSessionByDeviceIdAsync(string deviceId, CancellationToken ct) =>
        _db.AgentSessions.FirstOrDefaultAsync(s => s.DeviceId == deviceId && s.IsActive, ct);

    // ── Agent policies ────────────────────────────────────────────────────────

    public async Task AddOrUpdatePolicyAsync(AgentPolicy policy, CancellationToken ct)
    {
        var existing = await _db.AgentPolicies
            .FirstOrDefaultAsync(p => p.AgentId == policy.AgentId, ct);
        if (existing is null)
            await _db.AgentPolicies.AddAsync(policy, ct);
        else
        {
            existing.PolicyJson = policy.PolicyJson;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public Task<AgentPolicy?> GetPolicyByAgentIdAsync(Guid agentId, CancellationToken ct) =>
        _db.AgentPolicies.FirstOrDefaultAsync(p => p.AgentId == agentId, ct);

    // ── Health logs ───────────────────────────────────────────────────────────

    public async Task AddHealthLogAsync(AgentHealthLog log, CancellationToken ct) =>
        await _db.AgentHealthLogs.AddAsync(log, ct);
}
