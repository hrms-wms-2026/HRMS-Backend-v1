using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListLegalEntities;

public class ListLegalEntitiesQueryHandler
    : IRequestHandler<ListLegalEntitiesQuery, Result<IReadOnlyList<LegalEntityListItemResponse>>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public ListLegalEntitiesQueryHandler(ILegalEntityRepository legalEntities, ICurrentUser currentUser)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<LegalEntityListItemResponse>>> Handle(
        ListLegalEntitiesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LegalEntityListItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<LegalEntityListItemResponse>>.Forbidden("Tenant context missing.");

        // "Management access" deliberately checks legal_entity:update/delete, not
        // org:manage - org:manage still gates Department/Position management and
        // general org navigation, but must not by itself unlock every company in the
        // tenant for the selector (see the permission-model rework this handler is
        // part of).
        var hasManagementAccess =
            _currentUser.HasPermission("legal_entity:update") || _currentUser.HasPermission("legal_entity:delete");

        var entities = await _legalEntities.ListAccessibleAsync(
            tenantId, _currentUser.UserId, hasManagementAccess, request.IncludeInactive, ct);

        var items = entities.Select(LegalEntityMapper.ToListItemResponse).ToList();

        return Result<IReadOnlyList<LegalEntityListItemResponse>>.Success(items);
    }
}
