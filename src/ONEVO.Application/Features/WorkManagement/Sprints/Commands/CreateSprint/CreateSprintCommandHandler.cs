using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;

public class CreateSprintCommandHandler : IRequestHandler<CreateSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ISprintRepository _sprints;
    private readonly ISprintTaskAssignmentService _assignment;
    private readonly ISprintActivityLogRepository _logs;
    private readonly IUnitOfWork _unitOfWork;

    public CreateSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        IProjectMemberRepository members, IPermissionResolver permissionResolver, ISprintRepository sprints,
        ISprintTaskAssignmentService assignment, ISprintActivityLogRepository logs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _sprints = sprints;
        _assignment = assignment;
        _logs = logs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(CreateSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<SprintResponse>.NotFound("Project not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        if (!hasReadPermission && !await _members.HasActiveMembershipAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only project members can create sprints.");

        var now = DateTimeOffset.UtcNow;
        var sprint = new Sprint
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id,
            Name = request.Name.Trim(), Goal = request.Goal?.Trim(),
            Status = SprintStatuses.Draft, CreatedById = userId, CreatedAt = now
        };

        var prepared = await _assignment.PrepareAsync(tenantId, sprint, request.TaskIds, Array.Empty<Guid>(), callerEmployeeId.Value, ct);
        if (!prepared.IsSuccess)
            return Result<SprintResponse>.Failure(prepared.Error!, prepared.StatusCode ?? 400);
        var changes = prepared.Value!;

        // Capture each ToAdd task's pre-move sprint (Apply overwrites SprintId in place below), so
        // cross-sprint moves can log a tasks_removed entry on the sprint(s) they moved out of.
        var previousSprintGroups = changes.ToAdd
            .Where(t => t.SprintId is not null)
            .GroupBy(t => t.SprintId!.Value)
            .ToList();

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _sprints.AddAsync(sprint, innerCt);
            _assignment.Apply(changes, sprint.Id);
            await _logs.AddAsync(SprintActivityLogFactory.Create(
                tenantId, sprint.Id, callerEmployeeId.Value, SprintActivityActions.Created, toStatus: SprintStatuses.Draft), innerCt);
            if (changes.ToAdd.Count > 0)
                await _logs.AddAsync(SprintActivityLogFactory.Create(
                    tenantId, sprint.Id, callerEmployeeId.Value, SprintActivityActions.TasksAdded,
                    details: new { taskIds = changes.ToAdd.Select(t => t.Id) }), innerCt);
            foreach (var group in previousSprintGroups)
                await _logs.AddAsync(SprintActivityLogFactory.Create(
                    tenantId, group.Key, callerEmployeeId.Value, SprintActivityActions.TasksRemoved,
                    details: new { taskIds = group.Select(t => t.Id) }), innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<SprintResponse>.Success(SprintResponse.From(sprint, canManage: true));
        }, ct);
    }
}
