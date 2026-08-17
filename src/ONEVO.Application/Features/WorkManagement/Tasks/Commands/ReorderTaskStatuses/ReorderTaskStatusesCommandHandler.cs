using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;

public class ReorderTaskStatusesCommandHandler : IRequestHandler<ReorderTaskStatusesCommand, Result<IReadOnlyList<TaskStatusResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;

    public ReorderTaskStatusesCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ITaskStatusRepository statuses, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<TaskStatusResponse>>> Handle(ReorderTaskStatusesCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<IReadOnlyList<TaskStatusResponse>>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Only this milestone's owner can restructure the board.");

        // Defense in depth beyond the validator (which runs in the MediatR pipeline in production,
        // but not when a test calls Handle directly) - exactly one complete status, always.
        if (request.Updates.Count(u => u.MarksTaskComplete) != 1)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("Exactly one status must be marked as the complete status.", 422);

        var existing = await _statuses.GetByObjectiveIdAsync(tenantId, objective.Id, ct);
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
