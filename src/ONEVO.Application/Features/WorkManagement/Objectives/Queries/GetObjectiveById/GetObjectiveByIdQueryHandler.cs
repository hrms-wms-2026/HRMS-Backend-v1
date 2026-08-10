using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;

public class GetObjectiveByIdQueryHandler : IRequestHandler<GetObjectiveByIdQuery, Result<ObjectiveDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;

    public GetObjectiveByIdQueryHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives,
        IProjectMemberRepository members, IPermissionResolver permissionResolver)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _members = members;
        _permissionResolver = permissionResolver;
    }

    public async Task<Result<ObjectiveDetailResponse>> Handle(GetObjectiveByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveDetailResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Objective not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var selfAndAncestorIds = new List<Guid> { objective.Id };
            var cursor = objective;
            while (cursor.ParentObjectiveId is not null)
            {
                var parent = await _objectives.GetByIdForTenantAsync(tenantId, cursor.ParentObjectiveId.Value, ct);
                if (parent is null)
                    break;

                selfAndAncestorIds.Add(parent.Id);
                cursor = parent;
            }

            var hasAccess = await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, objective.ProjectId, userId, selfAndAncestorIds, ct);
            if (!hasAccess)
                return Result<ObjectiveDetailResponse>.Forbidden("You do not have access to this milestone.");
        }

        return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective));
    }
}
