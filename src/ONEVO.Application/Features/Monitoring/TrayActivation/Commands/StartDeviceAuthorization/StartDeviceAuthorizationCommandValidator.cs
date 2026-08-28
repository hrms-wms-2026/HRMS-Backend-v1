using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.StartDeviceAuthorization;

public sealed class StartDeviceAuthorizationCommandValidator : AbstractValidator<StartDeviceAuthorizationCommand>
{
    public StartDeviceAuthorizationCommandValidator()
    {
        RuleFor(x => x.DeviceName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DeviceOs).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceFingerprint).NotEmpty().MaximumLength(512);
        RuleFor(x => x.ClientVersion).NotEmpty().MaximumLength(50);
    }
}
