using System.Text.RegularExpressions;
using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Commands.CreateCalendarEvent;

public sealed class CreateCalendarEventCommandValidator : AbstractValidator<CreateCalendarEventCommand>
{
    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public CreateCalendarEventCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty).WithMessage("Project is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255).WithMessage("Name is required and must be 255 characters or fewer.");
        RuleFor(x => x.Color).Must(color => HexColor.IsMatch(color ?? string.Empty)).WithMessage("Color must be a hex value in the form #RRGGBB.");
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("End date must be on or after the start date.");
        RuleFor(x => x.ObjectiveIds).NotNull();
        RuleForEach(x => x.ObjectiveIds).NotEqual(Guid.Empty).WithMessage("Objective ids must not be empty.");
        RuleFor(x => x.TaskIds).NotNull();
        RuleForEach(x => x.TaskIds).NotEqual(Guid.Empty).WithMessage("Task ids must not be empty.");
    }
}
