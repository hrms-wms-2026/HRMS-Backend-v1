using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RefreshTrayToken;

public sealed class RefreshTrayTokenCommandValidator : AbstractValidator<RefreshTrayTokenCommand>
{
    public RefreshTrayTokenCommandValidator()
    {
        RuleFor(c => c.RefreshToken).NotEmpty().MaximumLength(512);
        RuleFor(c => c.DeviceFingerprint).NotEmpty().MaximumLength(512);
    }
}
