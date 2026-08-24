using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;

public sealed class StartOffboardingCommandValidator : AbstractValidator<StartOffboardingCommand>
{
    private static readonly string[] ValidReasons = ["resignation", "termination", "retirement", "contract_end"];
    private static readonly string[] ValidRiskLevels = ["low", "medium", "high", "critical"];
    private static readonly string[] ValidRehireEligibility = ["eligible", "not_eligible", "conditional"];

    public StartOffboardingCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Reason).Must(r => ValidReasons.Contains(r))
            .WithMessage($"Reason must be one of: {string.Join(", ", ValidReasons)}.");
        RuleFor(x => x.KnowledgeRiskLevel).Must(r => ValidRiskLevels.Contains(r))
            .WithMessage($"Knowledge risk level must be one of: {string.Join(", ", ValidRiskLevels)}.");
        RuleFor(x => x.RehireEligibility)
            .Must(r => r is null || ValidRehireEligibility.Contains(r))
            .WithMessage($"Rehire eligibility must be one of: {string.Join(", ", ValidRehireEligibility)}.");
        RuleFor(x => x.Notes).MaximumLength(4000);
    }
}
