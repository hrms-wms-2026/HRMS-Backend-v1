using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;

/// <summary>Draft -> Active. The one point where a sprint's dates are ever set - there is no
/// date-driven auto-advance anymore (see SprintLifecycleJob).</summary>
public class StartSprintCommandHandler : IRequestHandler<StartSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ISprintRepository _sprints;
    private readonly ISprintAccessService _access;
    private readonly ISprintActivityLogRepository _logs;
    private readonly IUnitOfWork _unitOfWork;

    public StartSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity,
        ISprintRepository sprints, ISprintAccessService access, ISprintActivityLogRepository logs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _sprints = sprints;
        _access = access;
        _logs = logs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(StartSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        if (request.EndDate < request.StartDate)
            return Result<SprintResponse>.Failure("End date must not be before start date.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        if (!await _access.CanManageAsync(tenantId, sprint, _currentUser.UserId, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only the sprint's creator or an owner of one of its tasks' modules can start this sprint.");

        if (sprint.Status != SprintStatuses.Draft)
            return Result<SprintResponse>.Conflict("Only a Draft sprint can be started.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            sprint.StartDate = request.StartDate;
            sprint.EndDate = request.EndDate;
            if (request.Goal is not null) sprint.Goal = request.Goal.Trim();
            sprint.Status = SprintStatuses.Active;
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            await _logs.AddAsync(SprintActivityLogFactory.Create(
                tenantId, sprint.Id, callerEmployeeId.Value, SprintActivityActions.Started,
                SprintStatuses.Draft, SprintStatuses.Active,
                new { startDate = request.StartDate, endDate = request.EndDate }), innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(SprintResponse.From(sprint, canManage: true));
        }, ct);
    }
}
