using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.SubmitCheckIn;

public class SubmitCheckInCommandValidator : AbstractValidator<SubmitCheckInCommand>
{
    public SubmitCheckInCommandValidator()
    {
        When(x => x.Latitude.HasValue, () =>
        {
            RuleFor(x => x.Latitude!.Value)
                .InclusiveBetween(-90, 90)
                .WithMessage("Latitude must be between -90 and 90.");
        });

        When(x => x.Longitude.HasValue, () =>
        {
            RuleFor(x => x.Longitude!.Value)
                .InclusiveBetween(-180, 180)
                .WithMessage("Longitude must be between -180 and 180.");
        });

        When(x => x.LocationAccuracy.HasValue, () =>
        {
            RuleFor(x => x.LocationAccuracy!.Value)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Location accuracy cannot be negative.");
        });

        When(x => x.LocationAddress is not null, () =>
        {
            RuleFor(x => x.LocationAddress!)
                .MaximumLength(500)
                .WithMessage("Address must not exceed 500 characters.");
        });

        When(x => x.DeviceSerialNumber is not null, () =>
        {
            RuleFor(x => x.DeviceSerialNumber!)
                .MaximumLength(200)
                .WithMessage("Device serial number must not exceed 200 characters.");
        });
    }
}
