using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ArchiveClockInPolicy;

public class ArchiveClockInPolicyCommandHandler
    : IRequestHandler<ArchiveClockInPolicyCommand, Result<bool>>
{
    private readonly IClockInPolicyRepository _policies;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ArchiveClockInPolicyCommandHandler(
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

    public async Task<Result<bool>> Handle(ArchiveClockInPolicyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<bool>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<bool>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity is null)
            return Result<bool>.NotFound("Legal entity not found.");

        var policy = await _policies.GetTrackedByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.PolicyId, ct);
        if (policy is null)
            return Result<bool>.NotFound("Clock-in policy not found.");

        if (!policy.IsActive)
            return Result<bool>.Success(true);

        policy.IsActive = false;
        policy.UpdatedAt = _dateTimeProvider.UtcNow;
        _policies.Update(policy);
        await _policies.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
