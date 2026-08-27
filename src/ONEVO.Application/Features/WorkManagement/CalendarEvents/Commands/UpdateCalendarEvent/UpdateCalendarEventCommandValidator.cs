using System.Text.RegularExpressions;
using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.UpdateCalendarEvent;

public sealed class UpdateCalendarEventCommandValidator : AbstractValidator<UpdateCalendarEventCommand>
{
    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public UpdateCalendarEventCommandValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty).WithMessage("Calendar event is required.");
        RuleFor(x => x).Must(x => x.Name is not null || x.Color is not null || x.ObjectiveIds is not null)
            .WithMessage("At least one event field must be supplied.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255).When(x => x.Name is not null);
        RuleFor(x => x.Color).Must(color => HexColor.IsMatch(color ?? string.Empty))
            .When(x => x.Color is not null)
            .WithMessage("Color must be a hex value in the form #RRGGBB.");
        RuleForEach(x => x.ObjectiveIds!).NotEqual(Guid.Empty)
            .When(x => x.ObjectiveIds is not null)
            .WithMessage("Objective ids must not be empty.");
    }
}
