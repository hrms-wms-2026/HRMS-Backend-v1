using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;

public class GetObjectiveSubtreeQueryHandler : IRequestHandler<GetObjectiveSubtreeQuery, Result<ObjectiveSubtreeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IModuleReadAccess _readAccess;
    private readonly IPermissionResolver _permissionResolver;

    public GetObjectiveSubtreeQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IModuleReadAccess readAccess, IPermissionResolver permissionResolver)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _readAccess = readAccess;
        _permissionResolver = permissionResolver;
    }

    public async Task<Result<ObjectiveSubtreeResponse>> Handle(GetObjectiveSubtreeQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveSubtreeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveSubtreeResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveSubtreeResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null)
            return Result<ObjectiveSubtreeResponse>.NotFound("Objective not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("*");

        if (!hasReadPermission)
        {
            var hasAccess = await _readAccess.CanReadAsync(tenantId, objective, callerEmployeeId.Value, ct);
            if (!hasAccess)
                return Result<ObjectiveSubtreeResponse>.Forbidden("You do not have access to this milestone.");
        }

        var all = await _objectives.GetAllByProjectIdAsync(tenantId, objective.ProjectId, ct);

        var nameLookupIds = all
            .SelectMany(o => new[] { (Guid?)o.OwnerId, o.ReportingManagerId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var namesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, nameLookupIds, ct);

        var parent = objective.ParentObjectiveId is Guid parentId
            ? all.FirstOrDefault(o => o.Id == parentId)
            : null;

        var childrenByParent = all
            .Where(o => o.ParentObjectiveId.HasValue)
            .ToLookup(o => o.ParentObjectiveId!.Value);

        var response = new ObjectiveSubtreeResponse(
            parent is null ? null : ObjectiveMapper.ToDetail(parent, namesByEmployeeId, callerEmployeeId.Value),
            ObjectiveMapper.ToSubtreeNode(objective, childrenByParent, namesByEmployeeId, callerEmployeeId.Value));

        return Result<ObjectiveSubtreeResponse>.Success(response);
    }
}
