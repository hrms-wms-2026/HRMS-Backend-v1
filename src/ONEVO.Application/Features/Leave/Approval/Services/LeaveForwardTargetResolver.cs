using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;

namespace ONEVO.Application.Features.Leave.Approval.Services;

/// <summary>
/// Who an approver hands a leave request up to: whoever would approve the approver's own leave
/// (authority route, then HR fallback, then any active delegation - the same ILeaveApproverResolver
/// routing a submission uses, with the approver as the subject). Never the requester and never
/// someone already on the request, so a forward can't loop back or collapse into an existing row.
/// </summary>
public sealed class LeaveForwardTargetResolver
{
    private readonly ILeaveApproverResolver _approvers;

    public LeaveForwardTargetResolver(ILeaveApproverResolver approvers) => _approvers = approvers;

    public async Task<Guid?> ResolveAsync(
        Guid tenantId, LeaveApprovalState state, Guid forwarderEmployeeId, CancellationToken ct)
    {
        var resolution = await _approvers.ResolveAsync(
            tenantId,
            forwarderEmployeeId,
            DateOnly.FromDateTime(state.Request.StartAt.UtcDateTime),
            DateOnly.FromDateTime(state.Request.EndAt.UtcDateTime),
            ct);

        var target = resolution.Approvers
            .OrderBy(a => a.SequenceOrder)
            .Select(a => (Guid?)a.ApproverEmployeeId)
            .FirstOrDefault();
        if (target is not { } targetId)
            return null;
        if (targetId == state.Request.EmployeeId || targetId == forwarderEmployeeId)
            return null;
        if (state.Approvers.Any(a => a.ApproverEmployeeId == targetId))
            return null;
        return targetId;
    }
}
