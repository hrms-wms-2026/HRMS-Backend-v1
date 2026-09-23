using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;

public class AchieveSprintCommandHandler : IRequestHandler<AchieveSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ISprintRepository _sprints;
    private readonly IProjectRepository _projects;
    private readonly ISprintAccessService _access;
    private readonly ISprintActivityLogRepository _logs;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public AchieveSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ISprintRepository sprints, IProjectRepository projects,
        ISprintAccessService access, ISprintActivityLogRepository logs, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _sprints = sprints;
        _projects = projects;
        _access = access;
        _logs = logs;
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

        if (!await _access.CanManageAsync(tenantId, sprint, _currentUser.UserId, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only the sprint's creator or an owner of one of its tasks' modules can achieve this sprint.");

        if (sprint.Status == SprintStatuses.Achieved)
            return Result<SprintResponse>.Conflict("This sprint has already been achieved.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var fromStatus = sprint.Status;

            sprint.Status = SprintStatuses.Achieved;
            sprint.AchievedAt = DateTimeOffset.UtcNow;
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            await _logs.AddAsync(SprintActivityLogFactory.Create(
                tenantId, sprint.Id, callerEmployeeId.Value, SprintActivityActions.Achieved, fromStatus, SprintStatuses.Achieved), innerCt);

            var project = await _projects.GetByIdForTenantAsync(tenantId, sprint.ProjectId, innerCt);
            var audience = await _access.GetAudienceEmployeeIdsAsync(tenantId, sprint.Id, innerCt);
            foreach (var employeeId in audience)
            {
                var assignee = await _membership.GetActiveAssigneeAsync(tenantId, employeeId, innerCt);
                if (assignee is null) continue;

                await _notifications.SendTemplatedAsync(
                    tenantId, assignee.UserId, "work_sprint_achieved",
                    new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = project?.Name ?? "the project" },
                    "sprint", sprint.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(SprintResponse.From(sprint, canManage: true));
        }, ct);
    }
}
