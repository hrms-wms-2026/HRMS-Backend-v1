using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.DeleteLegalEntity;

public class DeleteLegalEntityCommandHandler : IRequestHandler<DeleteLegalEntityCommand, Result>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public DeleteLegalEntityCommandHandler(ILegalEntityRepository legalEntities, ICurrentUser currentUser)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeleteLegalEntityCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var entity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (entity is null)
            return Result.NotFound("Company not found.");

        if (!string.Equals(entity.Name, request.ConfirmName.Trim(), StringComparison.Ordinal))
            return Result.Failure("Company name confirmation does not match.");

        var activeCount = await _legalEntities.CountActiveByTenantAsync(tenantId, ct);
        if (entity.IsActive && activeCount <= 1)
            return Result.Failure("Cannot delete or deactivate the last active company in the tenant.");

        // Soft delete only - IsActive = false, row preserved for audit/history.
        // Never physically remove the row (no Remove/RemoveRange call here).
        entity.IsActive = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        _legalEntities.Update(entity);
        await _legalEntities.SaveChangesAsync(ct);

        return Result.Success();
    }
}
