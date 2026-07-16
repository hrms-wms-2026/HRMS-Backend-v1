using FluentValidation;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminGoogleLogin;

public sealed class AdminGoogleLoginCommandValidator : AbstractValidator<AdminGoogleLoginCommand>
{
    public AdminGoogleLoginCommandValidator()
    {
        RuleFor(x => x.GoogleIdToken).NotEmpty();
    }
}
