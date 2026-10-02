using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.RemoveObjectiveMember;

/// <summary>
/// Removes a milestone member through the approval engine: the parent's owner (or anyone above) removes
/// them now; the milestone's own head or members below file a module.member_remove request to the
/// parent's owner.
/// </summary>
public class RemoveObjectiveMemberCommandHandler : IRequestHandler<RemoveObjectiveMemberCommand, Result<RemoveObjectiveMemberOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IModuleActionSubmitter _submitter;

    public RemoveObjectiveMemberCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership, IModuleActionSubmitter submitter)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _membership = membership;
        _submitter = submitter;
    }

    public async Task<Result<RemoveObjectiveMemberOutcomeResponse>> Handle(RemoveObjectiveMemberCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<RemoveObjectiveMemberOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<RemoveObjectiveMemberOutcomeResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<RemoveObjectiveMemberOutcomeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<RemoveObjectiveMemberOutcomeResponse>.NotFound("Objective not found.");

        if (objective.IsAchieved)
            return Result<RemoveObjectiveMemberOutcomeResponse>.Failure("Cannot remove members from an achieved milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<RemoveObjectiveMemberOutcomeResponse>.Forbidden("Only this milestone's head or an active member can remove members.");

        if (request.EmployeeId == objective.OwnerId)
            return Result<RemoveObjectiveMemberOutcomeResponse>.Failure("Cannot remove the milestone's head as a member - use Transfer instead.");

        var input = new ModuleMemberRemoveInput(request.EmployeeId);
        var outcome = await _submitter.SubmitAsync(tenantId, callerEmployeeId.Value, objective, WorkActionTypes.ModuleMemberRemove, input,
            (tracked, innerCt) => _membership.ApplyMemberRemoveAsync(tenantId, tracked, request.EmployeeId, innerCt),
            extraRecipients: [request.EmployeeId], ct: ct);

        return outcome.IsSuccess
            ? Result<RemoveObjectiveMemberOutcomeResponse>.Success(new RemoveObjectiveMemberOutcomeResponse(outcome.Value!.Applied, outcome.Value.ApprovalRequestId))
            : Result<RemoveObjectiveMemberOutcomeResponse>.Failure(outcome.Error!, outcome.StatusCode ?? 400);
    }
}
