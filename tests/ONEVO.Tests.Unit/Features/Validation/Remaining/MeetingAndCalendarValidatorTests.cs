using FluentValidation;
using FluentValidation.TestHelper;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Calendar.Commands.AddCalendarEventParticipants;
using ONEVO.Application.Features.Calendar.Commands.CompleteCalendarConnection;
using ONEVO.Application.Features.Calendar.Commands.CreateCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.Commands.RemoveCalendarEventGuest;
using ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.StartCalendarConnection;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarConnection;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarEvent;
using ONEVO.Application.Features.Calendar.Commands.UpdateHolidayCalendarSettings;
using ONEVO.Application.Features.Calendar.Validation;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.CancelMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.CreateMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.EditMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.MoveMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Validation;
using static ONEVO.Tests.Unit.Features.Validation.Remaining.ValidationTestData;

namespace ONEVO.Tests.Unit.Features.Validation.Remaining;

/// <summary>Task 19b: meetings and the personal calendar, plus the shared ValidStringList rule.</summary>
public sealed class MeetingAndCalendarValidatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    // ---- ValidStringList ----

    private sealed record ListSample(IReadOnlyList<string?>? Items);

    private sealed class ListSampleValidator : AbstractValidator<ListSample>
    {
        public ListSampleValidator() => RuleFor(x => x.Items).ValidStringList(2, 3);
    }

    [Fact]
    public void ValidStringList_BoundsCountAndEachEntry()
    {
        var validator = new ListSampleValidator();
        validator.TestValidate(new ListSample(null)).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new ListSample(["abc", null])).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new ListSample(["a", "b", "c"])).ShouldHaveValidationErrorFor(x => x.Items);
        validator.TestValidate(new ListSample(["abcd"])).ShouldHaveValidationErrorFor(x => x.Items);
    }

    // ---- Meetings ----

    private static CreateMeetingRequest Meeting(
        string title = "Standup", string? description = "<p>Daily</p>", string? location = null, string timezone = "Asia/Colombo",
        string? rule = null, string? platform = null, IReadOnlyList<Guid>? people = null, IReadOnlyList<string>? guests = null) =>
        new(null, null, title, description, location, false, Start, 30, false, timezone, rule, people ?? [], guests ?? [], platform);

    [Fact]
    public void CreateMeeting_AtLimits_Passes()
        => new CreateMeetingCommandValidator()
            .TestValidate(new CreateMeetingCommand(Meeting(S(500), S(50_000), S(500), S(50), S(500), "teams",
                Ids(1000), Enumerable.Range(0, 1000).Select(i => $"g{i}@example.com").ToList())))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void CreateMeeting_OverLimitsOrActiveContent_Fails()
    {
        var result = new CreateMeetingCommandValidator().TestValidate(new CreateMeetingCommand(Meeting(
            S(501), Script, S(501), S(51), S(501), S(31), Ids(1001), [S(256)])));
        result.ShouldHaveValidationErrorFor("Request.Title");
        result.ShouldHaveValidationErrorFor("Request.Description");
        result.ShouldHaveValidationErrorFor("Request.Location");
        result.ShouldHaveValidationErrorFor("Request.Timezone");
        result.ShouldHaveValidationErrorFor("Request.RecurrenceRule");
        result.ShouldHaveValidationErrorFor("Request.Platform");
        result.ShouldHaveValidationErrorFor("Request.ParticipantEmployeeIds");
        result.ShouldHaveValidationErrorFor("Request.GuestEmails");
    }

    [Fact]
    public void EditMeeting_NullFieldsPass_LongOrActiveFail()
    {
        var validator = new EditMeetingCommandValidator();
        validator.TestValidate(new EditMeetingCommand(Guid.NewGuid(), Start,
            new EditMeetingRequest("this", 1, null, null, null, null, null, null, null, null, null))).ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new EditMeetingCommand(Guid.NewGuid(), Start,
            new EditMeetingRequest(S(101), 1, S(501), Script, S(501), null, null, null, null, S(51), S(501))));
        result.ShouldHaveValidationErrorFor("Edit.Scope");
        result.ShouldHaveValidationErrorFor("Edit.Title");
        result.ShouldHaveValidationErrorFor("Edit.Description");
        result.ShouldHaveValidationErrorFor("Edit.Location");
        result.ShouldHaveValidationErrorFor("Edit.Timezone");
        result.ShouldHaveValidationErrorFor("Edit.RecurrenceRule");
    }

    [Fact]
    public void MoveAndCancel_BoundTheScope()
    {
        new MoveMeetingCommandValidator().TestValidate(new MoveMeetingCommand(Guid.NewGuid(), Start, new MoveMeetingRequest("following", Guid.NewGuid(), Guid.NewGuid())))
            .ShouldNotHaveAnyValidationErrors();
        new MoveMeetingCommandValidator().TestValidate(new MoveMeetingCommand(Guid.NewGuid(), Start, new MoveMeetingRequest(S(101), Guid.NewGuid(), Guid.NewGuid())))
            .ShouldHaveValidationErrorFor("Move.Scope");
        new CancelMeetingCommandValidator().TestValidate(new CancelMeetingCommand(Guid.NewGuid(), Start, "all")).ShouldNotHaveAnyValidationErrors();
        new CancelMeetingCommandValidator().TestValidate(new CancelMeetingCommand(Guid.NewGuid(), Start, S(101)))
            .ShouldHaveValidationErrorFor(x => x.Scope);
    }

    [Fact]
    public void Attendees_BoundBothLists()
    {
        new AddSeriesAttendeesCommandValidator()
            .TestValidate(new AddSeriesAttendeesCommand(Guid.NewGuid(), new MeetingAttendeesRequest(Ids(1000), ["a@example.com"])))
            .ShouldNotHaveAnyValidationErrors();
        var result = new AddOccurrenceAttendeesCommandValidator()
            .TestValidate(new AddOccurrenceAttendeesCommand(Guid.NewGuid(), Start, new MeetingAttendeesRequest(Ids(1001), [S(256)])));
        result.ShouldHaveValidationErrorFor("Request.EmployeeIds");
        result.ShouldHaveValidationErrorFor("Request.GuestEmails");
    }

    [Fact]
    public void RemoveGuest_EmailUpTo255()
    {
        new RemoveSeriesGuestCommandValidator().TestValidate(new RemoveSeriesGuestCommand(Guid.NewGuid(), "a@example.com")).ShouldNotHaveAnyValidationErrors();
        new RemoveSeriesGuestCommandValidator().TestValidate(new RemoveSeriesGuestCommand(Guid.NewGuid(), S(256))).ShouldHaveValidationErrorFor(x => x.Email);
        new RemoveOccurrenceGuestCommandValidator().TestValidate(new RemoveOccurrenceGuestCommand(Guid.NewGuid(), Start, S(256)))
            .ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void RespondAndLink_BoundTheirFields()
    {
        new RespondToMeetingCommandValidator()
            .TestValidate(new RespondToMeetingCommand(Guid.NewGuid(), new RespondToMeetingRequest("rejected", S(2_000), null)))
            .ShouldNotHaveAnyValidationErrors();
        var result = new RespondToMeetingCommandValidator()
            .TestValidate(new RespondToMeetingCommand(Guid.NewGuid(), new RespondToMeetingRequest(S(31), S(2_001), null)));
        result.ShouldHaveValidationErrorFor("Request.Status");
        result.ShouldHaveValidationErrorFor("Request.Reason");

        new CreateMeetingLinkCommandValidator().TestValidate(new CreateMeetingLinkCommand(Guid.NewGuid(), new MeetingLinkRequest("zoom")))
            .ShouldNotHaveAnyValidationErrors();
        new CreateMeetingLinkCommandValidator().TestValidate(new CreateMeetingLinkCommand(Guid.NewGuid(), new MeetingLinkRequest(S(31))))
            .ShouldHaveValidationErrorFor("Request.Platform");
    }

    // ---- Personal calendar ----

    private static CreateCalendarEventCommand Event(
        string title = "Dentist", string? description = null, DateTimeOffset? end = null, string? location = null, string? link = null,
        string? color = "#ffaa00", string recurrence = "none", IReadOnlyList<Guid>? people = null, string? rule = null,
        IReadOnlyList<string>? guests = null, string? provider = null) =>
        new(title, description, Start, end ?? Start.AddHours(1), false, location, link, color, recurrence, people ?? [], rule, guests, provider);

    [Fact]
    public void CreateEvent_AtLimits_Passes()
        => new CreateCalendarEventCommandValidator()
            .TestValidate(Event(S(200), S(10_000), Start.AddDays(366), S(500), S(500), S(7), S(20), Ids(1000), S(500),
                Enumerable.Range(0, 20).Select(i => $"g{i}@example.com").ToList(), S(30)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void CreateEvent_BlankTitle_IsLeftToTheHandler()
        => new CreateCalendarEventCommandValidator().TestValidate(Event(title: " ")).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void CreateEvent_OverLimits_Fails()
    {
        var result = new CreateCalendarEventCommandValidator()
            .TestValidate(Event(S(201), S(10_001), Start.AddDays(367), S(501), S(501), S(8), S(21), Ids(1001), S(501),
                Enumerable.Range(0, 21).Select(i => $"g{i}@example.com").ToList(), S(31)));
        foreach (var field in new[] { "Title", "Description", "EndDate", "Location", "MeetingLink", "Color", "Recurrence",
                     "ParticipantEmployeeIds", "RecurrenceRule", "GuestEmails", "PendingMeetingProvider" })
            result.ShouldHaveValidationErrorFor(field);
    }

    [Fact]
    public void UpdateEvent_EndBeforeStart_Fails()
    {
        var validator = new UpdateCalendarEventCommandValidator();
        validator.TestValidate(new UpdateCalendarEventCommand(Guid.NewGuid(), "x", null, Start, Start.AddHours(1), false, null, null, null, "none"))
            .ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new UpdateCalendarEventCommand(Guid.NewGuid(), "x", null, Start, Start.AddHours(-1), false, null, null, null, "none"))
            .ShouldHaveValidationErrorFor(x => x.EndDate);
    }

    [Fact]
    public void AddParticipants_UpTo1000()
    {
        new AddCalendarEventParticipantsCommandValidator().TestValidate(new AddCalendarEventParticipantsCommand(Guid.NewGuid(), Ids(1000)))
            .ShouldNotHaveAnyValidationErrors();
        new AddCalendarEventParticipantsCommandValidator().TestValidate(new AddCalendarEventParticipantsCommand(Guid.NewGuid(), Ids(1001)))
            .ShouldHaveValidationErrorFor(x => x.EmployeeIds);
    }

    [Fact]
    public void SmallCalendarCommands_PassAtLimits_FailBeyond()
    {
        new CreateEventMeetingCommandValidator().TestValidate(new CreateEventMeetingCommand(Guid.NewGuid(), "teams")).ShouldNotHaveAnyValidationErrors();
        new CreateEventMeetingCommandValidator().TestValidate(new CreateEventMeetingCommand(Guid.NewGuid(), S(31))).ShouldHaveValidationErrorFor(x => x.Provider);

        new RemoveCalendarEventGuestCommandValidator().TestValidate(new RemoveCalendarEventGuestCommand(Guid.NewGuid(), S(256)))
            .ShouldHaveValidationErrorFor(x => x.Email);

        new RespondToCalendarEventCommandValidator().TestValidate(new RespondToCalendarEventCommand(Guid.NewGuid(), "Accepted", S(2_000)))
            .ShouldNotHaveAnyValidationErrors();
        var respond = new RespondToCalendarEventCommandValidator().TestValidate(new RespondToCalendarEventCommand(Guid.NewGuid(), S(31), S(2_001)));
        respond.ShouldHaveValidationErrorFor(x => x.ResponseStatus);
        respond.ShouldHaveValidationErrorFor(x => x.Reason);

        new UpdateHolidayCalendarSettingsCommandValidator().TestValidate(new UpdateHolidayCalendarSettingsCommand(Guid.NewGuid(), "LK", true))
            .ShouldNotHaveAnyValidationErrors();
        new UpdateHolidayCalendarSettingsCommandValidator().TestValidate(new UpdateHolidayCalendarSettingsCommand(Guid.NewGuid(), "LKA", true))
            .ShouldHaveValidationErrorFor(x => x.OverrideCountryCode);

        new StartCalendarConnectionCommandValidator().TestValidate(new StartCalendarConnectionCommand(S(31))).ShouldHaveValidationErrorFor(x => x.Provider);
        new UpdateCalendarConnectionCommandValidator().TestValidate(new UpdateCalendarConnectionCommand(Guid.NewGuid(), "two_way"))
            .ShouldNotHaveAnyValidationErrors();
        new UpdateCalendarConnectionCommandValidator().TestValidate(new UpdateCalendarConnectionCommand(Guid.NewGuid(), S(21)))
            .ShouldHaveValidationErrorFor(x => x.SyncDirection);
    }

    [Fact]
    public void CompleteConnection_BoundsCodeAndState()
    {
        var validator = new CompleteCalendarConnectionCommandValidator();
        validator.TestValidate(new CompleteCalendarConnectionCommand("microsoft", S(4_096), S(2_048))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new CompleteCalendarConnectionCommand("microsoft", null, "s")).ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new CompleteCalendarConnectionCommand(S(31), S(4_097), S(2_049)));
        result.ShouldHaveValidationErrorFor(x => x.Provider);
        result.ShouldHaveValidationErrorFor(x => x.Code);
        result.ShouldHaveValidationErrorFor(x => x.State);
    }
}
