namespace ONEVO.Application.Features.Leave.Policy.DTOs.Responses;

public record LeavePolicyListItemResponse(
    Guid Id,
    string Name,
    string? Description,
    string Country,
    string? JobLevel,
    string AccrualMethod,
    string AccrualStart,
    string ProrationMethod,
    string ApprovalMode,
    DateOnly EffectiveFrom,
    int Version,
    bool IsActive,
    IReadOnlyList<LeavePolicyLeaveTypeRuleResponse> LeaveTypes,
    IReadOnlyList<LeavePolicyLegalEntityAssignmentResponse> LegalEntities,
    DateTimeOffset CreatedAt);

public record LeavePolicyResponse(
    Guid Id,
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
    int Version,
    bool IsActive,
    IReadOnlyList<LeavePolicyLeaveTypeRuleResponse> LeaveTypes,
    IReadOnlyList<LeavePolicyBlackoutPeriodResponse> BlackoutPeriods,
    IReadOnlyList<LeavePolicyLegalEntityAssignmentResponse> LegalEntities,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record LeavePolicyLeaveTypeRuleResponse(
    Guid Id,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    decimal AnnualEntitlementDays,
    decimal? MonthlyAccrualDays,
    decimal? CarryForwardMaxDays,
    int? CarryForwardExpiryMonths);

public record LeavePolicyBlackoutPeriodResponse(
    Guid Id,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason);

public record LeavePolicyLegalEntityAssignmentResponse(
    Guid Id,
    Guid LegalEntityId,
    string LegalEntityName,
    DateOnly EffectiveDate,
    bool IsActive);
