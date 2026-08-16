using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskCreationRequest;

public class CreateTaskCreationRequestCommandHandler : IRequestHandler<CreateTaskCreationRequestCommand, Result<TaskCreationRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskCreationRequestRepository _requests;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaskCreationRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership, ITaskCreationRequestRepository requests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _membership = membership;
        _requests = requests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TaskCreationRequestResponse>> Handle(CreateTaskCreationRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskCreationRequestResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<TaskCreationRequestResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<TaskCreationRequestResponse>.NotFound("Objective not found.");

        if (objective.OwnerId == callerEmployeeId.Value)
            return Result<TaskCreationRequestResponse>.Failure("The milestone owner creates tasks directly - no request needed.", 400);

        var isMember = await _membership.IsActiveMemberAsync(tenantId, objective.Id, callerEmployeeId.Value, ct);
        if (!isMember)
            return Result<TaskCreationRequestResponse>.Forbidden("Only active milestone members can request tasks.");

        var payload = new TaskCreationRequestPayload(
            request.Title.Trim(), request.Description?.Trim(), request.TaskType, request.Priority,
            request.DueDate, request.EstimatedHours, request.StoryPoints);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var entity = new TaskCreationRequest
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ObjectiveId = objective.Id,
                RequestedByEmployeeId = callerEmployeeId.Value, PayloadJson = JsonSerializer.Serialize(payload),
                Status = TaskCreationRequestStatuses.Pending, CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _requests.AddAsync(entity, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<TaskCreationRequestResponse>.Success(new TaskCreationRequestResponse(entity.Id, entity.ObjectiveId, entity.Status, payload, entity.CreatedAt));
        }, ct);
    }
}
