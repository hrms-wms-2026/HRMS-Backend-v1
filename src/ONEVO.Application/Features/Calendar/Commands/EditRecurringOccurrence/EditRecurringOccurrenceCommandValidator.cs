using FluentValidation;
using ONEVO.Application.Common.Validation;

namespace ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;

/// <summary>Task 19. personal_calendar_events: title nvarchar(200), location and meeting_link nvarchar(500),
/// color nvarchar(7). description is nvarchar(max) and plain text; 10,000 is a generous cap that leaves room for
/// descriptions written before this rule existed. One occurrence may span at most 366 days.</summary>
public sealed class EditRecurringOccurrenceCommandValidator : AbstractValidator<EditRecurringOccurrenceCommand>
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 10_000;
    public const int LocationMaxLength = 500;
    public const int MeetingLinkMaxLength = 500;
    public const int ColorMaxLength = 7;
    public static readonly TimeSpan MaxSpan = TimeSpan.FromDays(366);

    public EditRecurringOccurrenceCommandValidator()
    {
        RuleFor(x => x.Title).ValidName(TitleMaxLength);
        RuleFor(x => x.Description).ValidName(DescriptionMaxLength, required: false);
        RuleFor(x => x.Location).ValidName(LocationMaxLength, required: false);
        RuleFor(x => x.MeetingLink).ValidName(MeetingLinkMaxLength, required: false);
        RuleFor(x => x.Color).ValidName(ColorMaxLength, required: false);
        RuleFor(x => x).ValidDateRange(x => x.StartDate, x => x.EndDate, MaxSpan);
    }
}
