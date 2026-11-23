using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Calendar.Helpers;

namespace ONEVO.Application.Features.Calendar.Commands.AddCalendarEventGuests;

/// <summary>Task 19. calendar_event_guests.email is nvarchar(255); one event has at most
/// <see cref="CalendarEventValidation.MaxGuestsPerEvent"/> guests, so one request never needs more entries. The
/// handler still normalizes each address and checks the per-event total.</summary>
public sealed class AddCalendarEventGuestsCommandValidator : AbstractValidator<AddCalendarEventGuestsCommand>
{
    public const int EmailMaxLength = 255;

    public AddCalendarEventGuestsCommandValidator()
    {
        RuleFor(x => x.Emails)
            .Must(e => e is null || e.Count <= CalendarEventValidation.MaxGuestsPerEvent)
            .WithMessage($"'{{PropertyName}}' can hold at most {CalendarEventValidation.MaxGuestsPerEvent} entries.");
        RuleForEach(x => x.Emails).ValidName(EmailMaxLength, required: false);
    }
}
