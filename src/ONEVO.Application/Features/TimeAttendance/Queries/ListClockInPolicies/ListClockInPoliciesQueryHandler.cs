using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Mappers;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Queries.ListClockInPolicies;

public class ListClockInPoliciesQueryHandler
    : IRequestHandler<ListClockInPoliciesQuery, Result<IReadOnlyList<ClockInPolicyListItemResponse>>>
{
    private readonly IClockInPolicyRepository _policies;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public ListClockInPoliciesQueryHandler(
        IClockInPolicyRepository policies,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _policies = policies;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<ClockInPolicyListItemResponse>>> Handle(
        ListClockInPoliciesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ClockInPolicyListItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ClockInPolicyListItemResponse>>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity is null)
            return Result<IReadOnlyList<ClockInPolicyListItemResponse>>.NotFound("Legal entity not found.");

        var policies = await _policies.ListByLegalEntityAsync(
            tenantId, request.LegalEntityId, request.IncludeInactive, ct);

        var items = policies.Select(ClockInPolicyMapper.ToListItem).ToList();
        return Result<IReadOnlyList<ClockInPolicyListItemResponse>>.Success(items);
    }
}
