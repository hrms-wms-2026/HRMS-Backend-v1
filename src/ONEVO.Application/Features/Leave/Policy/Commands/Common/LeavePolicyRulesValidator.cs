using FluentValidation;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Policy.Commands.Common;

public class LeavePolicyRulesValidator : AbstractValidator<LeavePolicyRulesInput>
{
    /// <summary>366 days x 24 hours: no single credit can be larger than a leap year of hours.</summary>
    public const decimal MaxAmountHours = 8784m;

    public LeavePolicyRulesValidator()
    {
        RuleFor(x => x.AmountHours).GreaterThan(0).LessThanOrEqualTo(MaxAmountHours)
            .WithMessage("Hours must be more than 0 and at most 8784.");
        RuleFor(x => x.AmountPeriod).Must(p => LeaveAmountPeriods.All.Contains(p))
            .WithMessage("Hours are given every month or every year.");
        RuleFor(x => x.CarryForwardCapHours).GreaterThanOrEqualTo(0).When(x => x.CarryForwardCapHours.HasValue)
            .WithMessage("The carry-over cap cannot be negative.");
        RuleFor(x => x.CarryForwardExpiryMonths).InclusiveBetween(1, 24).When(x => x.CarryForwardExpiryMonths.HasValue)
            .WithMessage("Carried hours expire after 1 to 24 months.");
        RuleFor(x => x.EligibleFrom).Must(e => LeaveEligibility.All.Contains(e))
            .WithMessage("New joiners can use it right away, after probation, or after a number of months.");
        RuleFor(x => x.EligibleAfterMonths).NotNull().InclusiveBetween(1, 60)
            .When(x => x.EligibleFrom == LeaveEligibility.AfterMonths)
            .WithMessage("Enter 1 to 60 months.");
        RuleFor(x => x.EligibleAfterMonths).Null()
            .When(x => x.EligibleFrom != LeaveEligibility.AfterMonths)
            .WithMessage("A month count is only used with 'after a number of months'.");
        RuleFor(x => x.MinimumNoticeDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinRequestHours).GreaterThan(0)
            .WithMessage("The shortest request must be more than 0 hours.");
        RuleFor(x => x.MaxRequestHours).GreaterThanOrEqualTo(x => x.MinRequestHours)
            .When(x => x.MaxRequestHours.HasValue)
            .WithMessage("The longest request cannot be shorter than the shortest request.");
        RuleFor(x => x.MaxTeamAbsencePercent).InclusiveBetween(1, 100).When(x => x.MaxTeamAbsencePercent.HasValue)
            .WithMessage("Team absence must be between 1 and 100 percent.");
        RuleFor(x => x.ApprovalMode).Must(m => LeaveApprovalModes.All.Contains(m))
            .WithMessage("Unknown approval mode.");
        RuleFor(x => x.Blackouts).NotNull();
        RuleForEach(x => x.Blackouts).Must(b => b.EndDate >= b.StartDate)
            .WithMessage("A blackout period cannot end before it starts.");
        RuleForEach(x => x.Blackouts).Must(b => b.Reason is null || b.Reason.Length <= 200)
            .WithMessage("A blackout reason cannot exceed 200 characters.");
    }
}

/// <summary>Policy-level targets: the company, departments and positions. Employee-level
/// overrides are set from the employee screen only.</summary>
public class LeavePolicyTargetsValidator : AbstractValidator<IReadOnlyList<LeavePolicyAssignmentInput>>
{
    private static readonly string[] PolicyLevelKinds =
        [LeavePolicyTargetKinds.LegalEntity, LeavePolicyTargetKinds.Department, LeavePolicyTargetKinds.Position];

    public LeavePolicyTargetsValidator()
    {
        RuleFor(x => x).NotNull();
        RuleForEach(x => x).Must(t => PolicyLevelKinds.Contains(t.TargetKind))
            .WithMessage("A policy can be assigned to the company, departments or positions.");
        RuleFor(x => x).Must(targets => targets.Select(t => (t.TargetKind, t.TargetId)).Distinct().Count() == targets.Count)
            .When(x => x is not null)
            .WithMessage("The same target is listed twice.");
    }
}
