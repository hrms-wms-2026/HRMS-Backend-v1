using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;

public sealed record ListLeavePoliciesQuery(bool IncludeInactive) : IRequest<Result<IReadOnlyList<LeavePolicyListItem>>>;

public sealed class ListLeavePoliciesQueryHandler
    : IRequestHandler<ListLeavePoliciesQuery, Result<IReadOnlyList<LeavePolicyListItem>>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ListLeavePoliciesQueryHandler(
        ILeavePolicyRepository policies, ILeaveTypeRepository leaveTypes, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _policies = policies;
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<LeavePolicyListItem>>> Handle(ListLeavePoliciesQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var today = _clock.Today;
        var policies = await _policies.ListAsync(tenantId, request.IncludeInactive, ct);
        var ids = policies.Select(p => p.Id).ToList();
        var versions = (await _policies.ListVersionsTrackedAsync(tenantId, ids, ct)).ToLookup(v => v.LeavePolicyId);
        var assignments = await _policies.ListOpenAssignmentsForPoliciesAsync(tenantId, ids, ct);
        var targetNames = await _policies.GetTargetNamesAsync(
            tenantId, assignments.Select(a => (a.TargetKind, a.TargetId)).Distinct().ToList(), ct);
        var leaveTypes = (await _leaveTypes.ListAsync(tenantId, includeInactive: true, ct)).ToDictionary(t => t.Id);
        var assignmentsByPolicy = assignments.ToLookup(a => a.LeavePolicyId);

        var items = policies.Select(policy =>
        {
            var policyVersions = versions[policy.Id].ToList();
            var current = LeavePolicyVersionFactory.InForce(policyVersions, today) ?? policyVersions.MinBy(v => v.EffectiveFrom);
            var next = policyVersions.Where(v => v.EffectiveFrom > today).MinBy(v => v.EffectiveFrom);
            var type = leaveTypes.GetValueOrDefault(policy.LeaveTypeId);
            var policyAssignments = assignmentsByPolicy[policy.Id].ToList();
            return new LeavePolicyListItem(
                policy.Id,
                policy.Name,
                policy.LeaveTypeId,
                type?.Name ?? string.Empty,
                type?.Color ?? string.Empty,
                current?.AmountHours ?? 0m,
                current?.AmountPeriod ?? LeaveAmountPeriods.Year,
                current?.VersionNumber ?? 0,
                next?.EffectiveFrom,
                policyAssignments.Count,
                Summarise(policyAssignments, targetNames),
                policy.IsActive);
        }).ToList();

        return Result<IReadOnlyList<LeavePolicyListItem>>.Success(items);
    }

    /// <summary>"Whole company: X" per company, then departments, positions and employees: a name when
    /// there is one, a count when there are more.</summary>
    internal static IReadOnlyList<string> Summarise(
        IReadOnlyList<LeavePolicyAssignment> assignments, IReadOnlyDictionary<(string Kind, Guid Id), string> names)
    {
        string Name(LeavePolicyAssignment a) => names.GetValueOrDefault((a.TargetKind, a.TargetId)) ?? string.Empty;
        string Group(string kind, string singular, string plural)
        {
            var group = assignments.Where(a => a.TargetKind == kind).ToList();
            return group.Count switch
            {
                0 => string.Empty,
                1 when kind != LeavePolicyTargetKinds.Employee => Name(group[0]),
                1 => $"1 {singular}",
                _ => $"{group.Count} {plural}",
            };
        }

        var summary = assignments
            .Where(a => a.TargetKind == LeavePolicyTargetKinds.LegalEntity)
            .Select(a => $"Whole company: {Name(a)}")
            .ToList();
        summary.Add(Group(LeavePolicyTargetKinds.Department, "department", "departments"));
        summary.Add(Group(LeavePolicyTargetKinds.Position, "position", "positions"));
        summary.Add(Group(LeavePolicyTargetKinds.Employee, "employee", "employees"));
        return summary.Where(s => s.Length > 0).ToList();
    }
}
