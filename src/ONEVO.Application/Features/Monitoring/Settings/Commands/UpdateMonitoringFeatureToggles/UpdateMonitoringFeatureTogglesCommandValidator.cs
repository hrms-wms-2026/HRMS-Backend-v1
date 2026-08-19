using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;

public class UpdateMonitoringFeatureTogglesCommandValidator : AbstractValidator<UpdateMonitoringFeatureTogglesCommand>
{
    public UpdateMonitoringFeatureTogglesCommandValidator()
    {
        RuleFor(x => x.IdleThresholdMinutes).InclusiveBetween(1, 60);
    }
}
