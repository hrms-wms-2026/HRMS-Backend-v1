using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;

namespace ONEVO.Application.Features.OrgStructure.Commands.LinkLegalEntityLogo;

public sealed class LinkLegalEntityLogoCommandHandler
    : IRequestHandler<LinkLegalEntityLogoCommand, Result<LegalEntityLogoResponse>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IPrimaryEntityAssetLinker _linker;
    private readonly ICurrentUser _currentUser;

    public LinkLegalEntityLogoCommandHandler(
        ILegalEntityRepository legalEntities,
        IPrimaryEntityAssetLinker linker,
        ICurrentUser currentUser)
    {
        _legalEntities = legalEntities;
        _linker = linker;
        _currentUser = currentUser;
    }

    public async Task<Result<LegalEntityLogoResponse>> Handle(
        LinkLegalEntityLogoCommand request,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LegalEntityLogoResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var entity = await _legalEntities.GetAccessibleByIdAsync(
            tenantId, request.LegalEntityId, _currentUser.UserId, true, ct);
        if (entity is null)
            return Result<LegalEntityLogoResponse>.NotFound("Company not found.");

        var result = await _linker.LinkAsync(
            tenantId, _currentUser.UserId, EntityAssetOwnerTypes.LegalEntity, entity.Id,
            UploadPurposeCatalog.CompanyLogo, request.FileId, ct);
        return result.IsSuccess
            ? Result<LegalEntityLogoResponse>.Success(new LegalEntityLogoResponse(entity.Id, result.Value))
            : Result<LegalEntityLogoResponse>.Failure(result.Error!, result.StatusCode ?? 400);
    }
}
