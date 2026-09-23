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

namespace ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityGeneralSettings;

public class GetLegalEntityGeneralSettingsQueryHandler
    : IRequestHandler<GetLegalEntityGeneralSettingsQuery, Result<LegalEntityGeneralSettingsResponse>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ICurrentUser _currentUser;

    public GetLegalEntityGeneralSettingsQueryHandler(ILegalEntityRepository legalEntities, IEntityAssetRepository entityAssets, ICurrentUser currentUser)
    {
        _legalEntities = legalEntities;
        _entityAssets = entityAssets;
        _currentUser = currentUser;
    }

    public async Task<Result<LegalEntityGeneralSettingsResponse>> Handle(
        GetLegalEntityGeneralSettingsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LegalEntityGeneralSettingsResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<LegalEntityGeneralSettingsResponse>.Forbidden("Tenant context missing.");

        var hasManagementAccess = LegalEntityAccessPolicy.HasManagementAccess(_currentUser);
        var entity = await _legalEntities.GetAccessibleByIdAsync(
            tenantId, request.LegalEntityId, _currentUser.UserId, hasManagementAccess, ct);
        if (entity is null)
            return Result<LegalEntityGeneralSettingsResponse>.NotFound("Company not found.");

        var logoFileIds = await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
            tenantId, EntityAssetOwnerTypes.LegalEntity, new[] { entity.Id }, UploadPurposeCatalog.CompanyLogo, ct);
        var logoFileId = logoFileIds.TryGetValue(entity.Id, out var fid) ? (Guid?)fid : null;

        return Result<LegalEntityGeneralSettingsResponse>.Success(LegalEntityMapper.ToGeneralSettingsResponse(entity, logoFileId));
    }
}
