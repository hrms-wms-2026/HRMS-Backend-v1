using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskEditRequest;

public class CreateTaskEditRequestCommandHandler : IRequestHandler<CreateTaskEditRequestCommand, Result<TaskEditRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskEditRequestRepository _requests;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaskEditRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        IObjectiveRepository objectives, ISprintRepository sprints, IMilestoneMembershipCoordinator membership,
        ITaskEditRequestRepository requests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _objectives = objectives;
        _sprints = sprints;
        _membership = membership;
        _requests = requests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TaskEditRequestResponse>> Handle(CreateTaskEditRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskEditRequestResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<TaskEditRequestResponse>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<TaskEditRequestResponse>.NotFound("Task not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<TaskEditRequestResponse>.NotFound("Objective not found.");

        if (objective.OwnerId == callerEmployeeId.Value)
            return Result<TaskEditRequestResponse>.Failure("The milestone owner edits tasks directly - no request needed.", 400);

        var isMember = await _membership.IsActiveMemberAsync(tenantId, objective.Id, callerEmployeeId.Value, ct);
        if (!isMember)
            return Result<TaskEditRequestResponse>.Forbidden("Only active milestone members can request task edits.");

        if (task.SprintId.HasValue)
        {
            var sprint = await _sprints.GetByIdForTenantAsync(tenantId, task.SprintId.Value, ct);
            if (sprint is not null && sprint.Status == SprintStatuses.Achieved)
                return Result<TaskEditRequestResponse>.Forbidden("This task's sprint has been achieved and is now frozen.");
        }

                var payload = new TaskEditRequestPayload(
            request.Title.Trim(), request.Description?.Trim(), request.Priority, request.DueDate,
            request.EstimatedHours, request.StoryPoints, request.ProgressPercent);

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [callerEmployeeId.Value], ct);
        var requesterDisplayName = names.GetValueOrDefault(callerEmployeeId.Value) ?? "A teammate";

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var entity = new TaskEditRequest
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                                RequestedByEmployeeId = callerEmployeeId.Value, PayloadJson = JsonSerializer.Serialize(payload),
                Reason = request.Reason?.Trim(), Status = TaskEditRequestStatuses.Pending,
                CreatedById = _currentUser.UserId, CreatedAt = now

            };

            await _requests.AddAsync(entity, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<TaskEditRequestResponse>.Success(
                new TaskEditRequestResponse(entity.Id, entity.TaskId, entity.Status, payload, requesterDisplayName, entity.CreatedAt));
        }, ct);
    }
}
