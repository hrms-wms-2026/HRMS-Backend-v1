using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetSprintActivity;

public sealed class GetSprintActivityQueryHandler : IRequestHandler<GetSprintActivityQuery, Result<IReadOnlyList<SprintActivityResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ISprintRepository _sprints;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ISprintActivityLogRepository _logs;

    public GetSprintActivityQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        ISprintRepository sprints,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        ISprintActivityLogRepository logs)
    {
        _currentUser = currentUser;
        _identity = identity;
        _sprints = sprints;
        _members = members;
        _permissionResolver = permissionResolver;
        _logs = logs;
    }

    public async Task<Result<IReadOnlyList<SprintActivityResponse>>> Handle(GetSprintActivityQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<SprintActivityResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<SprintActivityResponse>>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<SprintActivityResponse>>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<IReadOnlyList<SprintActivityResponse>>.NotFound("Sprint not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        if (!hasReadPermission && !await _members.HasActiveMembershipAsync(tenantId, sprint.ProjectId, callerEmployeeId.Value, ct))
            return Result<IReadOnlyList<SprintActivityResponse>>.Forbidden("You do not have access to this project.");

        var logs = await _logs.GetForSprintAsync(tenantId, sprint.Id, ct);
        return Result<IReadOnlyList<SprintActivityResponse>>.Success(logs.Select(l => new SprintActivityResponse(
            l.Id, l.EmployeeId, l.Action, l.FromStatus, l.ToStatus, l.DetailsJson, l.OccurredAt)).ToList());
    }
}
