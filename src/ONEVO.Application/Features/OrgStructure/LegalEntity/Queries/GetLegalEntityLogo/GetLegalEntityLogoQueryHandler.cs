using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityLogo;

public class GetLegalEntityLogoQueryHandler
    : IRequestHandler<GetLegalEntityLogoQuery, Result<FileStreamDto>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorageService _fileStorage;

    public GetLegalEntityLogoQueryHandler(
        ILegalEntityRepository legalEntities, ICurrentUser currentUser, IFileStorageService fileStorage)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    public async Task<Result<FileStreamDto>> Handle(GetLegalEntityLogoQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<FileStreamDto>.Forbidden("Tenant context missing.");

        var entity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (entity is null || entity.LogoFileId is null)
            return Result<FileStreamDto>.NotFound("Company logo not found.");

        return await _fileStorage.OpenReadAsync(tenantId, entity.LogoFileId.Value, ct);
    }
}
