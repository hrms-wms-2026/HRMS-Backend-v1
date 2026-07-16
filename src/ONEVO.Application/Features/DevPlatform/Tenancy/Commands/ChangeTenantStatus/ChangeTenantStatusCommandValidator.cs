using FluentValidation;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ChangeTenantStatus;

public sealed class ChangeTenantStatusCommandValidator : AbstractValidator<ChangeTenantStatusCommand>
{
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "suspend",
        "unsuspend",
        "activate",
        "cancel"
    };

    public ChangeTenantStatusCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();

        RuleFor(x => x.Action)
            .NotEmpty()
            .Must(a => AllowedActions.Contains(a))
            .WithMessage("action must be one of: suspend, unsuspend, activate, cancel.");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .When(x => string.Equals(x.Action, "suspend", StringComparison.OrdinalIgnoreCase))
            .WithMessage("reason is required when suspending a tenant.");

        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .When(x => x.Reason is not null);
    }
}
