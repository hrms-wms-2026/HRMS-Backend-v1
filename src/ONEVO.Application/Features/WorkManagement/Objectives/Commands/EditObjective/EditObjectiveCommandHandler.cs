using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;

public class EditObjectiveCommandHandler : IRequestHandler<EditObjectiveCommand, Result<ObjectiveEditOutcomeResponse>>
{
    // Payloads are stored camelCase so the frontend can JSON.parse PayloadJson directly (matches
    // its own DTO field names) instead of only ever seeing System.Text.Json's PascalCase default.
    public static readonly JsonSerializerOptions PayloadJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;

    public EditObjectiveCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IObjectiveChangeRequestRepository changeRequests, IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _changeRequests = changeRequests;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _notifications = notifications;
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

        if (objective.IsAchieved)
            return Result<ObjectiveEditOutcomeResponse>.Failure("An achieved milestone cannot be edited.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<ObjectiveEditOutcomeResponse>.Forbidden("Only this milestone's head can edit it.");

        // Every non-default Objective always has a parent (Task 5 sets ParentObjectiveId at
        // creation).
        if (objective.ParentObjectiveId is null)
            return Result<ObjectiveEditOutcomeResponse>.NotFound("Parent objective not found.");

        // At most one pending change request per Objective (design intent) - otherwise a second
        // edit could later be silently overwritten by a stale pending request's own approval.
        if (await _changeRequests.HasPendingForObjectiveAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveEditOutcomeResponse>.Conflict("A change request is already pending for this objective.");

        // Every edit always routes through the Reporting Manager / parent milestone's head for
        // approval - the head of a milestone can no longer apply their own edits directly,
        // regardless of whether the edit conflicts with the parent's constraints or who created
        // the milestone. The approver validates parent-constraint conflicts (and may adjust the
        // requested fields) at approval time - see ApproveObjectiveChangeRequestCommandHandler.
        var payload = new EditObjectiveRequestPayload(request.Title.Trim(), request.Description?.Trim(), request.StartDate, request.EndDate, request.AllocatedHours);
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [callerEmployeeId.Value], ct);
        var requesterDisplayName = names.GetValueOrDefault(callerEmployeeId.Value) ?? "A teammate";

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var changeRequest = new ObjectiveChangeRequest
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ObjectiveId = objective.Id,
                RequestType = ObjectiveChangeRequestTypes.Edit,
                RequestedById = callerEmployeeId.Value,
                // Objective.ReportingManagerId is only ever null for the Default Objective, already
                // excluded above - safe to unwrap here.
                ReportingManagerId = objective.ReportingManagerId!.Value,
                Status = ObjectiveChangeRequestStatuses.Pending,
                PayloadJson = JsonSerializer.Serialize(payload, PayloadJsonOptions),
                CreatedById = userId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _changeRequests.AddAsync(changeRequest, innerCt);

            var manager = await _membership.GetActiveAssigneeAsync(tenantId, objective.ReportingManagerId!.Value, innerCt);
            if (manager is not null)
            {
                await _notifications.SendTemplatedAsync(
                    tenantId, manager.UserId, "work_objective_edit_request_created",
                    new Dictionary<string, string>
                    {
                        ["requesterName"] = requesterDisplayName,
                        ["objectiveName"] = objective.Title
                    },
                    "objective_change_request", changeRequest.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<ObjectiveEditOutcomeResponse>.Success(
                new ObjectiveEditOutcomeResponse(Applied: false, Objective: null, ObjectiveMapper.ToResponse(changeRequest)));
        }, ct);
    }
}
