using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Policy.Entities;

/// <summary>The rules of a policy from <see cref="EffectiveFrom"/> until the next version. Immutable
/// once in force; a future version may be replaced or deleted until it starts.</summary>
public class LeavePolicyVersion : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeavePolicyId { get; set; }
    public int VersionNumber { get; set; }
    public DateOnly EffectiveFrom { get; set; }

    public decimal AmountHours { get; set; }
    public string AmountPeriod { get; set; } = Common.LeaveAmountPeriods.Year;
    public decimal? CarryForwardCapHours { get; set; }
    public int? CarryForwardExpiryMonths { get; set; }
    public string EligibleFrom { get; set; } = Common.LeaveEligibility.Immediately;
    public int? EligibleAfterMonths { get; set; }

    public int MinimumNoticeDays { get; set; }
    public decimal MinRequestHours { get; set; } = 1m;
    public decimal? MaxRequestHours { get; set; }
    public decimal? MaxTeamAbsencePercent { get; set; }
    public string ApprovalMode { get; set; } = Common.LeaveApprovalModes.AnyOne;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; set; }
}
