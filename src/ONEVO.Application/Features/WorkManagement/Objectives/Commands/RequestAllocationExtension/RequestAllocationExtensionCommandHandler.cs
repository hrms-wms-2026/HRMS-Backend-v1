using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.RequestAllocationExtension;

/// <summary>
/// More allocated hours for a milestone, through the approval engine: the parent's owner (or above)
/// adds them now; the milestone's head or members request them from the parent's owner, who may
/// approve fewer hours.
/// </summary>
public class RequestAllocationExtensionCommandHandler : IRequestHandler<RequestAllocationExtensionCommand, Result<ObjectiveChangeOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IModuleWriteService _modules;
    private readonly IModuleActionSubmitter _submitter;

    public RequestAllocationExtensionCommandHandler(
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

    public async Task<Result<ObjectiveChangeOutcomeResponse>> Handle(RequestAllocationExtensionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveChangeOutcomeResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Forbidden("Only this milestone's owner can request an allocation extension.");

        var input = new ModuleAllocationExtendInput(request.RequestedAdditionalHours, request.Reason.Trim());
        var validation = await _modules.ValidateAllocationExtendAsync(tenantId, objective, input, ct);
        if (!validation.IsSuccess)
            return Result<ObjectiveChangeOutcomeResponse>.Failure(validation.Error!, validation.StatusCode ?? 400);

        var outcome = await _submitter.SubmitAsync(tenantId, callerEmployeeId.Value, objective, WorkActionTypes.ModuleAllocationExtend, input,
            (tracked, innerCt) => _modules.ApplyAllocationExtendAsync(tenantId, tracked, input, innerCt), ct: ct);

        return outcome.IsSuccess
            ? Result<ObjectiveChangeOutcomeResponse>.Success(new ObjectiveChangeOutcomeResponse(outcome.Value!.Applied, outcome.Value.ApprovalRequestId))
            : Result<ObjectiveChangeOutcomeResponse>.Failure(outcome.Error!, outcome.StatusCode ?? 400);
    }
}
