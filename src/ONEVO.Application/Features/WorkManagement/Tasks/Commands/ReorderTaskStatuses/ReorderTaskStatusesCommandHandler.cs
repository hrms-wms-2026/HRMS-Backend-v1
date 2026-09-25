using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;

public class ReorderTaskStatusesCommandHandler : IRequestHandler<ReorderTaskStatusesCommand, Result<IReadOnlyList<TaskStatusResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskStatusChangeRequestConflictSweeper _sweeper;

    public ReorderTaskStatusesCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectRepository projects, ITaskStatusRepository statuses, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, ITaskStatusChangeRequestConflictSweeper sweeper)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _projects = projects;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _sweeper = sweeper;
    }

    public async Task<Result<IReadOnlyList<TaskStatusResponse>>> Handle(ReorderTaskStatusesCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<TaskStatusResponse>>.NotFound("Project not found.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result<IReadOnlyList<TaskStatusResponse>>.NotFound("Project has no default milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct))
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Only an owner or member of this project can restructure the board.");

        // Defense in depth beyond the validator (which runs in the MediatR pipeline in production,
        // but not when a test calls Handle directly).
        if (request.Updates is null || request.Updates.Any(u => u is null))
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("Updates must not contain null entries.", 422);

        if (request.Updates.Select(u => u.StatusId).Distinct().Count() != request.Updates.Count)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("Updates must not contain duplicate status IDs.", 422);

        if (request.Updates.Count(u => u.Category == TaskStatusCategories.Done) > 1)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("At most one status in a single reorder call may be marked Done.", 422);

        var existing = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        var byId = existing.ToDictionary(s => s.Id);

        foreach (var update in request.Updates)
        {
            if (!byId.ContainsKey(update.StatusId))
                return Result<IReadOnlyList<TaskStatusResponse>>.NotFound($"Status {update.StatusId} not found on this milestone.");
        }

        var updatesById = request.Updates.ToDictionary(u => u.StatusId);
        var categories = existing.Select(s => updatesById.TryGetValue(s.Id, out var update) ? update.Category : s.Category).ToList();
        if (categories.Count(c => c == TaskStatusCategories.Done) != 1)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("A project must always have exactly one Done status.", 422);
        if (!categories.Contains(TaskStatusCategories.Active))
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("A project must always have at least one Active status.", 422);

        // The editor always sends every status here, so only statuses whose fields actually change,
        // and an actual change in relative order, count against pending change requests.
        var touched = request.Updates
            .Where(u => byId[u.StatusId] is var s && (s.Visibility != u.Visibility || s.Category != u.Category || s.Color != u.Color))
            .Select(u => u.StatusId)
            .ToHashSet();
        var oldOrder = existing.OrderBy(s => s.DisplayOrder).Select(s => s.Id).ToList();
        var newOrder = existing
            .OrderBy(s => updatesById.TryGetValue(s.Id, out var u) ? u.DisplayOrder : s.DisplayOrder)
            .ThenBy(s => oldOrder.IndexOf(s.Id))
            .Select(s => s.Id)
            .ToList();
        var footprint = new TaskStatusChangeFootprint(touched, !oldOrder.SequenceEqual(newOrder));

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            foreach (var update in request.Updates)
            {
                var status = byId[update.StatusId];
                status.DisplayOrder = update.DisplayOrder;
                status.Visibility = update.Visibility;
                status.Category = update.Category;
                status.Color = update.Color;
                status.MarksTaskComplete = update.Category == TaskStatusCategories.Done;
                status.UpdatedAt = DateTimeOffset.UtcNow;
                _statuses.Update(status);
            }

            await _sweeper.MarkConflictingOutdatedAsync(tenantId, project.Id, project.Name, footprint, null, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<IReadOnlyList<TaskStatusResponse>>.Success(
                existing.OrderBy(s => s.DisplayOrder)
                    .Select(s => new TaskStatusResponse(s.Id, s.Name, s.DisplayOrder, s.RequiresApproval, s.ApproverId, s.MarksTaskComplete, s.Visibility, s.Category, s.Color))
                    .ToList());
        }, ct);
    }
}

