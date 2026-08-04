using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.RemoveLegalEntityLogo;

public class RemoveLegalEntityLogoCommandHandler : IRequestHandler<RemoveLegalEntityLogoCommand, Result>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public RemoveLegalEntityLogoCommandHandler(ILegalEntityRepository legalEntities, ICurrentUser currentUser)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(RemoveLegalEntityLogoCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var entity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (entity is null)
            return Result.NotFound("Company not found.");

        // Only clears the FK column - the underlying file_records row is an
        // IFileStorageService concern, never touched from here.
        entity.LogoFileId = null;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        _legalEntities.Update(entity);
        await _legalEntities.SaveChangesAsync(ct);

        return Result.Success();
    }
}
