using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;

namespace ONEVO.Application.Features.OrgStructure.Commands.RemoveLegalEntityLogoAsset;

public sealed class RemoveLegalEntityLogoAssetCommandHandler
    : IRequestHandler<RemoveLegalEntityLogoAssetCommand, Result>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IPrimaryEntityAssetLinker _linker;
    private readonly ICurrentUser _currentUser;

    public RemoveLegalEntityLogoAssetCommandHandler(
        ILegalEntityRepository legalEntities,
        IPrimaryEntityAssetLinker linker,
        ICurrentUser currentUser)
    {
        _legalEntities = legalEntities;
        _linker = linker;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(RemoveLegalEntityLogoAssetCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var entity = await _legalEntities.GetAccessibleByIdAsync(
            tenantId, request.LegalEntityId, _currentUser.UserId, true, ct);
        if (entity is null)
            return Result.NotFound("Company not found.");

        return await _linker.RemoveAsync(
            tenantId, _currentUser.UserId, EntityAssetOwnerTypes.LegalEntity, entity.Id,
            UploadPurposeCatalog.CompanyLogo, ct);
    }
}
