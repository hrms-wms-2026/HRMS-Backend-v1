using FluentValidation;
using ONEVO.Application.Features.TimeAttendance.Validation;

namespace ONEVO.Application.Features.TimeAttendance.Commands.CreateClockInPolicy;

public class CreateClockInPolicyCommandValidator : AbstractValidator<CreateClockInPolicyCommand>
{
    public CreateClockInPolicyCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n))
            .WithMessage("Policy name is required.")
            .Must(n => n.Trim().Length <= 120)
            .WithMessage("Policy name cannot exceed 120 characters.");

        ClockInPolicyValidationRules.ApplyScopeRules(this, x => x.Scope);

        RuleFor(x => x)
            .Must(x => x.EffectiveTo is null || x.EffectiveTo.Value >= x.EffectiveFrom)
            .WithMessage("Effective to must be greater than or equal to effective from.");

        RuleFor(x => x.LateDeductionRules)
            .Must(rules =>
            {
                var minutes = rules.Select(r => r.LateArrivalMinute).ToList();
                return minutes.Count == minutes.Distinct().Count();
            })
            .WithMessage("Duplicate late arrival minute values are not allowed in the same policy.");

        RuleForEach(x => x.LateDeductionRules)
            .ChildRules(ClockInPolicyValidationRules.ApplyLateRuleItemRules);

        RuleFor(x => x.NotificationRecipientResolver)
            .NotEmpty()
            .WithMessage("Notification recipient resolver is required.")
            .MaximumLength(50);
    }
}
