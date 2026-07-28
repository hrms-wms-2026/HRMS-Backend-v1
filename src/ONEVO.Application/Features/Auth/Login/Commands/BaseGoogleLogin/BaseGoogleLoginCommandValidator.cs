using FluentValidation;

namespace ONEVO.Application.Features.Auth.Login.Commands.BaseGoogleLogin;

public sealed class BaseGoogleLoginCommandValidator : AbstractValidator<BaseGoogleLoginCommand>
{
    public BaseGoogleLoginCommandValidator()
    {
        RuleFor(c => c.GoogleIdToken).NotEmpty();
    }
}
