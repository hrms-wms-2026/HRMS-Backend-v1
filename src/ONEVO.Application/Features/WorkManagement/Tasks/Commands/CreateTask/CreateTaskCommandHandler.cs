using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.Mappers;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;

/// <summary>
/// Creates a task through the approval engine: a caller at or above the target Module creates it now
/// (and the Module owner is notified); any other Module member files a task.create approval request.
/// </summary>
public class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand, Result<TaskWriteOutcome>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskAssetLinker _assetLinker;
    private readonly ITaskWriteService _writes;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IWorkApprovalEngine _approvals;
    private readonly IWorkNotificationEngine _notifications;
    private readonly IOptions<MeetingsOptions> _meetingsOptions;
    private readonly MeetingActionItemService _actionItems;

    public CreateTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership, ITaskAssetLinker assetLinker,
        ITaskWriteService writes, IWorkHierarchyService hierarchy, IWorkApprovalEngine approvals,
        IWorkNotificationEngine notifications,
        IOptions<MeetingsOptions> meetingsOptions,
        MeetingActionItemService actionItems)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _assetLinker = assetLinker;
        _writes = writes;
        _hierarchy = hierarchy;
        _approvals = approvals;
        _notifications = notifications;
        _meetingsOptions = meetingsOptions;
        _actionItems = actionItems;
    }

    public async Task<Result<TaskWriteOutcome>> Handle(CreateTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskWriteOutcome>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<TaskWriteOutcome>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<TaskWriteOutcome>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<TaskWriteOutcome>.Forbidden("Only members of this module can change its tasks.");

        var input = new TaskCreateInput(objective.Id, request.Title.Trim(), request.Description?.Trim(), request.CategoryId,
            request.Priority, request.DueDate, request.EstimatedHours, request.StoryPoints, request.SprintId,
            request.SourceMeetingOccurrenceId);

        // An action item is a meeting write too: the Plan B flags gate it (R4), and the source must be an
        // occurrence the caller can see - an invisible or gone source is a 404 and nothing is created.
        if (request.SourceMeetingOccurrenceId is Guid sourceOccurrenceId)
        {
            if (MeetingGuard.CheckWritable<bool>(_meetingsOptions.Value) is { } blocked)
                return Result<TaskWriteOutcome>.Failure(blocked.Error!, blocked.StatusCode ?? 409, blocked.ErrorCode);

            var source = await _actionItems.ValidateSourceAsync(tenantId, callerEmployeeId.Value, sourceOccurrenceId, ct);
            if (!source.IsSuccess)
                return Result<TaskWriteOutcome>.Failure(source.Error!, source.StatusCode ?? 404, source.ErrorCode);
        }

        var validation = await _writes.ValidateCreateAsync(tenantId, input, ct);
        if (!validation.IsSuccess)
            return Result<TaskWriteOutcome>.Failure(validation.Error!, validation.StatusCode ?? 400);

        var tree = await _hierarchy.LoadTreeAsync(tenantId, objective.ProjectId, ct);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var decision = await _approvals.SubmitAsync(new WorkAction(
                tenantId, objective.ProjectId, callerEmployeeId.Value,
                WorkActionTypes.TaskCreate, WorkTargetTypes.Task,
                TargetId: null, TargetTitle: input.Title,
                TargetModuleId: objective.Id, PositionModuleId: objective.Id,
                PayloadJson: JsonSerializer.Serialize(input), TargetUpdatedAt: null), innerCt);
            if (!decision.IsSuccess)
                return Result<TaskWriteOutcome>.Failure(decision.Error!, decision.StatusCode ?? 400);

            if (!decision.Value!.IsDirect)
            {
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(null, decision.Value.ApprovalRequestId));
            }

            // AncestorChain goes self -> root, so LastOrDefault is the owned Module closest to the root.
            var position = tree.AncestorChain(objective.Id).LastOrDefault(m => m.OwnerId == callerEmployeeId.Value)?.Id
                ?? objective.Id;

            var created = await _writes.CreateAsync(tenantId, userId, callerEmployeeId.Value, input, position, innerCt);
            if (!created.IsSuccess)
                return Result<TaskWriteOutcome>.Failure(created.Error!, created.StatusCode ?? 400);
            var task = created.Value!;

            // The action-item link is written with the task, in the same transaction (spec 5.4).
            if (request.SourceMeetingOccurrenceId is Guid sourceOccurrenceId)
                await _actionItems.LinkAsync(tenantId, sourceOccurrenceId, task.Id, callerEmployeeId.Value, innerCt);

            await _notifications.NotifyAsync(new WorkNotificationEvent(
                tenantId, objective.ProjectId, callerEmployeeId.Value, WorkNotificationKinds.Direct,
                WorkActionTypes.TaskCreate, WorkTargetTypes.Task, task.Id, task.Title, null,
                [objective.OwnerId]), innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);

            await _assetLinker.SyncAttachmentsAsync(tenantId, userId, task.Id, request.AttachmentFileIds ?? Array.Empty<Guid>(), innerCt);
            await _assetLinker.SyncDescriptionImagesAsync(tenantId, userId, task.Id, task.Description, innerCt);

            return Result<TaskWriteOutcome>.Success(new TaskWriteOutcome(WorkTaskResponseMapper.ToResponse(task), null));
        }, ct);
    }
}
