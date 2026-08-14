using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Helpers;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.CreateObjective;

public class CreateObjectiveCommandHandler : IRequestHandler<CreateObjectiveCommand, Result<ObjectiveDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IPermissionAutoGrantService _autoGrant;

    public CreateObjectiveCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _autoGrant = autoGrant;
    }

    public async Task<Result<ObjectiveDetailResponse>> Handle(CreateObjectiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveDetailResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveDetailResponse>.Forbidden("No employee record for the current user.");

        var parent = await _objectives.GetByIdForTenantAsync(tenantId, request.ParentObjectiveId, ct);
        if (parent is null || !parent.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Parent objective not found.");

        // Free-control rule (design §4): only the parent's current Head may create a child under it.
        if (parent.OwnerId != callerEmployeeId.Value)
            return Result<ObjectiveDetailResponse>.Forbidden("Only the parent milestone's head can create a sub-milestone under it.");

        if (ObjectiveParentConstraintChecker.Conflicts(parent, request.StartDate, request.EndDate, request.AllocatedHours))
            return Result<ObjectiveDetailResponse>.Failure(
                "The new milestone's date range or allocated hours would exceed the parent milestone's.");

        // Creator always starts as owner (design amendment, Task 11 above) - HeadEmployeeId from
        // the request, if given, is handled entirely by the member-invitations loop Task 11 added,
        // never by assigning ownership directly here.
        var assignee = await _membership.GetActiveAssigneeAsync(tenantId, callerEmployeeId.Value, ct);
        if (assignee is null)
            return Result<ObjectiveDetailResponse>.Failure("The assigned head must be an active employee in this tenant.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
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
                OwnerId = callerEmployeeId.Value,
                // Always the creator's EmployeeId, later kept in sync with the PARENT's current
                // head by Transfer's cascade (design §4, Task 19 below), not by anything in this handler.
                ReportingManagerId = callerEmployeeId.Value,
                IsActive = true,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Progress = 0m,
                AllocatedHours = request.AllocatedHours,
                CompletedHours = 0m,
                CreatedById = userId,
                CreatedAt = now
            };

            await _objectives.AddAsync(objective, innerCt);

            await _membership.UpsertMembershipAsync(tenantId, objective.ProjectId, objective.Id, callerEmployeeId.Value, innerCt);
            await _autoGrant.EnsureGrantedAsync(tenantId, userId, userId, "projects:access", innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective));
        }, ct);
    }
}
