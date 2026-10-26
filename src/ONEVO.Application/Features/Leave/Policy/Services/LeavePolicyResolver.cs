using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Services;

public sealed class LeavePolicyResolver : ILeavePolicyResolver
{
    private readonly ILeavePolicyResolutionRepository _repository;

    public LeavePolicyResolver(ILeavePolicyResolutionRepository repository)
    {
        _repository = repository;
    }

    public async Task<ResolvedLeavePolicy?> ResolveAsync(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, DateOnly asOf, CancellationToken ct)
    {
        var all = await ResolveAllAsync(tenantId, employeeId, asOf, ct);
        return all.FirstOrDefault(r => r.LeaveTypeId == leaveTypeId);
    }

    public async Task<IReadOnlyList<ResolvedLeavePolicy>> ResolveAllAsync(
        Guid tenantId, Guid employeeId, DateOnly asOf, CancellationToken ct)
    {
        var byEmployee = await ResolveForEmployeesAsync(tenantId, [employeeId], asOf, ct);
        return byEmployee.TryGetValue(employeeId, out var resolved) ? resolved : [];
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ResolvedLeavePolicy>>> ResolveForEmployeesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly asOf, CancellationToken ct)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<ResolvedLeavePolicy>>();

        var snapshot = await _repository.LoadAsync(tenantId, employeeIds, ct);
        var index = new SnapshotIndex(snapshot, asOf);

        return snapshot.Employees.ToDictionary(
            employee => employee.Id,
            employee => (IReadOnlyList<ResolvedLeavePolicy>)index.Resolve(employee));
    }

    private sealed class SnapshotIndex
    {
        private readonly LeavePolicyResolutionSnapshot _snapshot;
        private readonly Dictionary<Guid, LeaveResolutionPolicy> _policies;
        private readonly Dictionary<Guid, LeavePolicyVersion> _versionInForce;
        private readonly ILookup<Guid, LeavePolicyBlackoutPeriod> _blackouts;
        private readonly ILookup<(string Kind, Guid Id), LeavePolicyAssignment> _assignments;
        private readonly Dictionary<Guid, Guid?> _departmentParents;
        private readonly ILookup<Guid, LeaveResolutionPosition> _positions;

        public SnapshotIndex(LeavePolicyResolutionSnapshot snapshot, DateOnly asOf)
        {
            _snapshot = snapshot;
            _policies = snapshot.Policies.ToDictionary(p => p.Id);
            _versionInForce = snapshot.Versions
                .Where(v => v.EffectiveFrom <= asOf && _policies.ContainsKey(v.LeavePolicyId))
                .GroupBy(v => v.LeavePolicyId)
                .ToDictionary(g => g.Key, g => g.MaxBy(v => v.EffectiveFrom)!);
            _blackouts = snapshot.Blackouts.ToLookup(b => b.LeavePolicyVersionId);
            _assignments = snapshot.Assignments
                .Where(a => a.EffectiveFrom <= asOf && (a.EffectiveTo is null || a.EffectiveTo >= asOf))
                .Where(a => _versionInForce.ContainsKey(a.LeavePolicyId))
                .ToLookup(a => (a.TargetKind, a.TargetId));
            _departmentParents = snapshot.Departments.ToDictionary(d => d.Id, d => d.ParentDepartmentId);
            _positions = snapshot.Positions
                .Where(p => p.EffectiveFrom <= asOf && (p.EffectiveTo is null || p.EffectiveTo >= asOf))
                .ToLookup(p => p.EmployeeId);
        }

        public List<ResolvedLeavePolicy> Resolve(LeaveResolutionEmployee employee)
        {
            var resolved = new Dictionary<Guid, ResolvedLeavePolicy>();
            foreach (var (kind, targetId) in TargetsMostSpecificFirst(employee))
            {
                foreach (var assignment in _assignments[(kind, targetId)])
                {
                    if (resolved.ContainsKey(assignment.LeaveTypeId))
                        continue;

                    var policy = _policies[assignment.LeavePolicyId];
                    var version = _versionInForce[policy.Id];
                    resolved[assignment.LeaveTypeId] = new ResolvedLeavePolicy(
                        policy.LeaveTypeId,
                        policy.Id,
                        policy.Name,
                        version,
                        _blackouts[version.Id].ToList(),
                        kind,
                        targetId,
                        _snapshot.TargetNames.GetValueOrDefault((kind, targetId)) ?? string.Empty);
                }
            }

            return resolved.Values.ToList();
        }

        private IEnumerable<(string Kind, Guid Id)> TargetsMostSpecificFirst(LeaveResolutionEmployee employee)
        {
            yield return (LeavePolicyTargetKinds.Employee, employee.Id);

            foreach (var position in _positions[employee.Id].OrderByDescending(p => p.EffectiveFrom))
                yield return (LeavePolicyTargetKinds.Position, position.PositionId);

            var visited = new HashSet<Guid>();
            var departmentId = employee.DepartmentId;
            while (departmentId is { } current && visited.Add(current))
            {
                yield return (LeavePolicyTargetKinds.Department, current);
                departmentId = _departmentParents.GetValueOrDefault(current);
            }

            if (employee.LegalEntityId is { } legalEntityId)
                yield return (LeavePolicyTargetKinds.LegalEntity, legalEntityId);
        }
    }
}
