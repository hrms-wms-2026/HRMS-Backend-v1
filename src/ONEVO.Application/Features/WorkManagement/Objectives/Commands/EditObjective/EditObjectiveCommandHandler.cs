using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;

/// <summary>
/// Edits a milestone through the approval engine: the parent's owner (or anyone above) edits it now;
/// the milestone's own head or members below file a module.edit request to the parent's owner, who
/// may adjust the fields before approving.
/// </summary>
public class EditObjectiveCommandHandler : IRequestHandler<EditObjectiveCommand, Result<ObjectiveEditOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IModuleWriteService _modules;
    private readonly IModuleActionSubmitter _submitter;

    public EditObjectiveCommandHandler(
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

    public async Task<Result<ObjectiveEditOutcomeResponse>> Handle(EditObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveEditOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveEditOutcomeResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveEditOutcomeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveEditOutcomeResponse>.NotFound("Objective not found.");

        // Default-Objective carve-out (design §5) - edited only via PUT /projects/{id}.
        if (objective.IsDefault)
            return Result<ObjectiveEditOutcomeResponse>.Failure("Use the Project edit endpoint for the Default Objective.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<ObjectiveEditOutcomeResponse>.Forbidden("Only this milestone's head can edit it.");

        var input = new ModuleEditInput(request.Title.Trim(), request.Description?.Trim(), request.StartDate, request.EndDate, request.AllocatedHours);
        var validation = await _modules.ValidateEditAsync(tenantId, objective, input, ct);
        if (!validation.IsSuccess)
            return Result<ObjectiveEditOutcomeResponse>.Failure(validation.Error!, validation.StatusCode ?? 400);

        var outcome = await _submitter.SubmitAsync(tenantId, callerEmployeeId.Value, objective, WorkActionTypes.ModuleEdit, input,
            (tracked, innerCt) => _modules.ApplyEditAsync(tenantId, tracked, input, innerCt), ct: ct);
        if (!outcome.IsSuccess)
            return Result<ObjectiveEditOutcomeResponse>.Failure(outcome.Error!, outcome.StatusCode ?? 400);

        return Result<ObjectiveEditOutcomeResponse>.Success(outcome.Value!.Applied
            ? new ObjectiveEditOutcomeResponse(true, ObjectiveMapper.ToDetail(outcome.Value.Module!), null)
            : new ObjectiveEditOutcomeResponse(false, null, outcome.Value.ApprovalRequestId));
    }
}
