using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Helpers;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;

public class CreateObjectiveCommandHandler : IRequestHandler<CreateObjectiveCommand, Result<ObjectiveDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;

    public CreateObjectiveCommandHandler(ICurrentUser currentUser, IObjectiveRepository objectives, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveDetailResponse>> Handle(CreateObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveDetailResponse>.Forbidden("Tenant context missing.");

        var parent = await _objectives.GetByIdForTenantAsync(tenantId, request.ParentObjectiveId, ct);
        if (parent is null || !parent.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Parent objective not found.");

        // Free-control rule (design §4): only the parent's current Head may create a child under it.
        if (parent.OwnerId != userId)
            return Result<ObjectiveDetailResponse>.Forbidden("Only the parent milestone's head can create a sub-milestone under it.");

        if (ObjectiveParentConstraintChecker.Conflicts(parent, request.StartDate, request.EndDate, request.AllocatedHours))
            return Result<ObjectiveDetailResponse>.Failure(
                "The new milestone's date range or allocated hours would exceed the parent milestone's.");

        var now = DateTimeOffset.UtcNow;

        var objective = new Objective
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProjectId = parent.ProjectId,
            ParentObjectiveId = parent.Id,
            IsDefault = false,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            // Head defaults to the creator if not explicitly assigned (design §5).
            OwnerId = request.HeadUserId ?? userId,
            // Always the creator, regardless of who is assigned Head - a one-time fact set at
            // creation and never touched again, including by Transfer (Task 8).
            ReportingManagerId = userId,
            IsActive = true,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Progress = 0m,
            AllocatedHours = request.AllocatedHours,
            CompletedHours = 0m,
            CreatedById = userId,
            CreatedAt = now
        };

        await _objectives.AddAsync(objective, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective));
    }
}
