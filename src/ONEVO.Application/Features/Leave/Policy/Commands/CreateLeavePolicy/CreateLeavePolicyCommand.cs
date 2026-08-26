using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;

public record CreateLeavePolicyCommand(
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
    IReadOnlyList<LeavePolicyTypeRuleInput> LeaveTypes,
    IReadOnlyList<LeavePolicyBlackoutPeriodInput> BlackoutPeriods,
    IReadOnlyList<Guid> LegalEntityIds,
    bool ConfirmReplaceExistingLegalEntityAssignments) : IRequest<Result<LeavePolicyResponse>>;

public record LeavePolicyTypeRuleInput(
    Guid LeaveTypeId,
    decimal AnnualEntitlementDays,
    decimal? MonthlyAccrualDays,
    decimal? CarryForwardMaxDays,
    int? CarryForwardExpiryMonths);

public record LeavePolicyBlackoutPeriodInput(
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason);
