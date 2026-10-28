using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarNotificationSenderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IEmployeeRepository> _employees = new();

    private CalendarNotificationSender BuildSut()
    {
        _employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, Email = "ada@example.com", FirstName = "Ada", LastName = "Lovelace" });
        return new CalendarNotificationSender(_outbox.Object, _employees.Object);
    }

    [Fact]
    public async Task NotifyParticipantsAddedAsync_EnqueuesInAppNotificationAndEmail()
    {
        var sut = BuildSut();
        await sut.NotifyParticipantsAddedAsync(TenantId, "Standup", DateTimeOffset.UtcNow, "Room 4", [EmployeeId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.RecipientUserId == UserId && p.TemplateCode == "calendar_event_participant_added"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "ada@example.com"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyParticipantsAddedAsync_SendInviteEmailFalse_SkipsTheEmailButStillNotifiesInApp()
    {
        var sut = BuildSut();
        await sut.NotifyParticipantsAddedAsync(
            TenantId, "Standup", DateTimeOffset.UtcNow, "Room 4", [EmployeeId], "Ada Owner", CancellationToken.None,
            sendInviteEmail: false);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.RecipientUserId == UserId),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.IsAny<CalendarEventInviteEmailPayload>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyParticipantsAddedAsync_IncludesMeetingLinkInEmailPayloadWhenProvided()
    {
        var sut = BuildSut();
        await sut.NotifyParticipantsAddedAsync(TenantId, "Standup", DateTimeOffset.UtcNow, "Room 4", [EmployeeId], "Ada Owner", CancellationToken.None, "https://zoom.us/j/123");

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.MeetingLink == "https://zoom.us/j/123"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyMeetingLinkAddedAsync_EnqueuesInviteEmailWithLinkToEachParticipant()
    {
        var sut = BuildSut();
        await sut.NotifyMeetingLinkAddedAsync(TenantId, "Standup", DateTimeOffset.UtcNow, "Room 4", [EmployeeId], "Ada Owner", "https://zoom.us/j/123", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "ada@example.com" && p.MeetingLink == "https://zoom.us/j/123"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.IsAny<WorkNotificationPayload>(),
            TenantId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyGuestsAsync_EnqueuesAnInviteEmailPerAddress_AndNoInAppNotification()
    {
        var sut = BuildSut();
        var start = DateTimeOffset.UtcNow;

        await sut.NotifyGuestsAsync(TenantId, "Vendor sync", start, "Room 4", ["vendor@example.com", "pm@example.com"], "Ada Owner", "https://zoom.us/j/9", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p =>
                p.ToEmail == "vendor@example.com" && p.RecipientName == "vendor" && p.EventTitle == "Vendor sync" &&
                p.StartDateUtc == start && p.Location == "Room 4" && p.OrganizerName == "Ada Owner" && p.MeetingLink == "https://zoom.us/j/9"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "pm@example.com"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueAsync(OutboxMessageTypes.WorkNotification, It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyGuestsAsync_WithNoMeetingLink_SendsThePlainInvite()
    {
        var sut = BuildSut();

        await sut.NotifyGuestsAsync(TenantId, "Vendor sync", DateTimeOffset.UtcNow, null, ["vendor@example.com"], "Ada Owner", null, CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.MeetingLink == null),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyEventUpdatedAsync_EnqueuesOnlyInAppNotification()
    {
        var sut = BuildSut();
        await sut.NotifyEventUpdatedAsync(TenantId, "Standup", [EmployeeId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.TemplateCode == "calendar_event_updated"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueAsync(OutboxMessageTypes.CalendarEventInviteEmail, It.IsAny<CalendarEventInviteEmailPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyEventCancelledAsync_EnqueuesOnlyInAppNotification()
    {
        var sut = BuildSut();
        await sut.NotifyEventCancelledAsync(TenantId, "Standup", [EmployeeId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.TemplateCode == "calendar_event_cancelled"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SkipsAnEmployeeIdThatNoLongerResolves()
    {
        var sut = BuildSut();
        var missingId = Guid.NewGuid();
        _employees.Setup(x => x.GetByIdAsync(TenantId, missingId, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);

        await sut.NotifyEventUpdatedAsync(TenantId, "Standup", [missingId], "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- 0C link emails: keyed (once per key), with the event's .ics, to participants and guests ---------------

    [Fact]
    public async Task SendMeetingLinkAsync_KeysEachEmailAndCarriesTheIcs()
    {
        var eventId = Guid.NewGuid();
        var ics = new CalendarEventIcs(eventId, DateTimeOffset.UtcNow.AddHours(1), 42);

        await BuildSut().SendMeetingLinkAsync(TenantId, "Standup", DateTimeOffset.UtcNow, null,
            [new CalendarParticipantRecipient(EmployeeId, "link:m:1:p")], [new CalendarGuestRecipient("client@example.com", "link:m:1:g")],
            "Ada Owner", "https://teams.microsoft.com/l/1", ics, CancellationToken.None);

        _outbox.Verify(x => x.EnqueueKeyedAsync(OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "ada@example.com" && p.MeetingLink == "https://teams.microsoft.com/l/1"
                && p.EventId == eventId && p.Sequence == 42 && p.Kind == CalendarEventEmailKinds.Invite),
            "link:m:1:p", TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueKeyedAsync(OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "client@example.com" && p.EventId == eventId),
            "link:m:1:g", TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMeetingLinkAsync_WithoutAKey_SendsAnOrdinaryEmail()
    {
        await BuildSut().SendMeetingLinkAsync(TenantId, "Standup", DateTimeOffset.UtcNow, null,
            [new CalendarParticipantRecipient(EmployeeId)], [], "Ada Owner", meetingLink: null, ics: null, CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "ada@example.com" && p.MeetingLink == null), TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueKeyedAsync(It.IsAny<string>(), It.IsAny<CalendarEventInviteEmailPayload>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendMeetingLinkRemovedAsync_TellsParticipantsInAppAndEmailsEveryoneTheLinkRemovedKind()
    {
        await BuildSut().SendMeetingLinkRemovedAsync(TenantId, "Standup", DateTimeOffset.UtcNow, null,
            [new CalendarParticipantRecipient(EmployeeId, "link-removed:m:1:p")], [new CalendarGuestRecipient("client@example.com", "link-removed:m:1:g")],
            "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.RecipientUserId == UserId && p.TemplateCode == "calendar_event_meeting_link_removed"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueKeyedAsync(OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.Kind == CalendarEventEmailKinds.LinkRemoved && p.MeetingLink == null),
            It.Is<string>(k => k.StartsWith("link-removed:")), TenantId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task NotifyOrganizerAboutMeetingAsync_SendsTheNoticeTemplateToTheOrganizer()
    {
        await BuildSut().NotifyOrganizerAboutMeetingAsync(TenantId, UserId, CalendarMeetingNoticeCodes.LinkUnknown, "Standup", "Zoom", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.RecipientUserId == UserId && p.TemplateCode == CalendarMeetingNoticeCodes.LinkUnknown
                && p.Placeholders["provider"] == "Zoom"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmailEventChangedKeyed_EnqueuesOneKeyedMessagePerRecipient_WithTheKindAndIcs()
    {
        var sut = BuildSut();
        var ics = new CalendarEventIcs(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1), 42, RecurrenceId: DateTimeOffset.UtcNow);

        await sut.EmailEventChangedKeyedAsync(TenantId, CalendarEventEmailKinds.Cancelled, ics, "Standup", DateTimeOffset.UtcNow, "Room 4", null,
            [new CalendarParticipantRecipient(EmployeeId, "cancel:a:1:p")], [new CalendarGuestRecipient("guest@example.com", "cancel:a:1:g")],
            "Ada Owner", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueKeyedAsync(OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "ada@example.com" && p.Kind == CalendarEventEmailKinds.Cancelled
                && p.EventId == ics.EventId && p.Sequence == 42 && p.RecurrenceId == ics.RecurrenceId),
            "cancel:a:1:p", TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueKeyedAsync(OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "guest@example.com"), "cancel:a:1:g", TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueAsync(OutboxMessageTypes.CalendarEventInviteEmail, It.IsAny<CalendarEventInviteEmailPayload>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
