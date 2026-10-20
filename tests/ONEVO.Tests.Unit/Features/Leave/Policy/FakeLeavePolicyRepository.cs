using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

/// <summary>In-memory policy store. Writes are staged and only become visible on SaveChangesAsync,
/// and every save checks the "one open policy per leave type per target" rule like the real index.</summary>
internal sealed class FakeLeavePolicyRepository : ILeavePolicyRepository
{
    public List<LeaveType> LeaveTypes { get; } = [];
    public List<LeavePolicy> Policies { get; } = [];
    public List<LeavePolicyVersion> Versions { get; } = [];
    public List<LeavePolicyBlackoutPeriod> Blackouts { get; } = [];
    public List<LeavePolicyAssignment> Assignments { get; } = [];
    public Dictionary<(string Kind, Guid Id), string> Targets { get; } = new();
    public int SaveCount { get; private set; }

    private readonly List<Action> _pending = [];

    public Task<IReadOnlyList<LegalEntity>> ListActiveLegalEntitiesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LegalEntity>>([]);

    public Task<LeaveType?> GetLeaveTypeAsync(Guid tenantId, Guid leaveTypeId, CancellationToken ct = default) =>
        Task.FromResult(LeaveTypes.FirstOrDefault(t => t.Id == leaveTypeId));

    public Task<bool> ExistsByNameAsync(Guid tenantId, string name, Guid? excludingPolicyId, CancellationToken ct = default) =>
        Task.FromResult(Policies.Any(p => p.Id != excludingPolicyId
            && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<LeavePolicy?> GetTrackedAsync(Guid tenantId, Guid policyId, CancellationToken ct = default) =>
        Task.FromResult(Policies.FirstOrDefault(p => p.Id == policyId));

    public Task<IReadOnlyList<LeavePolicy>> ListAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LeavePolicy>>(Policies.Where(p => includeInactive || p.IsActive).ToList());

    public Task<IReadOnlyList<LeavePolicyVersion>> ListVersionsTrackedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> policyIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LeavePolicyVersion>>(Versions.Where(v => policyIds.Contains(v.LeavePolicyId)).ToList());

    public Task<IReadOnlyList<LeavePolicyBlackoutPeriod>> ListBlackoutsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> versionIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LeavePolicyBlackoutPeriod>>(
            Blackouts.Where(b => versionIds.Contains(b.LeavePolicyVersionId)).ToList());

    public Task<IReadOnlyList<LeavePolicyAssignment>> ListOpenAssignmentsForLeaveTypeTrackedAsync(
        Guid tenantId, Guid leaveTypeId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LeavePolicyAssignment>>(
            Assignments.Where(a => a.LeaveTypeId == leaveTypeId && a.EffectiveTo is null).ToList());

    public Task<IReadOnlyList<LeavePolicyAssignment>> ListOpenAssignmentsForPoliciesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> policyIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LeavePolicyAssignment>>(
            Assignments.Where(a => policyIds.Contains(a.LeavePolicyId) && a.EffectiveTo is null).ToList());

    public Task<IReadOnlyList<LeavePolicyAssignmentInput>> ListMissingTargetsAsync(
        Guid tenantId, IReadOnlyCollection<LeavePolicyAssignmentInput> targets, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LeavePolicyAssignmentInput>>(
            targets.Where(t => !Targets.ContainsKey((t.TargetKind, t.TargetId))).ToList());

    public Task<IReadOnlyDictionary<(string Kind, Guid Id), string>> GetTargetNamesAsync(
        Guid tenantId, IReadOnlyCollection<(string Kind, Guid Id)> targets, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<(string Kind, Guid Id), string>>(
            targets.Where(Targets.ContainsKey).ToDictionary(t => t, t => Targets[t]));

    public Task<IReadOnlyDictionary<Guid, string>> GetPolicyNamesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> policyIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(
            Policies.Where(p => policyIds.Contains(p.Id)).ToDictionary(p => p.Id, p => p.Name));

    public Task<IReadOnlyDictionary<Guid, string>> GetUserNamesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

    public void AddPolicy(LeavePolicy policy) => _pending.Add(() => Policies.Add(policy));

    public void AddVersion(LeavePolicyVersion version, IReadOnlyList<LeavePolicyBlackoutPeriod> blackouts) =>
        _pending.Add(() => { Versions.Add(version); Blackouts.AddRange(blackouts); });

    public void RemoveVersion(LeavePolicyVersion version) => _pending.Add(() =>
    {
        Versions.Remove(version);
        Blackouts.RemoveAll(b => b.LeavePolicyVersionId == version.Id);
    });

    public void AddAssignment(LeavePolicyAssignment assignment) => _pending.Add(() => Assignments.Add(assignment));

    public void RemoveAssignment(LeavePolicyAssignment assignment) => _pending.Add(() => Assignments.Remove(assignment));

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var write in _pending)
            write();
        _pending.Clear();
        SaveCount++;

        var duplicate = Assignments
            .Where(a => a.EffectiveTo is null)
            .GroupBy(a => (a.TargetKind, a.TargetId, a.LeaveTypeId))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Unique open assignment violated for {duplicate.Key}.");

        return Task.CompletedTask;
    }
}

internal sealed class InlineUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default) =>
        operation(cancellationToken);

    public void ClearTracking()
    {
    }
}
