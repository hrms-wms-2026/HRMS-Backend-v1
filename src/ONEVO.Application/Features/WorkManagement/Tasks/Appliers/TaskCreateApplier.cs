using System.Text.Json;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
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

/// <summary>Applies an approved task.create: the requester becomes the task's creator and the target Module is its creator position.
/// A request from the Planner create popover also carries the requester as assignee and a slot: the requester is then
/// assigned, and the slot planned for them if it still can be (a failed plan never fails the approval).</summary>
public sealed class TaskCreateApplier : IApprovalActionApplier
{
    private readonly ITaskWriteService _writes;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly MeetingActionItemService _actionItems;
    private readonly ITaskAssignmentRepository? _assignments;
    private readonly IUnitOfWork? _unitOfWork;
    private readonly IPlannerBlockWriter? _planner;

    public TaskCreateApplier(ITaskWriteService writes, IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership,
        MeetingActionItemService? actionItems = null, ITaskAssignmentRepository? assignments = null, IUnitOfWork? unitOfWork = null,
        IPlannerBlockWriter? planner = null)
    {
        _writes = writes;
        _objectives = objectives;
        _membership = membership;
        _actionItems = actionItems;
        _assignments = assignments;
        _unitOfWork = unitOfWork;
        _planner = planner;
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
        // Who the task is for and when it is planned are the requester's, like the source meeting.
        if (context.PayloadJson != request.PayloadJson
            && JsonSerializer.Deserialize<TaskCreateInput>(request.PayloadJson, TaskPayload.Options) is { } filed
            && (filed.AssigneeEmployeeId, filed.PlanStartAt, filed.PlanEndAt) != (input.AssigneeEmployeeId, input.PlanStartAt, input.PlanEndAt))
            return ApplyOutcome.Invalid("The assignee and planned time cannot be changed by the approver.");

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

        // The assignee is always the requester (checked when the request was filed); the approval stands in for the
        // module owner's assign. The block needs the assignment saved first, then follows the requester's planner rules.
        var task = created.Value!;
        if (input.AssigneeEmployeeId is not null && _assignments is not null && _unitOfWork is not null)
        {
            await _assignments.AddAsync(new TaskAssignment
            {
                Id = Guid.NewGuid(), TaskId = task.Id, UserId = requester.UserId, EmployeeId = requester.Id,
                AssignedById = context.DeciderEmployeeId, AssignedAt = DateTimeOffset.UtcNow,
            }, ct);
            await _membership.UpsertMembershipAsync(request.TenantId, task.ProjectId, task.ObjectiveId, requester.Id, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            if (input.PlanStartAt is { } start && input.PlanEndAt is { } end && _planner is not null)
                await _planner.PlanForAsync(request.TenantId, requester.Id, task.Id, start, end, ct);
        }

        request.TargetId = task.Id;   // links the approved request to the task it produced
        return ApplyOutcome.Applied();
    }
}
