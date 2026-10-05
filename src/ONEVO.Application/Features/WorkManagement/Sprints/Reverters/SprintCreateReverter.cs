using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Reverters;

/// <summary>Undoes an approved sprint.create by soft-deleting the sprint it created (detaching any
/// tasks currently in it back to the backlog, the same way an ordinary sprint.delete does), and nulls
/// Request.TargetId back to null (SprintCreateApplier stamped it with the new sprint's id - see Review
/// Focus #2) so the reopened Pending request doesn't keep pointing at a now-deleted sprint.
///
/// Known limitation: if the create moved tasks in from another existing sprint (SprintCreateInput.TaskIds
/// can reference tasks already assigned elsewhere - SprintWriteService.CreateAsync tracks exactly this via
/// previousSprintGroups), this sends them to the backlog rather than back to that prior sprint. No step in
/// this plan captures which tasks came from where, so a full undo isn't a mechanical addition. Narrow and
/// visible (the task's sprint column) if it happens.</summary>
public sealed class SprintCreateReverter : IApprovalActionReverter
{
    private readonly ISprintWriteService _writes;
    private readonly ISprintRepository _sprints;

    public SprintCreateReverter(ISprintWriteService writes, ISprintRepository sprints)
    {
        _writes = writes;
        _sprints = sprints;
    }

    public string ActionType => WorkActionTypes.SprintCreate;

    public async Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return RevertOutcome.Stale;

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null)
            return RevertOutcome.Stale;

        await _writes.ApplyDeleteAsync(request.TenantId, sprint, ct);
        request.TargetId = null;
        // No TargetUpdatedAtSnapshot to re-baseline: SprintCreateApplier never checked one (it has no
        // existing target to be stale against), and the request now has no TargetId either.
        return RevertOutcome.Reverted();
    }
}
