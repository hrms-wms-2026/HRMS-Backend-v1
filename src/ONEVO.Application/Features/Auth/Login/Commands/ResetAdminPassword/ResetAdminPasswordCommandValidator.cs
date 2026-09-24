using FluentValidation;
using ONEVO.Application.Features.Auth.Login.Validation;

namespace ONEVO.Application.Features.Auth.Login.Commands.ResetAdminPassword;

public sealed class ResetAdminPasswordCommandValidator : AbstractValidator<ResetAdminPasswordCommand>
{
    public ResetAdminPasswordCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).ApplyAdminPasswordPolicy();
    }
}
