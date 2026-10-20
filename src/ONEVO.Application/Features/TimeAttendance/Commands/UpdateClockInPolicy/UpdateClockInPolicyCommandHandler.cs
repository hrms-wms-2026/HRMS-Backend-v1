using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Mappers;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Commands.UpdateClockInPolicy;

public class UpdateClockInPolicyCommandHandler
    : IRequestHandler<UpdateClockInPolicyCommand, Result<ClockInPolicyResponse>>
{
    private readonly IClockInPolicyRepository _policies;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IClockInPolicyScopeMembershipValidator _scopeMembership;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateClockInPolicyCommandHandler(
        IClockInPolicyRepository policies,
        ILegalEntityRepository legalEntities,
        IClockInPolicyScopeMembershipValidator scopeMembership,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _policies = policies;
        _legalEntities = legalEntities;
        _scopeMembership = scopeMembership;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<ClockInPolicyResponse>> Handle(
        UpdateClockInPolicyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ClockInPolicyResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<ClockInPolicyResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity is null)
            return Result<ClockInPolicyResponse>.NotFound("Legal entity not found.");
        if (!legalEntity.IsActive)
            return Result<ClockInPolicyResponse>.Conflict("Legal entity is inactive.");

        var policy = await _policies.GetTrackedByIdForLegalEntityAsync(
            tenantId, request.LegalEntityId, request.PolicyId, ct);
        if (policy is null)
            return Result<ClockInPolicyResponse>.NotFound("Clock-in policy not found.");

        var scopeError = await _scopeMembership.ValidateAsync(
            tenantId, request.LegalEntityId, request.Scope, ct);
        if (scopeError is not null)
            return Result<ClockInPolicyResponse>.Failure(scopeError.Error!, scopeError.StatusCode ?? 400);

        var departmentIds = ClockInPolicyMapper.NormalizeIds(request.Scope.DepartmentIds);
        var positionIds = ClockInPolicyMapper.NormalizeIds(request.Scope.PositionIds);
        var employeeIds = ClockInPolicyMapper.NormalizeIds(request.Scope.EmployeeIds);

        if (request.IsActive
            && await _policies.HasOverlappingActiveScopeAsync(
                tenantId,
                request.LegalEntityId,
                request.Scope.Type.Trim(),
                departmentIds,
                positionIds,
                employeeIds,
                request.EffectiveFrom,
                request.EffectiveTo,
                excludingPolicyId: policy.Id,
                ct))
        {
            return Result<ClockInPolicyResponse>.Conflict(
                "An active clock-in policy with overlapping effective dates already exists for this scope.");
        }

        var now = _dateTimeProvider.UtcNow;
        // Who the rows apply to, and when: widening it must not back-charge the newly covered days either.
        var coverageBefore = (policy.ScopeType, Ids(policy.DepartmentIds), Ids(policy.PositionIds), Ids(policy.EmployeeIds),
            policy.EffectiveFrom, policy.EffectiveTo, policy.IsActive);
        policy.Name = request.Name.Trim();
        policy.EffectiveFrom = request.EffectiveFrom;
        policy.EffectiveTo = request.EffectiveTo;
        policy.CorrectionRequiresApproval = request.CorrectionRequiresApproval;
        policy.NotificationRecipientResolver = request.NotificationRecipientResolver.Trim();
        policy.IsActive = request.IsActive;
        policy.UpdatedAt = now;

        ClockInPolicyMapper.ApplyScope(policy, request.Scope);

        // Days before the rows last changed are never newly charged, so only a real change moves the stamp
        // (the rows are recreated on every save, so compare their content, not their ids). Rows saved before
        // deductions existed carry no stamp; the first save stamps them, so they only charge from then on.
        var coverageAfter = (policy.ScopeType, Ids(policy.DepartmentIds), Ids(policy.PositionIds), Ids(policy.EmployeeIds),
            policy.EffectiveFrom, policy.EffectiveTo, policy.IsActive);
        if ((policy.DeductionRulesChangedAt is null && request.LateDeductionRules.Count > 0)
            || coverageBefore != coverageAfter
            || !ClockInPolicyRules.SameRows(
                policy.LateDeductionRules.Where(r => r.IsActive).Select(r => (r.LateArrivalMinute, r.Multiplier, r.TimeOffTypeId)),
                request.LateDeductionRules.Select(r => (r.LateArrivalMinute, r.Multiplier, r.TimeOffTypeId))))
            policy.DeductionRulesChangedAt = now;

        policy.LateDeductionRules.Clear();
        foreach (var rule in request.LateDeductionRules.OrderBy(r => r.LateArrivalMinute))
        {
            policy.LateDeductionRules.Add(new ClockInLateDeductionRule
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ClockInPolicyId = policy.Id,
                LateArrivalMinute = rule.LateArrivalMinute,
                Multiplier = rule.Multiplier,
                TimeOffTypeId = rule.TimeOffTypeId,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        _policies.Update(policy);
        await _policies.SaveChangesAsync(ct);

        return Result<ClockInPolicyResponse>.Success(ClockInPolicyMapper.ToResponse(policy));
    }

    /// <summary>Order-insensitive comparable form of a scope id list.</summary>
    private static string Ids(Guid[]? ids) => ids is null ? "" : string.Join(",", ids.Order());
}
