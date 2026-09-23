using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Policy.Mappers;

public static class LeavePolicyMapper
{
    public static LeavePolicyListItemResponse ToListItem(LeavePolicyAggregate aggregate)
    {
        var policy = aggregate.Policy;
        return new LeavePolicyListItemResponse(
            policy.Id,
            policy.Name,
            policy.Description,
            policy.Country ?? string.Empty,
            policy.JobLevel,
            policy.AccrualMethod,
            policy.AccrualStart,
            policy.ProrationMethod,
            policy.ApprovalMode,
            policy.EffectiveFrom,
            policy.Version,
            policy.IsActive,
            aggregate.LeaveTypes.Select(t => ToTypeRule(policy.AccrualMethod, t)).ToList(),
            aggregate.LegalEntities.Select(ToLegalEntityAssignment).ToList(),
            policy.CreatedAt);
    }

    public static LeavePolicyResponse ToResponse(LeavePolicyAggregate aggregate)
    {
        var policy = aggregate.Policy;
        return new LeavePolicyResponse(
            policy.Id,
            policy.Name,
            policy.Description,
            policy.Country ?? string.Empty,
            policy.JobLevel,
            policy.AccrualMethod,
            policy.AccrualStart,
            policy.AccrualAfterNMonths,
            policy.ProrationMethod,
            policy.ProbationRestriction,
            policy.MinimumTenureMonths,
            policy.FirstYearReducedPercent,
            policy.MinimumNoticeDays,
            policy.MaxConsecutiveDays,
            policy.MinDaysPerRequest,
            policy.MaxTeamAbsencePercent,
            policy.ApprovalMode,
            policy.EffectiveFrom,
            policy.Version,
            policy.IsActive,
            aggregate.LeaveTypes.Select(t => ToTypeRule(policy.AccrualMethod, t)).ToList(),
            aggregate.BlackoutPeriods.Select(ToBlackoutPeriod).ToList(),
            aggregate.LegalEntities.Select(ToLegalEntityAssignment).ToList(),
            policy.CreatedAt,
            policy.UpdatedAt);
    }

    private static LeavePolicyLeaveTypeRuleResponse ToTypeRule(
        string accrualMethod,
        LeavePolicyLeaveTypeWithType item)
    {
        var rule = item.Rule;
        decimal? monthlyAccrualDays = accrualMethod == LeaveAccrualMethods.Monthly
            ? decimal.Round(rule.AnnualEntitlementDays / 12m, 1, MidpointRounding.AwayFromZero)
            : null;

        return new LeavePolicyLeaveTypeRuleResponse(
            rule.Id,
            rule.LeaveTypeId,
            item.LeaveTypeName,
            item.LeaveTypeCode,
            rule.AnnualEntitlementDays,
            monthlyAccrualDays,
            rule.CarryForwardMaxDays,
            rule.CarryForwardExpiryMonths);
    }

    private static LeavePolicyBlackoutPeriodResponse ToBlackoutPeriod(
        ONEVO.Domain.Features.Leave.Policy.Entities.LeavePolicyBlackoutPeriod period)
        => new(period.Id, period.StartDate, period.EndDate, period.Reason);

    private static LeavePolicyLegalEntityAssignmentResponse ToLegalEntityAssignment(
        LeavePolicyLegalEntityWithName item)
        => new(item.Assignment.Id, item.Assignment.LegalEntityId, item.LegalEntityName,
            item.Assignment.EffectiveDate, item.Assignment.IsActive);
}
