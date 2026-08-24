using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Mappers;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Queries.GetClockInPolicy;

public class GetClockInPolicyQueryHandler
    : IRequestHandler<GetClockInPolicyQuery, Result<ClockInPolicyResponse>>
{
    private readonly IClockInPolicyRepository _policies;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public GetClockInPolicyQueryHandler(
        IClockInPolicyRepository policies,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _policies = policies;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<ClockInPolicyResponse>> Handle(
        GetClockInPolicyQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ClockInPolicyResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<ClockInPolicyResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity is null)
            return Result<ClockInPolicyResponse>.NotFound("Legal entity not found.");

        var policy = await _policies.GetByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.PolicyId, ct);
        if (policy is null)
            return Result<ClockInPolicyResponse>.NotFound("Clock-in policy not found.");

        return Result<ClockInPolicyResponse>.Success(ClockInPolicyMapper.ToResponse(policy));
    }
}
