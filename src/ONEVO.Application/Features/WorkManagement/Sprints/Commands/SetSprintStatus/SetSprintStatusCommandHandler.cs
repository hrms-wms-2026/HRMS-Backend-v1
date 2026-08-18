using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintStatus;

/// <summary>
/// The Objective owner's manual override - any status, any time, bypassing the normal gates
/// (CompleteSprintCommand's all-tasks-complete check, AchieveSprintCommand's not-already-achieved
/// check). Marks Sprint.IsManuallyOverridden so SprintLifecycleJob's date-driven sweep stops
/// touching this sprint - the override has to stick, not get reverted by the next tick.
/// </summary>
public class SetSprintStatusCommandHandler : IRequestHandler<SetSprintStatusCommand, Result<SprintResponse>>
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.Ordinal)
    {
        SprintStatuses.Future, SprintStatuses.Active, SprintStatuses.Complete,
        SprintStatuses.Incomplete, SprintStatuses.Achieved
    };

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IUnitOfWork _unitOfWork;

    public SetSprintStatusCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ISprintRepository sprints, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _sprints = sprints;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(SetSprintStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        if (!ValidStatuses.Contains(request.Status))
            return Result<SprintResponse>.Failure("Unrecognized sprint status.", 422);

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, sprint.ObjectiveId, ct);
        if (objective is null)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can change a sprint's status.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            sprint.Status = request.Status;
            sprint.CompletedAt = request.Status == SprintStatuses.Complete ? DateTimeOffset.UtcNow : null;
            sprint.AchievedAt = request.Status == SprintStatuses.Achieved ? DateTimeOffset.UtcNow : null;
            sprint.IsManuallyOverridden = true;
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(new SprintResponse(
                sprint.Id, sprint.ObjectiveId, sprint.Name, sprint.StartDate, sprint.EndDate, sprint.Status,
                sprint.CompletedAt, sprint.AchievedAt));
        }, ct);
    }
}
