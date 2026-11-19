using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

/// <summary>When the organizer's Outlook pushes the event with its attendees, Outlook emails them - ONEVO must not.</summary>
public sealed class CalendarOutlookInviteGateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OrganizerUserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IExternalCalendarEventLinkRepository> _links = new();

    private ExternalCalendarConnection? _connection = new()
    {
        Id = ConnectionId, TenantId = TenantId, UserId = OrganizerUserId, Provider = CalendarExternalSources.OutlookCalendar,
        Status = ExternalCalendarConnectionStatuses.Active, SyncDirection = CalendarSyncDirections.TwoWay
    };

    private CalendarNotificationSender BuildSut(CalendarEvent? storedEvent = null)
    {
        _employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = Guid.NewGuid(), Email = "ada@example.com", FirstName = "Ada", LastName = "L" });
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.UserId).Returns(OrganizerUserId);
        _connections.Setup(x => x.GetByTenantUserProviderAsync(TenantId, OrganizerUserId, CalendarExternalSources.OutlookCalendar, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _connection);
        _events.Setup(x => x.GetByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(storedEvent);
        return new CalendarNotificationSender(_outbox.Object, _employees.Object, _currentUser.Object, _connections.Object, _events.Object, _links.Object);
    }

    private static CalendarEvent ManualEvent(string recurrence = CalendarRecurrences.None) => new()
    {
        Id = EventId, TenantId = TenantId, Title = "Sync", SourceType = CalendarEventSourceTypes.Manual,
        Recurrence = recurrence, CreatedById = OrganizerUserId
    };

    private static CalendarEventIcs Ics(string? rule = null) => CalendarEventIcs.For(EventId, DateTimeOffset.UtcNow.AddHours(1), rule, DateTimeOffset.UtcNow);

    private void VerifyEmails(Times times) => _outbox.Verify(x => x.EnqueueAsync(
        OutboxMessageTypes.CalendarEventInviteEmail, It.IsAny<CalendarEventInviteEmailPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), times);

    private void VerifyPush(Func<CalendarOutlookPushPayload, bool> match, Times times) => _outbox.Verify(x => x.EnqueueAsync(
        OutboxMessageTypes.CalendarOutlookPush, It.Is<CalendarOutlookPushPayload>(p => match(p)), TenantId, It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task NewEvent_OrganizerPushesToOutlook_SkipsEmailQueuesOnePushAndKeepsInApp()
    {
        var sut = BuildSut(storedEvent: null); // not committed yet - being created in this request

        await sut.NotifyParticipantsAddedAsync(TenantId, "Sync", DateTimeOffset.UtcNow, null, [EmployeeId], "Org", CancellationToken.None, ics: Ics());
        await sut.NotifyGuestsAsync(TenantId, "Sync", DateTimeOffset.UtcNow, null, ["guest@example.com"], "Org", null, CancellationToken.None, Ics());

        VerifyEmails(Times.Never());
        VerifyPush(p => p.ConnectionId == ConnectionId && p.EventId == EventId && p.DeleteExternalEventId == null, Times.Once());
        _outbox.Verify(x => x.EnqueueAsync(OutboxMessageTypes.WorkNotification, It.IsAny<object>(), TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecurringEvent_StillEmailsBecauseRecurringEventsAreNeverPushed()
    {
        var sut = BuildSut(ManualEvent(CalendarRecurrences.Weekly));

        await sut.NotifyGuestsAsync(TenantId, "Sync", DateTimeOffset.UtcNow, null, ["guest@example.com"], "Org", null, CancellationToken.None, Ics("FREQ=WEEKLY"));

        VerifyEmails(Times.Once());
        VerifyPush(_ => true, Times.Never());
    }

    [Theory]
    [InlineData(ExternalCalendarConnectionStatuses.ReauthRequired, CalendarSyncDirections.TwoWay)]
    [InlineData(ExternalCalendarConnectionStatuses.Active, CalendarSyncDirections.PullOnly)]
    public async Task ConnectionThatCannotPush_StillEmails(string status, string direction)
    {
        _connection!.Status = status;
        _connection.SyncDirection = direction;
        var sut = BuildSut(ManualEvent());

        await sut.NotifyGuestsAsync(TenantId, "Sync", DateTimeOffset.UtcNow, null, ["guest@example.com"], "Org", null, CancellationToken.None, Ics());

        VerifyEmails(Times.Once());
        VerifyPush(_ => true, Times.Never());
    }

    [Fact]
    public async Task NoOutlookConnection_StillEmails()
    {
        _connection = null;
        var sut = BuildSut(ManualEvent());

        await sut.NotifyGuestsAsync(TenantId, "Sync", DateTimeOffset.UtcNow, null, ["guest@example.com"], "Org", null, CancellationToken.None, Ics());

        VerifyEmails(Times.Once());
    }

    [Fact]
    public async Task Cancelled_PushedEvent_QueuesOutlookDeleteInsteadOfEmail()
    {
        var sut = BuildSut(ManualEvent());
        _links.Setup(x => x.GetTrackedByCalendarEventAndConnectionAsync(TenantId, EventId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarEventLink { ExternalEventId = "AAMk-outlook-id" });

        await sut.EmailEventChangedAsync(TenantId, CalendarEventEmailKinds.Cancelled, Ics(), "Sync", DateTimeOffset.UtcNow, null, null,
            [EmployeeId], [], "Org", CancellationToken.None);

        VerifyEmails(Times.Never());
        VerifyPush(p => p.DeleteExternalEventId == "AAMk-outlook-id" && p.EventId == null, Times.Once());
    }

    [Fact]
    public async Task Cancelled_EventNeverReachedOutlook_StillEmailsTheCancellation()
    {
        var sut = BuildSut(ManualEvent());

        await sut.EmailEventChangedAsync(TenantId, CalendarEventEmailKinds.Cancelled, Ics(), "Sync", DateTimeOffset.UtcNow, null, null,
            [EmployeeId], [], "Org", CancellationToken.None);

        VerifyEmails(Times.Once());
        VerifyPush(_ => true, Times.Never());
    }

    [Fact]
    public async Task WithoutIcs_StillEmails()
    {
        var sut = BuildSut(ManualEvent());

        await sut.NotifyGuestsAsync(TenantId, "Sync", DateTimeOffset.UtcNow, null, ["guest@example.com"], "Org", null, CancellationToken.None);

        VerifyEmails(Times.Once());
    }
}
