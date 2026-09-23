using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Commands.RestoreClockInPolicy;

public class RestoreClockInPolicyCommandHandler
    : IRequestHandler<RestoreClockInPolicyCommand, Result<bool>>
{
    private readonly IClockInPolicyRepository _policies;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RestoreClockInPolicyCommandHandler(
        IClockInPolicyRepository policies,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _policies = policies;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<bool>> Handle(RestoreClockInPolicyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<bool>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<bool>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity is null)
            return Result<bool>.NotFound("Legal entity not found.");
        if (!legalEntity.IsActive)
            return Result<bool>.Conflict("Legal entity is inactive.");

        var policy = await _policies.GetTrackedByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.PolicyId, ct);
        if (policy is null)
            return Result<bool>.NotFound("Clock-in policy not found.");

        if (policy.IsActive)
            return Result<bool>.Success(true);

        if (await _policies.HasOverlappingActiveScopeAsync(
                tenantId,
                request.LegalEntityId,
                policy.ScopeType,
                policy.DepartmentIds,
                policy.PositionIds,
                policy.EmployeeIds,
                policy.EffectiveFrom,
                policy.EffectiveTo,
                excludingPolicyId: policy.Id,
                ct))
        {
            return Result<bool>.Conflict(
                "Cannot restore: an active clock-in policy with overlapping effective dates already exists for this scope.");
        }

        policy.IsActive = true;
        policy.UpdatedAt = _dateTimeProvider.UtcNow;
        _policies.Update(policy);
        await _policies.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
