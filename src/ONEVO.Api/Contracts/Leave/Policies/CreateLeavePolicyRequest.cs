namespace ONEVO.Api.Contracts.Leave.Policies;

public record CreateLeavePolicyRequest(
    string Name,
    string? Description,
    string Country,
    string? JobLevel,
    string AccrualMethod,
    string AccrualStart,
    int? AccrualAfterNMonths,
    string ProrationMethod,
    bool ProbationRestriction,
    int MinimumTenureMonths,
    decimal? FirstYearReducedPercent,
    int MinimumNoticeDays,
    int? MaxConsecutiveDays,
    decimal MinDaysPerRequest,
    decimal? MaxTeamAbsencePercent,
    string ApprovalMode,
    DateOnly EffectiveFrom,
    IReadOnlyList<CreateLeavePolicyTypeRuleRequest> LeaveTypes,
    IReadOnlyList<CreateLeavePolicyBlackoutPeriodRequest> BlackoutPeriods,
    IReadOnlyList<Guid> LegalEntityIds,
    bool ConfirmReplaceExistingLegalEntityAssignments);

public record CreateLeavePolicyTypeRuleRequest(
    Guid LeaveTypeId,
    decimal AnnualEntitlementDays,
    decimal? MonthlyAccrualDays,
    decimal? CarryForwardMaxDays,
    int? CarryForwardExpiryMonths);

public record CreateLeavePolicyBlackoutPeriodRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason);
