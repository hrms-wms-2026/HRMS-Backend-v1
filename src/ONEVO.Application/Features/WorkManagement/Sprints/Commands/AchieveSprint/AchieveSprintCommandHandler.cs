using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;

public class AchieveSprintCommandHandler : IRequestHandler<AchieveSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IProjectMemberRepository _members;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public AchieveSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ISprintRepository sprints, IProjectMemberRepository members, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _sprints = sprints;
        _members = members;
        _membership = membership;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(AchieveSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, sprint.ObjectiveId, ct);
        if (objective is null)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can achieve sprints.");

        if (sprint.Status == SprintStatuses.Achieved)
            return Result<SprintResponse>.Conflict("This sprint has already been achieved.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            sprint.Status = SprintStatuses.Achieved;
            sprint.AchievedAt = DateTimeOffset.UtcNow;
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            var members = await _members.ListActiveForObjectiveAsync(tenantId, objective.Id, innerCt);
            foreach (var member in members)
            {
                var assignee = await _membership.GetActiveAssigneeAsync(tenantId, member.EmployeeId, innerCt);
                if (assignee is null) continue;

                await _notifications.SendTemplatedAsync(
                    tenantId, assignee.UserId, "work_sprint_achieved",
                    new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = objective.Title },
                    "sprint", sprint.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(new SprintResponse(
                sprint.Id, sprint.ObjectiveId, sprint.Name, sprint.StartDate, sprint.EndDate, sprint.Status,
                sprint.CompletedAt, sprint.AchievedAt));
        }, ct);
    }
}
