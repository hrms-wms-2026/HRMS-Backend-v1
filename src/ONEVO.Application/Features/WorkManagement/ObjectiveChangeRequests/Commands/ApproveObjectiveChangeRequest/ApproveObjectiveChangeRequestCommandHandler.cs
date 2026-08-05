using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;

public class ApproveObjectiveChangeRequestCommandHandler : IRequestHandler<ApproveObjectiveChangeRequestCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveChangeRequestRepository _changeRequests;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveObjectiveChangeRequestCommandHandler(
        ICurrentUser currentUser, IObjectiveChangeRequestRepository changeRequests,
        IObjectiveRepository objectives, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _changeRequests = changeRequests;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ApproveObjectiveChangeRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var changeRequest = await _changeRequests.GetByIdForTenantAsync(tenantId, request.RequestId, ct);
        if (changeRequest is null)
            return Result.NotFound("Change request not found.");

        if (changeRequest.ReportingManagerId != userId)
            return Result.Forbidden("Only this request's reporting manager can approve it.");

        if (changeRequest.Status != ObjectiveChangeRequestStatuses.Pending)
            return Result.Conflict("This request has already been decided.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, changeRequest.ObjectiveId, ct);
        if (objective is null)
            return Result.NotFound("Objective not found.");

        var now = DateTimeOffset.UtcNow;

        switch (changeRequest.RequestType)
        {
            case ObjectiveChangeRequestTypes.Delete:
                objective.IsActive = false;
                objective.UpdatedAt = now;
                break;

            case ObjectiveChangeRequestTypes.Edit:
                var editPayload = JsonSerializer.Deserialize<EditObjectiveRequestPayload>(changeRequest.PayloadJson!)!;
                objective.Title = editPayload.Title;
                objective.Description = editPayload.Description;
                objective.StartDate = editPayload.StartDate;
                objective.EndDate = editPayload.EndDate;
                objective.AllocatedHours = editPayload.AllocatedHours;
                objective.UpdatedAt = now;
                break;

            case ObjectiveChangeRequestTypes.Transfer:
                var transferPayload = JsonSerializer.Deserialize<TransferObjectiveRequestPayload>(changeRequest.PayloadJson!)!;
                objective.OwnerId = transferPayload.NewHeadUserId;
                objective.UpdatedAt = now;
                break;
        }

        _objectives.Update(objective);

        changeRequest.Status = ObjectiveChangeRequestStatuses.Approved;
        changeRequest.DecidedAt = now;
        changeRequest.DecidedById = userId;
        _changeRequests.Update(changeRequest);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
