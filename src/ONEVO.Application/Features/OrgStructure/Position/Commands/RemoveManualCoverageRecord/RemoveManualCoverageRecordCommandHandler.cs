using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.RemoveManualCoverageRecord;

public class RemoveManualCoverageRecordCommandHandler
    : IRequestHandler<RemoveManualCoverageRecordCommand, Result<bool>>
{
    private readonly IPositionRepository _positions;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public RemoveManualCoverageRecordCommandHandler(
        IPositionRepository positions,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _positions = positions;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<bool>> Handle(RemoveManualCoverageRecordCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<bool>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<bool>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<bool>.NotFound("Legal entity not found.");

        var owner = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (owner == null)
            return Result<bool>.NotFound("Position not found.");

        var record = await _positions.GetCoverageRecordByIdAsync(tenantId, request.CoverageId, ct);
        // A record found under a different owner/legal entity than the route specifies is treated
        // the same as "not found" here, rather than leaking its existence via a different error.
        if (record == null || record.OwnerPositionId != owner.Id || record.LegalEntityId != request.LegalEntityId)
            return Result<bool>.NotFound("Coverage record not found.");

        if (record.IsLocked)
        {
            return Result<bool>.Conflict(
                "This access comes from the reporting structure. Change the position's Reports to value to remove it.");
        }

        _positions.RemoveCoverageRecord(record);
        await _positions.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
