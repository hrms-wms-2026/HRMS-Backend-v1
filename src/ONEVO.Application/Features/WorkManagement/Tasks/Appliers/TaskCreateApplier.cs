using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Appliers;

public static class TaskPayload
{
    // Case-insensitive: rows migrated from the old request tables were serialised PascalCase.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>Applies an approved task.create: the requester becomes the task's creator and the target Module is its creator position.</summary>
public sealed class TaskCreateApplier : IApprovalActionApplier
{
    private readonly ITaskWriteService _writes;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly MeetingActionItemService _actionItems;

    public TaskCreateApplier(ITaskWriteService writes, IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership,
        MeetingActionItemService? actionItems = null)
    {
        _writes = writes;
        _objectives = objectives;
        _membership = membership;
        _actionItems = actionItems;
    }

    public string ActionType => WorkActionTypes.TaskCreate;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        var input = JsonSerializer.Deserialize<TaskCreateInput>(context.PayloadJson, TaskPayload.Options);
        if (input is null || string.IsNullOrWhiteSpace(input.Title))
            return ApplyOutcome.Invalid("The task request has no title.");
        // The meeting an action item comes from is the requester's choice, checked against what they may see when they
        // filed it (spec 5.4). The approver may edit the task, not point it at another meeting.
        if (context.PayloadJson != request.PayloadJson
            && JsonSerializer.Deserialize<TaskCreateInput>(request.PayloadJson, TaskPayload.Options)?.SourceMeetingOccurrenceId
                != input.SourceMeetingOccurrenceId)
            return ApplyOutcome.Invalid("The meeting this task comes from cannot be changed by the approver.");

        var objective = await _objectives.GetByIdForTenantAsync(request.TenantId, input.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return ApplyOutcome.Stale;

        var requester = await _membership.GetActiveAssigneeAsync(request.TenantId, request.RequestedByEmployeeId, ct);
        if (requester is null)
            return ApplyOutcome.Stale;

        var created = await _writes.CreateAsync(request.TenantId, requester.UserId, request.RequestedByEmployeeId,
            input, input.ObjectiveId, ct);
        if (!created.IsSuccess)
            return ApplyOutcome.Invalid(created.Error ?? "The task could not be created.");

        // The action-item link is written when the request is applied, never when it is filed (spec 5.4).
        // A no-op link (the occurrence was cancelled or removed meanwhile, or the requester can no longer
        // see it) is not a failure: the task is still created, and the apply still succeeds (Review Focus 2).
        if (input.SourceMeetingOccurrenceId is Guid sourceOccurrenceId && _actionItems is not null
            && (await _actionItems.ValidateSourceAsync(request.TenantId, request.RequestedByEmployeeId, sourceOccurrenceId, ct)).IsSuccess)
            await _actionItems.LinkAsync(request.TenantId, sourceOccurrenceId, created.Value!.Id,
                request.RequestedByEmployeeId, ct);

        request.TargetId = created.Value!.Id;   // links the approved request to the task it produced
        return ApplyOutcome.Applied();
    }
}
