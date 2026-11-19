using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Tests.Unit.Features.Leave;

internal static class LeaveTestData
{
    internal static LegalEntity CreateLegalEntity(Guid tenantId, Guid legalEntityId, bool workWindowSet = true) => new()
    {
        Id = legalEntityId,
        TenantId = tenantId,
        Name = "Acme UK",
        CountryCode = "GBR",
        CurrencyCode = "GBP",
        IsActive = true,
        Timezone = "UTC",
        StandardWorkingDays = "[1,2,3,4,5]",
        WorkStartTime = workWindowSet ? new TimeOnly(9, 0) : null,
        WorkEndTime = workWindowSet ? new TimeOnly(18, 0) : null,
        BreakDurationMinutes = 60
    };

    /// <summary>A company-level policy version (160 h / year, 1 h minimum request, any one approver).</summary>
    internal static ResolvedLeavePolicy CreateResolvedPolicy(
        Guid tenantId, Guid leaveTypeId, Guid legalEntityId, IReadOnlyList<LeavePolicyBlackoutPeriod>? blackouts = null)
    {
        var policyId = Guid.NewGuid();
        var version = new LeavePolicyVersion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = policyId,
            VersionNumber = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            AmountHours = 160m,
            AmountPeriod = LeaveAmountPeriods.Year,
            EligibleFrom = LeaveEligibility.Immediately,
            MinRequestHours = 1m,
            ApprovalMode = LeaveApprovalModes.AnyOne,
        };
        return new ResolvedLeavePolicy(
            leaveTypeId, policyId, "Annual Leave - standard", version, blackouts ?? [],
            LeavePolicyTargetKinds.LegalEntity, legalEntityId, "Acme UK");
    }
}
