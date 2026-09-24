using FluentValidation;

namespace ONEVO.Application.Features.Auth.Login.Commands.RequestAdminPasswordReset;

public sealed class RequestAdminPasswordResetCommandValidator
    : AbstractValidator<RequestAdminPasswordResetCommand>
{
    public RequestAdminPasswordResetCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
