using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
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
    private static readonly Guid OrganizerUserId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IEmployeeRepository> _employees = new();

    private CalendarNotificationSender BuildSut()
    {
        _employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, Email = "ada@example.com", FirstName = "Ada", LastName = "Lovelace" });
        return new CalendarNotificationSender(_outbox.Object, _employees.Object);
    }

    private void VerifyInApp(Guid userId, string templateCode, Times times) =>
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.RecipientUserId == userId && p.TemplateCode == templateCode),
            TenantId, It.IsAny<CancellationToken>()), times);

    private void VerifyKeyedEmail(string toEmail, string kind, string key) =>
        _outbox.Verify(x => x.EnqueueKeyedAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == toEmail && p.Kind == kind),
            key, TenantId, It.IsAny<CancellationToken>()), Times.Once);

    private void VerifyUnkeyedEmail(string toEmail, string kind, Times times) =>
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == toEmail && p.Kind == kind),
            TenantId, It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task NotifyParticipantsAddedAsync_Keyed_EnqueuesInAppAndAKeyedInvite()
    {
        await BuildSut().NotifyParticipantsAddedAsync(
            TenantId, "Standup", Start, "Room 4", [new CalendarParticipantRecipient(EmployeeId, "invite:k1")], "Ada Owner", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_participant_added", Times.Once());
        VerifyKeyedEmail("ada@example.com", CalendarEmailKinds.Invite, "invite:k1");
        VerifyUnkeyedEmail("ada@example.com", CalendarEmailKinds.Invite, Times.Never());
    }

    [Fact]
    public async Task NotifyParticipantsAddedAsync_Unkeyed_UsesTheStagedEnqueue()
    {
        await BuildSut().NotifyParticipantsAddedAsync(
            TenantId, "Standup", Start, "Room 4", [new CalendarParticipantRecipient(EmployeeId)], "Ada Owner", CancellationToken.None);

        VerifyUnkeyedEmail("ada@example.com", CalendarEmailKinds.Invite, Times.Once());
    }

    [Fact]
    public async Task NotifyParticipantsAddedAsync_SendInviteEmailFalse_SkipsTheEmailButStillNotifiesInApp()
    {
        await BuildSut().NotifyParticipantsAddedAsync(
            TenantId, "Standup", Start, "Room 4", [new CalendarParticipantRecipient(EmployeeId, "invite:k1")], "Ada Owner",
            CancellationToken.None, sendInviteEmail: false);

        VerifyInApp(UserId, "calendar_event_participant_added", Times.Once());
        _outbox.Verify(x => x.EnqueueKeyedAsync(
            OutboxMessageTypes.CalendarEventInviteEmail, It.IsAny<CalendarEventInviteEmailPayload>(),
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyParticipantsAddedAsync_IncludesMeetingLinkInEmailPayloadWhenProvided()
    {
        await BuildSut().NotifyParticipantsAddedAsync(
            TenantId, "Standup", Start, null, [new CalendarParticipantRecipient(EmployeeId)], "Ada Owner",
            CancellationToken.None, meetingLink: "https://us05web.zoom.us/j/1");

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.MeetingLink == "https://us05web.zoom.us/j/1"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyGuestsAsync_EnqueuesAKeyedInvitePerGuest_AndNoInAppNotification()
    {
        await BuildSut().NotifyGuestsAsync(
            TenantId, "Vendor sync", Start, null,
            [new CalendarGuestRecipient("vendor@example.com", "invite:g1"), new CalendarGuestRecipient("pm@example.com", "invite:g2")],
            "Ada Owner", null, CancellationToken.None);

        VerifyKeyedEmail("vendor@example.com", CalendarEmailKinds.Invite, "invite:g1");
        VerifyKeyedEmail("pm@example.com", CalendarEmailKinds.Invite, "invite:g2");
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification, It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyMeetingLinkAddedAsync_EnqueuesInviteEmailWithLinkToEachParticipant()
    {
        await BuildSut().NotifyMeetingLinkAddedAsync(
            TenantId, "Standup", Start, null, [new CalendarParticipantRecipient(EmployeeId)], "Ada Owner", "https://teams.microsoft.com/l/1", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.ToEmail == "ada@example.com" && p.MeetingLink == "https://teams.microsoft.com/l/1"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyEventUpdatedAsync_InAppToParticipants_UpdatedEmailToParticipantsAndGuests()
    {
        await BuildSut().NotifyEventUpdatedAsync(
            TenantId, "Standup", Start, "Room 4", "https://zoom.us/j/1",
            [new CalendarParticipantRecipient(EmployeeId, "update:p")], [new CalendarGuestRecipient("vendor@example.com", "update:g")],
            "Ada Owner", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_updated", Times.Once());
        VerifyKeyedEmail("ada@example.com", CalendarEmailKinds.Updated, "update:p");
        VerifyKeyedEmail("vendor@example.com", CalendarEmailKinds.Updated, "update:g");
    }

    [Fact]
    public async Task NotifyEventCancelledAsync_InAppToParticipants_CancelledEmailToParticipantsAndGuests()
    {
        await BuildSut().NotifyEventCancelledAsync(
            TenantId, "Standup", Start, null,
            [new CalendarParticipantRecipient(EmployeeId, "cancel:p")], [new CalendarGuestRecipient("vendor@example.com", "cancel:g")],
            "Ada Owner", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_cancelled", Times.Once());
        VerifyKeyedEmail("ada@example.com", CalendarEmailKinds.Cancelled, "cancel:p");
        VerifyKeyedEmail("vendor@example.com", CalendarEmailKinds.Cancelled, "cancel:g");
    }

    [Theory]
    [InlineData(true, "calendar_event_rsvp_accepted")]
    [InlineData(false, "calendar_event_rsvp_rejected")]
    public async Task NotifyRsvpAsync_InAppToTheOrganizer(bool accepted, string templateCode)
    {
        await BuildSut().NotifyRsvpAsync(TenantId, OrganizerUserId, "Standup", "Ada Lovelace", accepted, CancellationToken.None);

        VerifyInApp(OrganizerUserId, templateCode, Times.Once());
    }

    [Fact]
    public async Task NotifyNomineeAsync_InAppToTheNominee()
    {
        await BuildSut().NotifyNomineeAsync(TenantId, EmployeeId, "Standup", "Grace Hopper", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_replacement_nominee", Times.Once());
    }

    [Fact]
    public async Task NotifyParticipantRemovedAsync_InAppAndARemovedEmail()
    {
        await BuildSut().NotifyParticipantRemovedAsync(TenantId, "Standup", Start, null, EmployeeId, "Ada Owner", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_participant_removed", Times.Once());
        VerifyUnkeyedEmail("ada@example.com", CalendarEmailKinds.Removed, Times.Once());
    }

    [Fact]
    public async Task NotifyGuestRemovedAsync_ARemovedEmailOnly()
    {
        await BuildSut().NotifyGuestRemovedAsync(TenantId, "Standup", Start, null, "vendor@example.com", "Ada Owner", CancellationToken.None);

        VerifyUnkeyedEmail("vendor@example.com", CalendarEmailKinds.Removed, Times.Once());
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification, It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SkipsAnEmployeeIdThatNoLongerResolves()
    {
        var sut = BuildSut();
        await sut.NotifyEventUpdatedAsync(
            TenantId, "Standup", Start, null, null, [new CalendarParticipantRecipient(Guid.NewGuid(), "update:x")], [],
            "Ada Owner", CancellationToken.None);

        _outbox.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NotifyMeetingLinkAddedAsync_KeyedLinkEmail_NoInApp()
    {
        await BuildSut().NotifyMeetingLinkAddedAsync(
            TenantId, "Standup", Start, null, [new CalendarParticipantRecipient(EmployeeId, "link:m:1:p")], "Ada Owner", "https://zoom.us/j/1", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueKeyedAsync(
            OutboxMessageTypes.CalendarEventInviteEmail,
            It.Is<CalendarEventInviteEmailPayload>(p => p.MeetingLink == "https://zoom.us/j/1" && p.Kind == CalendarEmailKinds.Invite),
            "link:m:1:p", TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification, It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyMeetingLinkRemovedAsync_InAppToParticipants_LinkRemovedEmailToEveryone()
    {
        await BuildSut().NotifyMeetingLinkRemovedAsync(
            TenantId, "Standup", Start, null,
            [new CalendarParticipantRecipient(EmployeeId, "link-removed:p")], [new CalendarGuestRecipient("vendor@example.com", "link-removed:g")],
            "Ada Owner", CancellationToken.None);

        VerifyInApp(UserId, "calendar_event_meeting_link_removed", Times.Once());
        VerifyKeyedEmail("ada@example.com", CalendarEmailKinds.LinkRemoved, "link-removed:p");
        VerifyKeyedEmail("vendor@example.com", CalendarEmailKinds.LinkRemoved, "link-removed:g");
    }

    [Fact]
    public async Task NotifyOrganizerAboutMeetingAsync_InAppToTheOrganizer_WithTheProviderName()
    {
        await BuildSut().NotifyOrganizerAboutMeetingAsync(
            TenantId, OrganizerUserId, CalendarMeetingNoticeCodes.LinkFailed, "Standup", "Zoom", CancellationToken.None);

        _outbox.Verify(x => x.EnqueueAsync(
            OutboxMessageTypes.WorkNotification,
            It.Is<WorkNotificationPayload>(p => p.RecipientUserId == OrganizerUserId && p.TemplateCode == "calendar_event_meeting_link_failed"
                && p.Placeholders["provider"] == "Zoom"),
            TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
