using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure;
using ONEVO.Application.Features.Storage.File.Helpers;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListLegalEntities;

public class ListLegalEntitiesQueryHandler
    : IRequestHandler<ListLegalEntitiesQuery, Result<IReadOnlyList<LegalEntityListItemResponse>>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ICurrentUser _currentUser;

    public ListLegalEntitiesQueryHandler(ILegalEntityRepository legalEntities, IEntityAssetRepository entityAssets, ICurrentUser currentUser)
    {
        _legalEntities = legalEntities;
        _entityAssets = entityAssets;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<LegalEntityListItemResponse>>> Handle(
        ListLegalEntitiesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LegalEntityListItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<LegalEntityListItemResponse>>.Forbidden("Tenant context missing.");

        var hasManagementAccess = LegalEntityAccessPolicy.HasManagementAccess(_currentUser);

        var entities = await _legalEntities.ListAccessibleAsync(
            tenantId, _currentUser.UserId, hasManagementAccess, request.IncludeInactive, ct);

        var logoFileIds = await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
            tenantId, EntityAssetOwnerTypes.LegalEntity, entities.Select(e => e.Id).ToList(), UploadPurposeCatalog.CompanyLogo, ct);
        var items = entities
            .Select(e => LegalEntityMapper.ToListItemResponse(e, logoFileIds.TryGetValue(e.Id, out var fid) ? (Guid?)fid : null))
            .ToList();

        return Result<IReadOnlyList<LegalEntityListItemResponse>>.Success(items);
    }
}
