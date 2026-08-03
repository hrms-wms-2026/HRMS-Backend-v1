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

        var entities = await _legalEntities.ListByTenantAsync(tenantId, ct);

        var filtered = request.IncludeInactive
            ? entities
            : entities.Where(e => e.IsActive).ToList();

        var items = filtered.Select(LegalEntityMapper.ToListItemResponse).ToList();

        return Result<IReadOnlyList<LegalEntityListItemResponse>>.Success(items);
    }
}
