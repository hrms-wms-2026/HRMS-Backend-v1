using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Calendar.Commands.AddCalendarEventParticipants;
using ONEVO.Application.Features.Calendar.Commands.CompleteCalendarConnection;
using ONEVO.Application.Features.Calendar.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;
using ONEVO.Application.Features.Calendar.Commands.RemoveCalendarEventGuest;
using ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.StartCalendarConnection;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarConnection;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.UpdateHolidayCalendarSettings;
using ONEVO.Application.Features.Calendar.Helpers;

namespace ONEVO.Application.Features.Calendar.Validation;

/// <summary>
/// Task 19b. Personal calendar commands. The event limits are the ones task 19 chose for
/// <see cref="EditRecurringOccurrenceCommandValidator"/> (personal_calendar_events columns, a 10,000-character
/// plain-text description, a 366-day span). recurrence is nvarchar(20). recurrence_rule has no column size, so it
/// gets the meeting_series.recurrence_rule size of 500. Provider columns (external_calendar_connections,
/// calendar_event_meetings) are nvarchar(30), sync_direction nvarchar(20), calendar_event_participants.response_status
/// nvarchar(30) and holiday_calendar_settings.override_country_code nvarchar(2). response_reason is nvarchar(max);
/// 2,000 matches the other decision notes. The handlers keep the required checks, the known providers, statuses and
/// directions, the link and email formats and the per-event guest total.
/// </summary>
public static class CalendarLimits
{
    public const int RecurrenceMaxLength = 20;
    public const int RecurrenceRuleMaxLength = 500;
    public const int ProviderMaxLength = 30;
    public const int SyncDirectionMaxLength = 20;
    public const int ResponseStatusMaxLength = 30;
    public const int ResponseReasonMaxLength = 2_000;
    public const int CountryCodeMaxLength = 2;

    /// <summary>The most participants one request may add. It matches the meeting bulk-invite bound.</summary>
    public const int MaxParticipantsPerRequest = 1_000;

    /// <summary>OAuth authorization codes are opaque; Microsoft's run to about 1,500 characters.</summary>
    public const int OAuthCodeMaxLength = 4_096;

    /// <summary>The state is our own data-protected token, a few hundred characters long.</summary>
    public const int OAuthStateMaxLength = 2_048;
}

/// <summary>Create and update share the event fields.</summary>
public interface ICalendarEventInput
{
    string Title { get; }
    string? Description { get; }
    DateTimeOffset StartDate { get; }
    DateTimeOffset EndDate { get; }
    string? Location { get; }
    string? MeetingLink { get; }
    string? Color { get; }
    string Recurrence { get; }
}

public abstract class CalendarEventInputValidator<T> : AbstractValidator<T> where T : ICalendarEventInput
{
    protected CalendarEventInputValidator()
    {
        RuleFor(x => x.Title).ValidName(EditRecurringOccurrenceCommandValidator.TitleMaxLength, required: false);
        RuleFor(x => x.Description).ValidName(EditRecurringOccurrenceCommandValidator.DescriptionMaxLength, required: false);
        RuleFor(x => x.Location).ValidName(EditRecurringOccurrenceCommandValidator.LocationMaxLength, required: false);
        RuleFor(x => x.MeetingLink).ValidName(EditRecurringOccurrenceCommandValidator.MeetingLinkMaxLength, required: false);
        RuleFor(x => x.Color).ValidName(EditRecurringOccurrenceCommandValidator.ColorMaxLength, required: false);
        RuleFor(x => x.Recurrence).ValidName(CalendarLimits.RecurrenceMaxLength, required: false);
        RuleFor(x => x).ValidDateRange(x => x.StartDate, x => x.EndDate, EditRecurringOccurrenceCommandValidator.MaxSpan);
    }
}

public sealed class CreateCalendarEventCommandValidator : CalendarEventInputValidator<CreateCalendarEventCommand>
{
    public CreateCalendarEventCommandValidator()
    {
        RuleFor(x => x.RecurrenceRule).ValidName(CalendarLimits.RecurrenceRuleMaxLength, required: false);
        RuleFor(x => x.ParticipantEmployeeIds).ValidIdList(CalendarLimits.MaxParticipantsPerRequest);
        RuleFor(x => x.GuestEmails).ValidStringList(CalendarEventValidation.MaxGuestsPerEvent, InputLimits.EmailMaxLength);
        RuleFor(x => x.PendingMeetingProvider).ValidName(CalendarLimits.ProviderMaxLength, required: false);
    }
}

public sealed class UpdateCalendarEventCommandValidator : CalendarEventInputValidator<UpdateCalendarEventCommand> { }

/// <summary>Not a string command, so the coverage test cannot see it; the task names it for its id list.</summary>
public sealed class AddCalendarEventParticipantsCommandValidator : AbstractValidator<AddCalendarEventParticipantsCommand>
{
    public AddCalendarEventParticipantsCommandValidator()
    {
        RuleFor(x => x.EmployeeIds).ValidIdList(CalendarLimits.MaxParticipantsPerRequest);
    }
}

public sealed class CreateEventMeetingCommandValidator : AbstractValidator<CreateEventMeetingCommand>
{
    public CreateEventMeetingCommandValidator()
    {
        RuleFor(x => x.Provider).ValidName(CalendarLimits.ProviderMaxLength, required: false);
    }
}

public sealed class RemoveCalendarEventGuestCommandValidator : AbstractValidator<RemoveCalendarEventGuestCommand>
{
    public RemoveCalendarEventGuestCommandValidator()
    {
        RuleFor(x => x.Email).ValidName(InputLimits.EmailMaxLength, required: false);
    }
}

public sealed class RespondToCalendarEventCommandValidator : AbstractValidator<RespondToCalendarEventCommand>
{
    public RespondToCalendarEventCommandValidator()
    {
        RuleFor(x => x.ResponseStatus).ValidName(CalendarLimits.ResponseStatusMaxLength, required: false);
        RuleFor(x => x.Reason).ValidName(CalendarLimits.ResponseReasonMaxLength, required: false);
    }
}

public sealed class UpdateHolidayCalendarSettingsCommandValidator : AbstractValidator<UpdateHolidayCalendarSettingsCommand>
{
    public UpdateHolidayCalendarSettingsCommandValidator()
    {
        RuleFor(x => x.OverrideCountryCode).ValidName(CalendarLimits.CountryCodeMaxLength, required: false);
    }
}

public sealed class StartCalendarConnectionCommandValidator : AbstractValidator<StartCalendarConnectionCommand>
{
    public StartCalendarConnectionCommandValidator()
    {
        RuleFor(x => x.Provider).ValidName(CalendarLimits.ProviderMaxLength, required: false);
    }
}

public sealed class CompleteCalendarConnectionCommandValidator : AbstractValidator<CompleteCalendarConnectionCommand>
{
    public CompleteCalendarConnectionCommandValidator()
    {
        RuleFor(x => x.Provider).ValidName(CalendarLimits.ProviderMaxLength, required: false);
        RuleFor(x => x.Code).ValidName(CalendarLimits.OAuthCodeMaxLength, required: false);
        RuleFor(x => x.State).ValidName(CalendarLimits.OAuthStateMaxLength, required: false);
    }
}

public sealed class UpdateCalendarConnectionCommandValidator : AbstractValidator<UpdateCalendarConnectionCommand>
{
    public UpdateCalendarConnectionCommandValidator()
    {
        RuleFor(x => x.SyncDirection).ValidName(CalendarLimits.SyncDirectionMaxLength, required: false);
    }
}
