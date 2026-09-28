using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.Mappers;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;

public class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskAssetLinker _assetLinker;
    private readonly ITaskWriteService _writes;

    public CreateTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership, ITaskAssetLinker assetLinker,
        ITaskWriteService writes)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _assetLinker = assetLinker;
        _writes = writes;
    }

    public async Task<Result<WorkTaskResponse>> Handle(CreateTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<WorkTaskResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can create tasks directly. Non-owner members must submit a task creation request.");

        var input = new TaskCreateInput(objective.Id, request.Title.Trim(), request.Description?.Trim(), request.CategoryId,
            request.Priority, request.DueDate, request.EstimatedHours, request.StoryPoints, request.SprintId);

        var validation = await _writes.ValidateCreateAsync(tenantId, input, ct);
        if (!validation.IsSuccess)
            return Result<WorkTaskResponse>.Failure(validation.Error!, validation.StatusCode ?? 400);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var created = await _writes.CreateAsync(tenantId, userId, callerEmployeeId.Value, input, objective.Id, innerCt);
            if (!created.IsSuccess)
                return Result<WorkTaskResponse>.Failure(created.Error!, created.StatusCode ?? 400);
            var task = created.Value!;

            await _unitOfWork.SaveChangesAsync(innerCt);

            await _assetLinker.SyncAttachmentsAsync(tenantId, userId, task.Id, request.AttachmentFileIds ?? Array.Empty<Guid>(), innerCt);
            await _assetLinker.SyncDescriptionImagesAsync(tenantId, userId, task.Id, task.Description, innerCt);

            return Result<WorkTaskResponse>.Success(WorkTaskResponseMapper.ToResponse(task));
        }, ct);
    }
}
