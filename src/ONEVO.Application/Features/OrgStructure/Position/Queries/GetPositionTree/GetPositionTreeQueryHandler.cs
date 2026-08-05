using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;

public class GetPositionTreeQueryHandler
    : IRequestHandler<GetPositionTreeQuery, Result<IReadOnlyList<PositionTreeNodeResponse>>>
{
    private readonly IPositionRepository _positions;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public GetPositionTreeQueryHandler(
        IPositionRepository positions,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _positions = positions;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<PositionTreeNodeResponse>>> Handle(
        GetPositionTreeQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<PositionTreeNodeResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<PositionTreeNodeResponse>>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<IReadOnlyList<PositionTreeNodeResponse>>.NotFound("Legal entity not found.");

        var positions = await _positions.ListByLegalEntityAsync(
            tenantId, request.LegalEntityId, request.IncludeInactive, departmentId: null, ct);

        var tree = PositionTreeMapper.BuildTree(positions);

        return Result<IReadOnlyList<PositionTreeNodeResponse>>.Success(tree);
    }
}
