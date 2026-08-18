using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;

public class CreateSprintCommandHandler : IRequestHandler<CreateSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IUnitOfWork _unitOfWork;

    public CreateSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ISprintRepository sprints, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _sprints = sprints;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(CreateSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        if (request.EndDate < request.StartDate)
            return Result<SprintResponse>.Failure("End date must not be before start date.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can create sprints.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var initialStatus = request.StartDate <= today ? SprintStatuses.Active : SprintStatuses.Future;

            var sprint = new Sprint
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                Name = request.Name.Trim(), StartDate = request.StartDate, EndDate = request.EndDate,
                Status = initialStatus, CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _sprints.AddAsync(sprint, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(new SprintResponse(
                sprint.Id, sprint.ObjectiveId, sprint.Name, sprint.StartDate, sprint.EndDate, sprint.Status,
                sprint.CompletedAt, sprint.AchievedAt));
        }, ct);
    }
}
