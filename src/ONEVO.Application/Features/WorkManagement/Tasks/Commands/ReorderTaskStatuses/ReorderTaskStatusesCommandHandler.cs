using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

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

    public ReorderTaskStatusesCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectRepository projects, ITaskStatusRepository statuses, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _projects = projects;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
        _membership = membership;
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
        // but not when a test calls Handle directly) - exactly one complete status, always.
        if (request.Updates is null || request.Updates.Any(u => u is null))
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("Updates must not contain null entries.", 422);

        if (request.Updates.Count(u => u.MarksTaskComplete) != 1)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("Exactly one status must be marked as the complete status.", 422);

        if (request.Updates.Select(u => u.StatusId).Distinct().Count() != request.Updates.Count)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("Updates must not contain duplicate status IDs.", 422);

        var existing = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        var byId = existing.ToDictionary(s => s.Id);

        foreach (var update in request.Updates)
        {
            if (!byId.TryGetValue(update.StatusId, out var status))
                return Result<IReadOnlyList<TaskStatusResponse>>.NotFound($"Status {update.StatusId} not found on this milestone.");

            status.DisplayOrder = update.DisplayOrder;
            status.Visibility = update.Visibility;
            status.MarksTaskComplete = update.MarksTaskComplete;
            status.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (existing.Count(s => s.MarksTaskComplete) != 1)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("Exactly one status must be marked as the complete status.", 422);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            foreach (var status in existing.Where(s => request.Updates.Any(u => u.StatusId == s.Id)))
                _statuses.Update(status);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<IReadOnlyList<TaskStatusResponse>>.Success(
                existing.OrderBy(s => s.DisplayOrder)
                    .Select(s => new TaskStatusResponse(s.Id, s.Name, s.DisplayOrder, s.RequiresApproval, s.ApproverId, s.MarksTaskComplete, s.Visibility))
                    .ToList());
        }, ct);
    }
}
