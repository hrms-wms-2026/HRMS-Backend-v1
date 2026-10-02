using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;

/// <summary>Marks a milestone Achieved through the approval engine (the parent's owner or above applies it now; others request).</summary>
public class AchieveObjectiveCommandHandler : IRequestHandler<AchieveObjectiveCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IModuleWriteService _modules;
    private readonly IModuleActionSubmitter _submitter;

    public AchieveObjectiveCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership, IModuleWriteService modules, IModuleActionSubmitter submitter)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _membership = membership;
        _modules = modules;
        _submitter = submitter;
    }

    public async Task<Result<ObjectiveChangeOutcomeResponse>> Handle(AchieveObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveChangeOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsDefault)
            return Result<ObjectiveChangeOutcomeResponse>.Failure("Use the Project achieve endpoint for the Default Objective.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's head can achieve it.");

        var validation = await _modules.ValidateAchieveAsync(tenantId, objective, ct);
        if (!validation.IsSuccess)
            return Result<ObjectiveChangeOutcomeResponse>.Failure(validation.Error!, validation.StatusCode ?? 400);

        var outcome = await _submitter.SubmitAsync(tenantId, callerEmployeeId.Value, objective, WorkActionTypes.ModuleAchieve, null,
            (tracked, innerCt) => _modules.ApplyAchieveAsync(tenantId, tracked, innerCt), ct: ct);

        return outcome.IsSuccess
            ? Result<ObjectiveChangeOutcomeResponse>.Success(new ObjectiveChangeOutcomeResponse(outcome.Value!.Applied, outcome.Value.ApprovalRequestId))
            : Result<ObjectiveChangeOutcomeResponse>.Failure(outcome.Error!, outcome.StatusCode ?? 400);
    }
}
