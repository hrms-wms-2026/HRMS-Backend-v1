using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;

public class SetLegalEntityLogoCommandHandler
    : IRequestHandler<SetLegalEntityLogoCommand, Result<LegalEntityLogoResponse>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorageService _fileStorage;

    public SetLegalEntityLogoCommandHandler(
        ILegalEntityRepository legalEntities, ICurrentUser currentUser, IFileStorageService fileStorage)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    public async Task<Result<LegalEntityLogoResponse>> Handle(SetLegalEntityLogoCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LegalEntityLogoResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<LegalEntityLogoResponse>.Forbidden("Tenant context missing.");

        var entity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (entity is null)
            return Result<LegalEntityLogoResponse>.NotFound("Company not found.");

        var uploadResult = await _fileStorage.UploadAsync(
            tenantId, _currentUser.UserId, request.FileName, request.ContentType,
            UploadPurposeCatalog.CompanyLogo, request.Content, ct);

        if (!uploadResult.IsSuccess)
            return Result<LegalEntityLogoResponse>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 400);

        entity.LogoFileId = uploadResult.Value!.Id;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        _legalEntities.Update(entity);
        await _legalEntities.SaveChangesAsync(ct);

        return Result<LegalEntityLogoResponse>.Success(new LegalEntityLogoResponse(entity.Id, entity.LogoFileId));
    }
}
