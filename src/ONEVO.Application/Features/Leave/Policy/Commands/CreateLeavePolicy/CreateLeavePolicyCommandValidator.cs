using FluentValidation;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;

public class CreateLeavePolicyCommandValidator : AbstractValidator<CreateLeavePolicyCommand>
{
    public CreateLeavePolicyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .WithMessage("Policy name is required and cannot exceed 100 characters.");

        RuleFor(x => x.Country).NotEmpty().MaximumLength(100)
            .WithMessage("Country is required to determine statutory compliance");

        RuleFor(x => x.JobLevel).MaximumLength(100);

        RuleFor(x => x.AccrualMethod).Must(m => LeaveAccrualMethods.All.Contains(m))
            .WithMessage("Accrual method must be one of: annual, monthly, daily.");

        RuleFor(x => x.AccrualStart).Must(s => LeaveAccrualStarts.All.Contains(s))
            .WithMessage("Accrual start must be one of: immediately, after_probation, after_n_months.");

        RuleFor(x => x.AccrualAfterNMonths).GreaterThanOrEqualTo(1)
            .When(x => x.AccrualStart == LeaveAccrualStarts.AfterNMonths)
            .WithMessage("Accrual-after-N-months must be at least 1.");

        RuleFor(x => x.AccrualAfterNMonths).Null()
            .When(x => x.AccrualStart != LeaveAccrualStarts.AfterNMonths)
            .WithMessage("Accrual-after-N-months is only allowed when accrual start is after_n_months.");

        RuleFor(x => x.ProrationMethod).Must(m => LeaveProrationMethods.All.Contains(m))
            .WithMessage("Proration method must be one of: calendar_days, working_days.");

        RuleFor(x => x.ApprovalMode).Must(m => LeaveApprovalModes.All.Contains(m))
            .WithMessage("Approval mode must be one of: any_one, all_must_approve, in_order.");

        RuleFor(x => x.MinimumTenureMonths).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinimumNoticeDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinDaysPerRequest).GreaterThan(0);
        RuleFor(x => x.MaxConsecutiveDays).GreaterThan(0).When(x => x.MaxConsecutiveDays.HasValue);
        RuleFor(x => x.MaxTeamAbsencePercent).InclusiveBetween(0, 100).When(x => x.MaxTeamAbsencePercent.HasValue);
        RuleFor(x => x.FirstYearReducedPercent).InclusiveBetween(0, 100).When(x => x.FirstYearReducedPercent.HasValue);

        RuleFor(x => x.LeaveTypes).NotEmpty();
        RuleFor(x => x.LeaveTypes)
            .Must(types => types.Select(t => t.LeaveTypeId).Distinct().Count() == types.Count)
            .WithMessage("The same leave type cannot appear twice in one policy.");
        RuleFor(x => x.LeaveTypes)
            .Must((command, types) => command.AccrualMethod != LeaveAccrualMethods.Monthly
                || types.All(t => t.MonthlyAccrualDays.HasValue && t.MonthlyAccrualDays.Value > 0))
            .WithMessage("Monthly accrual days are required for every leave type when accrual method is monthly.");
        RuleFor(x => x.LeaveTypes)
            .Must((command, types) => command.AccrualMethod == LeaveAccrualMethods.Monthly
                || types.All(t => t.AnnualEntitlementDays > 0))
            .WithMessage("Annual entitlement days must be positive for every leave type.");
        RuleForEach(x => x.LeaveTypes).ChildRules(rule =>
        {
            rule.RuleFor(x => x.LeaveTypeId).NotEmpty();
            rule.RuleFor(x => x.CarryForwardMaxDays).GreaterThanOrEqualTo(0)
                .When(x => x.CarryForwardMaxDays.HasValue);
            rule.RuleFor(x => x.CarryForwardExpiryMonths).InclusiveBetween(1, 12)
                .When(x => x.CarryForwardExpiryMonths.HasValue);
        });

        RuleForEach(x => x.BlackoutPeriods).ChildRules(rule =>
        {
            rule.RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
                .WithMessage("Blackout period end date must be on or after start date.");
            rule.RuleFor(x => x.Reason).MaximumLength(200);
        });

        RuleFor(x => x.LegalEntityIds).NotEmpty()
            .WithMessage("Assign one or more legal entities.");
        RuleFor(x => x.LegalEntityIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("The same legal entity cannot appear twice in one policy.");
    }
}
