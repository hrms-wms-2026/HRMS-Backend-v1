using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ExchangeActivationCode;

public sealed class ExchangeActivationCodeCommandValidator : AbstractValidator<ExchangeActivationCodeCommand>
{
    public ExchangeActivationCodeCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().Length(8).Matches("^[A-Z2-9]+$");
        RuleFor(c => c.DeviceName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.DeviceOs).NotEmpty().MaximumLength(100);
        RuleFor(c => c.DeviceFingerprint).NotEmpty().MaximumLength(512);
    }
}
