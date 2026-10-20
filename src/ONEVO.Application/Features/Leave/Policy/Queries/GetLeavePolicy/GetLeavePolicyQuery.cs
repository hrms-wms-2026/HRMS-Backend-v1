using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;

public sealed record GetLeavePolicyQuery(Guid PolicyId) : IRequest<Result<LeavePolicyResponse>>;

public sealed class GetLeavePolicyQueryHandler : IRequestHandler<GetLeavePolicyQuery, Result<LeavePolicyResponse>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetLeavePolicyQueryHandler(
        ILeavePolicyRepository policies, ILeaveTypeRepository leaveTypes, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _policies = policies;
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<LeavePolicyResponse>> Handle(GetLeavePolicyQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var today = _clock.Today;
        var policy = await _policies.GetTrackedAsync(tenantId, request.PolicyId, ct);
        if (policy is null)
            return Result<LeavePolicyResponse>.NotFound("Leave policy not found.");

        var versions = await _policies.ListVersionsTrackedAsync(tenantId, [policy.Id], ct);
        if (versions.Count == 0)
            return Result<LeavePolicyResponse>.NotFound("Leave policy has no versions.");
        var blackouts = (await _policies.ListBlackoutsAsync(tenantId, versions.Select(v => v.Id).ToList(), ct))
            .ToLookup(b => b.LeavePolicyVersionId);
        var authors = await _policies.GetUserNamesAsync(
            tenantId, versions.Select(v => v.CreatedBy).OfType<Guid>().Distinct().ToList(), ct);
        var assignments = await _policies.ListOpenAssignmentsForPoliciesAsync(tenantId, [policy.Id], ct);
        var targetNames = await _policies.GetTargetNamesAsync(
            tenantId, assignments.Select(a => (a.TargetKind, a.TargetId)).ToList(), ct);
        var leaveType = await _leaveTypes.GetByIdAsync(tenantId, policy.LeaveTypeId, ct);

        var current = LeavePolicyVersionFactory.InForce(versions, today) ?? versions.MinBy(v => v.EffectiveFrom)!;
        LeavePolicyVersionResponse Map(LeavePolicyVersion v) => ToResponse(
            v, v.Id == current.Id, v.EffectiveFrom > today, blackouts[v.Id], authors);

        var response = new LeavePolicyResponse(
            policy.Id,
            policy.Name,
            policy.Description,
            policy.LeaveTypeId,
            leaveType?.Name ?? string.Empty,
            leaveType?.Color ?? string.Empty,
            policy.IsActive,
            Map(current),
            versions.OrderByDescending(v => v.EffectiveFrom).Select(Map).ToList(),
            assignments
                .OrderBy(a => a.TargetKind).ThenBy(a => targetNames.GetValueOrDefault((a.TargetKind, a.TargetId)))
                .Select(a => new LeavePolicyAssignmentResponse(
                    a.Id, a.TargetKind, a.TargetId,
                    targetNames.GetValueOrDefault((a.TargetKind, a.TargetId)) ?? string.Empty, a.EffectiveFrom))
                .ToList());

        return Result<LeavePolicyResponse>.Success(response);
    }

    private static LeavePolicyVersionResponse ToResponse(
        LeavePolicyVersion v, bool isCurrent, bool isFuture, IEnumerable<LeavePolicyBlackoutPeriod> blackouts,
        IReadOnlyDictionary<Guid, string> authors) => new(
        v.Id,
        v.VersionNumber,
        v.EffectiveFrom,
        isCurrent,
        isFuture,
        v.AmountHours,
        v.AmountPeriod,
        v.CarryForwardCapHours,
        v.CarryForwardExpiryMonths,
        v.EligibleFrom,
        v.EligibleAfterMonths,
        v.MinimumNoticeDays,
        v.MinRequestHours,
        v.MaxRequestHours,
        v.MaxTeamAbsencePercent,
        v.ApprovalMode,
        blackouts.Select(b => new LeavePolicyBlackoutResponse(b.StartDate, b.EndDate, b.Reason)).ToList(),
        v.CreatedAt,
        v.CreatedBy is { } userId ? authors.GetValueOrDefault(userId) : null);
}
