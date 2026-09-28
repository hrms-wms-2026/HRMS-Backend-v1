using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;

public class EditTaskStatusCommandHandler : IRequestHandler<EditTaskStatusCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskStatusRepository _statuses;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskStatusChangeRequestConflictSweeper _sweeper;

    public EditTaskStatusCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ITaskStatusRepository statuses,
        IObjectiveRepository objectives, IProjectRepository projects, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, ITaskStatusChangeRequestConflictSweeper sweeper)
    {
        _currentUser = currentUser;
        _identity = identity;
        _statuses = statuses;
        _objectives = objectives;
        _projects = projects;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _sweeper = sweeper;
    }

    public async Task<Result> Handle(EditTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var status = await _statuses.GetByIdForTenantAsync(tenantId, request.StatusId, ct);
        if (status is null || status.ObjectiveId is not null)
            return Result.NotFound("Task status not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, status.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result.NotFound("Project not found.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result.NotFound("Project has no default milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct))
            return Result.Forbidden("Only an owner or member of this project can change task status configuration.");

        var siblings = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);

        if (request.Category == TaskStatusCategories.Done && status.Category != TaskStatusCategories.Done
            && siblings.Any(s => s.Id != status.Id && s.Category == TaskStatusCategories.Done))
            return Result.Conflict("This project already has a Done status; edit or delete it first.");

        if (status.Category == TaskStatusCategories.Done && request.Category != TaskStatusCategories.Done)
            return Result.Conflict("A project must always have exactly one Done status.");

        if (status.Category == TaskStatusCategories.Active && request.Category != TaskStatusCategories.Active
            && siblings.Count(s => s.Category == TaskStatusCategories.Active) <= 1)
            return Result.Conflict("A project must always have at least one Active status.");

        var before = TaskStatusChangeSetApplier.Snapshot(status);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            status.Name = request.Name.Trim();
            status.DisplayOrder = request.DisplayOrder;
            status.RequiresApproval = request.RequiresApproval;
            status.ApproverId = request.ApproverId;
            status.Visibility = request.Visibility;
            status.Category = request.Category;
            status.Color = request.Color;
            status.MarksTaskComplete = request.Category == TaskStatusCategories.Done;
            status.UpdatedAt = DateTimeOffset.UtcNow;
            _statuses.Update(status);

            if (!TaskStatusChangeSetApplier.Snapshot(status).Equals(before))
                await _sweeper.MarkConflictingOutdatedAsync(
                    tenantId, project.Id, project.Name,
                    new TaskStatusChangeFootprint(new HashSet<Guid> { status.Id }, ReordersExisting: false), null, innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
