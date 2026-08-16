using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;

public class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly IObjectiveAllocationSlackCalculator _slack;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectRepository projects, IWorkTaskRepository tasks, ITaskStatusRepository statuses,
        IObjectiveAllocationSlackCalculator slack, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _projects = projects;
        _tasks = tasks;
        _statuses = statuses;
        _slack = slack;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkTaskResponse>> Handle(CreateTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<WorkTaskResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can create tasks directly. Non-owner members must submit a task creation request.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, objective.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<WorkTaskResponse>.NotFound("Project not found.");

        var statuses = await _statuses.GetByObjectiveIdAsync(tenantId, objective.Id, ct);
        var defaultStatus = statuses.Where(s => !s.MarksTaskComplete).OrderBy(s => s.DisplayOrder).FirstOrDefault();
        if (defaultStatus is null)
            return Result<WorkTaskResponse>.Failure("No task statuses configured for this milestone yet.", 422);

        if (request.EstimatedHours.HasValue)
        {
            var slack = await _slack.CalculateAsync(tenantId, objective, ct: ct);
            if (request.EstimatedHours.Value > slack)
                return Result<WorkTaskResponse>.Conflict(
                    InsufficientAllocationResponseJson.Serialize(new InsufficientAllocationResponse(slack)));
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var taskNumber = await _projects.IncrementAndGetNextTaskNumberAsync(tenantId, objective.ProjectId, innerCt);
            var now = DateTimeOffset.UtcNow;
            var task = new WorkTask
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                ShortId = $"{project.Identifier}-{taskNumber}",
                StatusId = defaultStatus.Id,
                Title = request.Title.Trim(), Description = request.Description?.Trim(),
                TaskType = request.TaskType, Priority = request.Priority, DueDate = request.DueDate,
                EstimatedHours = request.EstimatedHours, StoryPoints = request.StoryPoints,
                CompletedHours = 0m, ProgressPercent = 0, CreatedById = userId, CreatedAt = now
            };

            await _tasks.AddAsync(task, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
                task.TaskType, task.StatusId, task.Priority, task.StoryPoints,
                task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent));
        }, ct);
    }
}
