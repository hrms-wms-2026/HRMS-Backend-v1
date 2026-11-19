using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Commands.Common;

public static class LeavePolicyVersionFactory
{
    public static (LeavePolicyVersion Version, IReadOnlyList<LeavePolicyBlackoutPeriod> Blackouts) Create(
        Guid tenantId, Guid policyId, int versionNumber, DateOnly effectiveFrom, LeavePolicyRulesInput rules,
        Guid? actorId, DateTimeOffset now)
    {
        var version = new LeavePolicyVersion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = policyId,
            VersionNumber = versionNumber,
            EffectiveFrom = effectiveFrom,
            AmountHours = rules.AmountHours,
            AmountPeriod = rules.AmountPeriod,
            CarryForwardCapHours = rules.CarryForwardCapHours,
            CarryForwardExpiryMonths = rules.CarryForwardExpiryMonths,
            EligibleFrom = rules.EligibleFrom,
            EligibleAfterMonths = rules.EligibleFrom == LeaveEligibility.AfterMonths ? rules.EligibleAfterMonths : null,
            MinimumNoticeDays = rules.MinimumNoticeDays,
            MinRequestHours = rules.MinRequestHours,
            MaxRequestHours = rules.MaxRequestHours,
            MaxTeamAbsencePercent = rules.MaxTeamAbsencePercent,
            ApprovalMode = rules.ApprovalMode,
            CreatedAt = now,
            CreatedBy = actorId,
        };
        var blackouts = rules.Blackouts
            .Select(b => new LeavePolicyBlackoutPeriod
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                LeavePolicyVersionId = version.Id,
                StartDate = b.StartDate,
                EndDate = b.EndDate,
                Reason = string.IsNullOrWhiteSpace(b.Reason) ? null : b.Reason.Trim(),
            })
            .ToList();
        return (version, blackouts);
    }

    public static LeavePolicyRulesInput ToRules(LeavePolicyVersion version, IEnumerable<LeavePolicyBlackoutPeriod> blackouts) =>
        new(
            version.AmountHours,
            version.AmountPeriod,
            version.CarryForwardCapHours,
            version.CarryForwardExpiryMonths,
            version.EligibleFrom,
            version.EligibleAfterMonths,
            version.MinimumNoticeDays,
            version.MinRequestHours,
            version.MaxRequestHours,
            version.MaxTeamAbsencePercent,
            version.ApprovalMode,
            blackouts.Select(b => new LeavePolicyBlackoutInput(b.StartDate, b.EndDate, b.Reason)).ToList());

    /// <summary>The version in force on <paramref name="date"/>, or null if none has started yet.</summary>
    public static LeavePolicyVersion? InForce(IEnumerable<LeavePolicyVersion> versions, DateOnly date) =>
        versions.Where(v => v.EffectiveFrom <= date).MaxBy(v => v.EffectiveFrom);
}
