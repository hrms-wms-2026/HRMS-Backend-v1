using FluentValidation;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminLogin;

public sealed class AdminLoginCommandValidator : AbstractValidator<AdminLoginCommand>
{
    public AdminLoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
