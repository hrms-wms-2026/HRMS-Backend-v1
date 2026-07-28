using FluentValidation;

namespace ONEVO.Application.Features.Auth.Login.Commands.BaseLogin;

public sealed class BaseLoginCommandValidator : AbstractValidator<BaseLoginCommand>
{
    public BaseLoginCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128);
    }
}
