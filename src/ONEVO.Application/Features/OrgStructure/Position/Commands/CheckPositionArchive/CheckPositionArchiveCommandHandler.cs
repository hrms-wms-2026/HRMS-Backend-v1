using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Services;

namespace ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;

public class CheckPositionArchiveCommandHandler
    : IRequestHandler<CheckPositionArchiveCommand, Result<PositionArchiveBlockers>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public CheckPositionArchiveCommandHandler(
        IPositionRepository positions,
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _positions = positions;
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<PositionArchiveBlockers>> Handle(
        CheckPositionArchiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PositionArchiveBlockers>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PositionArchiveBlockers>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<PositionArchiveBlockers>.NotFound("Legal entity not found.");

        var existing = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (existing == null)
            return Result<PositionArchiveBlockers>.NotFound("Position not found.");

        var blockers = await PositionArchiveDependencyEvaluator.EvaluateAsync(
            _positions, _departments, tenantId, request.LegalEntityId, existing.Id, ct);

        return Result<PositionArchiveBlockers>.Success(blockers);
    }
}
