using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;

public class GetObjectiveByIdQueryHandler : IRequestHandler<GetObjectiveByIdQuery, Result<ObjectiveDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IModuleReadAccess _readAccess;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IEntityAssetRepository _assets;

    public GetObjectiveByIdQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IModuleReadAccess readAccess, IPermissionResolver permissionResolver, IEntityAssetRepository assets)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _readAccess = readAccess;
        _permissionResolver = permissionResolver;
        _assets = assets;
    }

    public async Task<Result<ObjectiveDetailResponse>> Handle(GetObjectiveByIdQuery request, CancellationToken ct)
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

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Objective not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("*");

        if (!hasReadPermission)
        {
            var hasAccess = await _readAccess.CanReadAsync(tenantId, objective, callerEmployeeId.Value, ct);
            if (!hasAccess)
                return Result<ObjectiveDetailResponse>.Forbidden("You do not have access to this milestone.");
        }

        var nameLookupIds = new List<Guid> { objective.OwnerId };
        if (objective.ReportingManagerId.HasValue)
            nameLookupIds.Add(objective.ReportingManagerId.Value);

        var namesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, nameLookupIds, ct);

        var assets = (await _assets.ListByOwnerAsync(tenantId, EntityAssetOwnerTypes.Objective, objective.Id, ct))
            .Where(a => a.AssetPurpose == UploadPurposeCatalog.ObjectiveAsset)
            .Select(a => new ObjectiveAssetResponse(
                a.Id, a.OriginalFileName, a.FileSizeBytes, a.ContentType, a.CreatedAt,
                ObjectiveAssetResponse.DownloadPath(objective.Id, a.Id)))
            .ToList();

        return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective, namesByEmployeeId, callerEmployeeId.Value, assets));
    }
}
