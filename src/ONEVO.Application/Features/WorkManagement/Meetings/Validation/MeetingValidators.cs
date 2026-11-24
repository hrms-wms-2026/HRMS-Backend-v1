using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.CancelMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.CreateMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.EditMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.MoveMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Validation;

/// <summary>
/// Task 19b. meeting_series: title, location and recurrence_rule nvarchar(500), timezone nvarchar(50).
/// meeting_occurrence_attendees: email nvarchar(255), response_status nvarchar(30). calendar_event_meetings.provider
/// is nvarchar(30). The description lands in work_tasks.description (nvarchar(max), rich text, checked with
/// RichTextRules on the task paths); 50,000 matches the task-comment cap. response_reason is nvarchar(max); 2,000
/// matches the other decision notes. The id and email lists use the bulk-invite bound of
/// <see cref="MeetingBulkInviteService.MaxIdsPerList"/> per list.
/// MeetingCreateValidator and the edit, move, cancel and attendee services keep the required checks, the allowed
/// scopes, statuses and platforms, the email format and the recurrence rules, each with its own error code.
/// </summary>
public static class MeetingLimits
{
    public const int TitleMaxLength = MeetingCreateValidator.MaxTitleLength;
    public const int DescriptionMaxLength = 50_000;
    public const int LocationMaxLength = MeetingCreateValidator.MaxLocationLength;
    public const int RecurrenceRuleMaxLength = MeetingCreateValidator.MaxRecurrenceRuleLength;
    public const int TimezoneMaxLength = 50;
    public const int PlatformMaxLength = 30;
    public const int ResponseStatusMaxLength = 30;
    public const int ResponseReasonMaxLength = 2_000;
    public const int MaxPeoplePerList = MeetingBulkInviteService.MaxIdsPerList;
}

public sealed class CreateMeetingRequestValidator : AbstractValidator<CreateMeetingRequest>
{
    public CreateMeetingRequestValidator()
    {
        RuleFor(x => x.Title).ValidName(MeetingLimits.TitleMaxLength, required: false);
        RuleFor(x => x.Description).ValidRichText(MeetingLimits.DescriptionMaxLength);
        RuleFor(x => x.Location).ValidName(MeetingLimits.LocationMaxLength, required: false);
        RuleFor(x => x.Timezone).ValidName(MeetingLimits.TimezoneMaxLength, required: false);
        RuleFor(x => x.RecurrenceRule).ValidName(MeetingLimits.RecurrenceRuleMaxLength, required: false);
        RuleFor(x => x.Platform).ValidName(MeetingLimits.PlatformMaxLength, required: false);
        RuleFor(x => x.ParticipantEmployeeIds).ValidIdList(MeetingLimits.MaxPeoplePerList);
        RuleFor(x => x.GuestEmails).ValidStringList(MeetingLimits.MaxPeoplePerList, InputLimits.EmailMaxLength);
    }
}

public sealed class CreateMeetingCommandValidator : AbstractValidator<CreateMeetingCommand>
{
    public CreateMeetingCommandValidator()
    {
        RuleFor(x => x.Request).SetValidator(new CreateMeetingRequestValidator());
    }
}

public sealed class EditMeetingCommandValidator : AbstractValidator<EditMeetingCommand>
{
    public EditMeetingCommandValidator()
    {
        RuleFor(x => x.Edit.Scope).ValidName(InputLimits.KeyMaxLength, required: false).When(x => x.Edit is not null);
        RuleFor(x => x.Edit.Title).ValidName(MeetingLimits.TitleMaxLength, required: false).When(x => x.Edit is not null);
        RuleFor(x => x.Edit.Description).ValidRichText(MeetingLimits.DescriptionMaxLength).When(x => x.Edit is not null);
        RuleFor(x => x.Edit.Location).ValidName(MeetingLimits.LocationMaxLength, required: false).When(x => x.Edit is not null);
        RuleFor(x => x.Edit.Timezone).ValidName(MeetingLimits.TimezoneMaxLength, required: false).When(x => x.Edit is not null);
        RuleFor(x => x.Edit.RecurrenceRule).ValidName(MeetingLimits.RecurrenceRuleMaxLength, required: false).When(x => x.Edit is not null);
    }
}

public sealed class MoveMeetingCommandValidator : AbstractValidator<MoveMeetingCommand>
{
    public MoveMeetingCommandValidator()
    {
        RuleFor(x => x.Move.Scope).ValidName(InputLimits.KeyMaxLength, required: false).When(x => x.Move is not null);
    }
}

/// <summary>The cancel command has no reason field. Its scope is only compared with the known scopes in
/// MeetingCancelService, so this bounds it as a key.</summary>
public sealed class CancelMeetingCommandValidator : AbstractValidator<CancelMeetingCommand>
{
    public CancelMeetingCommandValidator()
    {
        RuleFor(x => x.Scope).ValidName(InputLimits.KeyMaxLength, required: false);
    }
}

public sealed class MeetingAttendeesRequestValidator : AbstractValidator<MeetingAttendeesRequest>
{
    public MeetingAttendeesRequestValidator()
    {
        RuleFor(x => x.EmployeeIds).ValidIdList(MeetingLimits.MaxPeoplePerList);
        RuleFor(x => x.GuestEmails).ValidStringList(MeetingLimits.MaxPeoplePerList, InputLimits.EmailMaxLength);
    }
}

public sealed class AddSeriesAttendeesCommandValidator : AbstractValidator<AddSeriesAttendeesCommand>
{
    public AddSeriesAttendeesCommandValidator()
    {
        RuleFor(x => x.Request).SetValidator(new MeetingAttendeesRequestValidator());
    }
}

public sealed class AddOccurrenceAttendeesCommandValidator : AbstractValidator<AddOccurrenceAttendeesCommand>
{
    public AddOccurrenceAttendeesCommandValidator()
    {
        RuleFor(x => x.Request).SetValidator(new MeetingAttendeesRequestValidator());
    }
}

public sealed class RemoveSeriesGuestCommandValidator : AbstractValidator<RemoveSeriesGuestCommand>
{
    public RemoveSeriesGuestCommandValidator()
    {
        RuleFor(x => x.Email).ValidName(InputLimits.EmailMaxLength, required: false);
    }
}

public sealed class RemoveOccurrenceGuestCommandValidator : AbstractValidator<RemoveOccurrenceGuestCommand>
{
    public RemoveOccurrenceGuestCommandValidator()
    {
        RuleFor(x => x.Email).ValidName(InputLimits.EmailMaxLength, required: false);
    }
}

public sealed class RespondToMeetingCommandValidator : AbstractValidator<RespondToMeetingCommand>
{
    public RespondToMeetingCommandValidator()
    {
        RuleFor(x => x.Request.Status).ValidName(MeetingLimits.ResponseStatusMaxLength, required: false).When(x => x.Request is not null);
        RuleFor(x => x.Request.Reason).ValidName(MeetingLimits.ResponseReasonMaxLength, required: false).When(x => x.Request is not null);
    }
}

public sealed class CreateMeetingLinkCommandValidator : AbstractValidator<CreateMeetingLinkCommand>
{
    public CreateMeetingLinkCommandValidator()
    {
        RuleFor(x => x.Request.Platform).ValidName(MeetingLimits.PlatformMaxLength, required: false).When(x => x.Request is not null);
    }
}
