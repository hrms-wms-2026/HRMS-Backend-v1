using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Helpers;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;

public class EditObjectiveCommandHandler : IRequestHandler<EditObjectiveCommand, Result<ObjectiveEditOutcomeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IUnitOfWork _unitOfWork;

    public EditObjectiveCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveEditOutcomeResponse>> Handle(EditObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveEditOutcomeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveEditOutcomeResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveEditOutcomeResponse>.NotFound("Objective not found.");

        // Default-Objective carve-out (design §5) - edited only via PUT /projects/{id}.
        if (objective.IsDefault)
            return Result<ObjectiveEditOutcomeResponse>.Failure("Use the Project edit endpoint for the Default Objective.");

        if (objective.IsAchieved)
            return Result<ObjectiveEditOutcomeResponse>.Failure("An achieved milestone cannot be edited.");

        if (objective.OwnerId != userId)
            return Result<ObjectiveEditOutcomeResponse>.Forbidden("Only this milestone's head can edit it.");

        // Every non-default Objective always has a parent (Task 5 sets ParentObjectiveId at
        // creation) - loaded to run the conflict check against it.
        var parent = await _objectives.GetByIdForTenantAsync(tenantId, objective.ParentObjectiveId!.Value, ct);
        if (parent is null)
            return Result<ObjectiveEditOutcomeResponse>.NotFound("Parent objective not found.");

        // At most one pending change request per Objective (design intent) - this gates every
        // edit attempt uniformly, not just the ones that would themselves create a new pending
        // request. Otherwise an immediate edit could apply on top of - and later be silently
        // overwritten by - a stale pending request's eventual approval.
        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveEditOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        var conflicts = ObjectiveParentConstraintChecker.Conflicts(parent, request.StartDate, request.EndDate, request.AllocatedHours);
        var isCreator = objective.CreatedById == userId;

        // Non-conflicting edits always apply immediately, regardless of who's asking. Conflicting
        // edits also apply immediately if the caller is the Objective's own creator - a creator
        // never needs approval for their own creation (design §4).
        if (!conflicts || isCreator)
        {
            var now = DateTimeOffset.UtcNow;
            objective.Title = request.Title.Trim();
            objective.Description = request.Description?.Trim();
            objective.StartDate = request.StartDate;
            objective.EndDate = request.EndDate;
            objective.AllocatedHours = request.AllocatedHours;
            objective.UpdatedAt = now;

            _objectives.Update(objective);
            await _unitOfWork.SaveChangesAsync(ct);

            return Result<ObjectiveEditOutcomeResponse>.Success(
                new ObjectiveEditOutcomeResponse(Applied: true, ObjectiveMapper.ToDetail(objective), PendingRequest: null));
        }

        var payload = new EditObjectiveRequestPayload(request.Title.Trim(), request.Description?.Trim(), request.StartDate, request.EndDate, request.AllocatedHours);

        var changeRequest = new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ObjectiveId = objective.Id,
            RequestType = ObjectiveChangeRequestTypes.Edit,
            RequestedById = userId,
            // Objective.ReportingManagerId is only ever null for the Default Objective, already
            // excluded above - safe to unwrap here.
            ReportingManagerId = objective.ReportingManagerId!.Value,
            Status = ObjectiveChangeRequestStatuses.Pending,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _changeRequests.AddAsync(changeRequest, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ObjectiveEditOutcomeResponse>.Success(
            new ObjectiveEditOutcomeResponse(Applied: false, Objective: null, ObjectiveMapper.ToResponse(changeRequest)));
    }
}
