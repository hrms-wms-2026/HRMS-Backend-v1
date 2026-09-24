using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.RequestScreenshot;

public class RequestScreenshotCommandValidator : AbstractValidator<RequestScreenshotCommand>
{
    public RequestScreenshotCommandValidator()
    {
        RuleFor(x => x.AgentDeviceId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
    }
}
