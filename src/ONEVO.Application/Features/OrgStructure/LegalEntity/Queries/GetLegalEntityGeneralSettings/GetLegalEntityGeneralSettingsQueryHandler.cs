using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityGeneralSettings;

public class GetLegalEntityGeneralSettingsQueryHandler
    : IRequestHandler<GetLegalEntityGeneralSettingsQuery, Result<LegalEntityGeneralSettingsResponse>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public GetLegalEntityGeneralSettingsQueryHandler(ILegalEntityRepository legalEntities, ICurrentUser currentUser)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<LegalEntityGeneralSettingsResponse>> Handle(
        GetLegalEntityGeneralSettingsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LegalEntityGeneralSettingsResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<LegalEntityGeneralSettingsResponse>.Forbidden("Tenant context missing.");

        var entity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (entity is null)
            return Result<LegalEntityGeneralSettingsResponse>.NotFound("Company not found.");

        return Result<LegalEntityGeneralSettingsResponse>.Success(LegalEntityMapper.ToGeneralSettingsResponse(entity));
    }
}
