using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;

// File-ownership validation is deliberately deferred (Part 2C): the codebase's
// FileStorageArchitectureTests forbid any feature outside Storage.File from
// referencing IFileRecordRepository/IFileUploadReservationRepository directly,
// and IFileStorageService (the only allowed entry point) exposes no lookup
// method for "does this fileId belong to this tenant" - only upload/reservation
// flows. Adding one is a Storage-feature change, out of this task's scope.
// This matches the task's explicit fallback: "May be contract/command-only if
// file ownership validation is deferred... update logoFileId only."
public class SetLegalEntityLogoCommandHandler
    : IRequestHandler<SetLegalEntityLogoCommand, Result<LegalEntityLogoResponse>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public SetLegalEntityLogoCommandHandler(ILegalEntityRepository legalEntities, ICurrentUser currentUser)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
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

        entity.LogoFileId = request.FileId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        _legalEntities.Update(entity);
        await _legalEntities.SaveChangesAsync(ct);

        return Result<LegalEntityLogoResponse>.Success(new LegalEntityLogoResponse(entity.Id, entity.LogoFileId));
    }
}
