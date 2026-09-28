using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.Mappers;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;

public class EditTaskCommandHandler : IRequestHandler<EditTaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICallerIdentityResolver _identity;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskAssetLinker _assetLinker;
    private readonly ITaskWriteService _writes;

    public EditTaskCommandHandler(
        ICurrentUser currentUser, IWorkTaskRepository tasks, IObjectiveRepository objectives,
        IUnitOfWork unitOfWork, ICallerIdentityResolver identity, IMilestoneMembershipCoordinator membership,
        ITaskAssignmentRepository assignments, ITaskAssetLinker assetLinker, ITaskWriteService writes)
    {
        _currentUser = currentUser;
        _tasks = tasks;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _identity = identity;
        _membership = membership;
        _assignments = assignments;
        _assetLinker = assetLinker;
        _writes = writes;
    }

    public async Task<Result<WorkTaskResponse>> Handle(EditTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<WorkTaskResponse>.NotFound("Task not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null)
            return Result<WorkTaskResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveOwnerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<WorkTaskResponse>.Forbidden(
                "Only this milestone's owner can edit tasks directly. Non-owner members must submit a task edit request.");

        var input = new TaskEditInput(request.Title.Trim(), request.Description?.Trim(), request.Priority, request.DueDate,
            request.EstimatedHours, request.StoryPoints, request.ProgressPercent, request.Reason?.Trim(), request.SprintId);

        var validation = await _writes.ValidateEditAsync(tenantId, task, objective, input, ct);
        if (!validation.IsSuccess)
            return Result<WorkTaskResponse>.Failure(validation.Error!, validation.StatusCode ?? 400);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var applied = await _writes.ApplyEditAsync(tenantId, callerEmployeeId.Value, task, objective, input,
                TaskEditLogSources.Direct, null, innerCt);
            if (!applied.IsSuccess)
                return Result<WorkTaskResponse>.Failure(applied.Error!, applied.StatusCode ?? 400);

            await _unitOfWork.SaveChangesAsync(innerCt);

            var assignments = await _assignments.GetByTaskIdAsync(task.Id, innerCt);
            var assigneeIds = assignments.Select(a => a.EmployeeId).ToList();

            await _assetLinker.SyncAttachmentsAsync(tenantId, userId, task.Id, request.AttachmentFileIds ?? Array.Empty<Guid>(), innerCt);
            await _assetLinker.SyncDescriptionImagesAsync(tenantId, userId, task.Id, task.Description, innerCt);

            return Result<WorkTaskResponse>.Success(WorkTaskResponseMapper.ToResponse(task, assigneeIds));
        }, ct);
    }
}
