using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Services;

/// <summary>What changing a policy's targets would do: the conflicts that need confirmation, the
/// open rows to end, and the targets to add.</summary>
public sealed record LeavePolicyAssignmentPlan(
    IReadOnlyList<LeavePolicyAssignmentConflict> Conflicts,
    IReadOnlyList<LeavePolicyAssignment> ToEnd,
    IReadOnlyList<LeavePolicyAssignmentInput> ToAdd);

/// <summary>Writes dated assignments. Nothing is ever deleted once in force: a replaced or removed
/// assignment is ended yesterday (or dropped if it only started today), and the new one starts today.
/// Ends are saved before adds so the "one open policy per leave type per target" index never sees
/// two open rows at once.</summary>
public sealed class LeavePolicyAssignmentService
{
    private readonly ILeavePolicyRepository _policies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public LeavePolicyAssignmentService(ILeavePolicyRepository policies, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _policies = policies;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Plans replacing the company/department/position targets of <paramref name="policyId"/>
    /// with <paramref name="desired"/>. Employee-level overrides of the policy are left alone.</summary>
    public async Task<LeavePolicyAssignmentPlan> PlanPolicyTargetsAsync(
        Guid tenantId, Guid policyId, Guid leaveTypeId, IReadOnlyList<LeavePolicyAssignmentInput> desired, CancellationToken ct)
    {
        var open = await _policies.ListOpenAssignmentsForLeaveTypeTrackedAsync(tenantId, leaveTypeId, ct);
        var wanted = desired.Select(t => (t.TargetKind, t.TargetId)).ToHashSet();

        var mine = open.Where(a => a.LeavePolicyId == policyId && a.TargetKind != LeavePolicyTargetKinds.Employee).ToList();
        var taken = open.Where(a => a.LeavePolicyId != policyId && wanted.Contains((a.TargetKind, a.TargetId))).ToList();
        var conflicts = await DescribeConflictsAsync(tenantId, taken, ct);

        var keep = mine.Select(a => (a.TargetKind, a.TargetId)).ToHashSet();
        var toEnd = mine.Where(a => !wanted.Contains((a.TargetKind, a.TargetId))).Concat(taken).ToList();
        var toAdd = desired.Where(t => !keep.Contains((t.TargetKind, t.TargetId))).ToList();
        return new LeavePolicyAssignmentPlan(conflicts, toEnd, toAdd);
    }

    /// <summary>Ends <see cref="LeavePolicyAssignmentPlan.ToEnd"/> then adds
    /// <see cref="LeavePolicyAssignmentPlan.ToAdd"/> for the policy, in one transaction.</summary>
    public Task ApplyAsync(
        Guid tenantId, LeavePolicy policy, LeavePolicyAssignmentPlan plan, Guid? actorId, CancellationToken ct,
        Action? beforeSave = null) =>
        _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            beforeSave?.Invoke();
            foreach (var assignment in plan.ToEnd)
                End(assignment, actorId);
            await _policies.SaveChangesAsync(innerCt);

            foreach (var target in plan.ToAdd)
                _policies.AddAssignment(NewAssignment(tenantId, policy, target, actorId));
            await _policies.SaveChangesAsync(innerCt);
            return true;
        }, ct);

    public void End(LeavePolicyAssignment assignment, Guid? actorId)
    {
        var today = _clock.Today;
        if (assignment.EffectiveFrom >= today)
        {
            _policies.RemoveAssignment(assignment);
            return;
        }

        assignment.EffectiveTo = today.AddDays(-1);
        assignment.EndedBy = actorId;
    }

    public LeavePolicyAssignment NewAssignment(
        Guid tenantId, LeavePolicy policy, LeavePolicyAssignmentInput target, Guid? actorId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        LeavePolicyId = policy.Id,
        LeaveTypeId = policy.LeaveTypeId,
        TargetKind = target.TargetKind,
        TargetId = target.TargetId,
        EffectiveFrom = _clock.Today,
        CreatedAt = _clock.UtcNow,
        CreatedBy = actorId,
    };

    private async Task<IReadOnlyList<LeavePolicyAssignmentConflict>> DescribeConflictsAsync(
        Guid tenantId, IReadOnlyList<LeavePolicyAssignment> taken, CancellationToken ct)
    {
        if (taken.Count == 0)
            return [];

        var targetNames = await _policies.GetTargetNamesAsync(
            tenantId, taken.Select(a => (a.TargetKind, a.TargetId)).ToList(), ct);
        var policyNames = await _policies.GetPolicyNamesAsync(
            tenantId, taken.Select(a => a.LeavePolicyId).Distinct().ToList(), ct);
        return taken
            .Select(a => new LeavePolicyAssignmentConflict(
                a.TargetKind,
                a.TargetId,
                targetNames.GetValueOrDefault((a.TargetKind, a.TargetId)) ?? string.Empty,
                a.LeavePolicyId,
                policyNames.GetValueOrDefault(a.LeavePolicyId) ?? string.Empty))
            .ToList();
    }
}
