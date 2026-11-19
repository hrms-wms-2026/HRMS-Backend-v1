using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class DeleteCalendarEventCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid MeetingId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private DeleteCalendarEventCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>());
        _meetings.Setup(x => x.GetTrackedByCalendarEventAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEventMeeting?)null);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result>>, CancellationToken>((op, ct) => op(ct));
        return new DeleteCalendarEventCommandHandler(
            _currentUser.Object, _events.Object, _employees.Object, _notifications.Object,
            _meetings.Object, _outbox.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_EventNotFound_ReturnsNotFound()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var result = await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = Guid.NewGuid(), Title = "Event" });

        var result = await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Owner_RemovesEvent()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event" };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.Remove(existing), Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OwnerWithParticipants_NotifiesThem()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event" };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var participantId = Guid.NewGuid();
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(EventId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [EventId] = [new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = participantId, ResponseStatus = CalendarEventParticipantStatuses.Accepted }]
            });

        var result = await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _notifications.Verify(x => x.NotifyEventCancelledAsync(
            TenantId, "Event", It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == participantId),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Delete_EmailsParticipantsAndGuestsACancellation()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event", MeetingLink = "https://zoom.us/j/1" };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>
            {
                [EventId] = [new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" }]
            });

        await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        _notifications.Verify(x => x.EmailEventChangedAsync(TenantId, ONEVO.Application.Features.Calendar.Services.CalendarEventEmailKinds.Cancelled,
            It.Is<ONEVO.Application.Features.Calendar.Services.CalendarEventIcs>(i => i.EventId == EventId), "Event", It.IsAny<DateTimeOffset>(),
            It.IsAny<string?>(), null, It.IsAny<IReadOnlyList<Guid>>(), It.Is<IReadOnlyList<string>>(g => g.Single() == "vendor@example.com"),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ActiveMeeting_QueuesASelfContainedCancel_InsideTheTransaction_AndNeverCallsTheProvider()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event", StartDate = DateTimeOffset.UtcNow.AddDays(1), EndDate = DateTimeOffset.UtcNow.AddDays(1).AddHours(1) };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var meeting = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId, Provider = CalendarEventMeetingProviders.Zoom,
            ExternalMeetingId = "987654321", ExternalCalendarConnectionId = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Active, LinkGeneration = 3
        };
        _meetings.Setup(x => x.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _meetings.Setup(x => x.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        var calls = new List<string>();
        _outbox.Setup(o => o.EnqueueKeyedAsync(OutboxMessageTypes.CalendarMeetingCancel, It.IsAny<CalendarMeetingCancelPayload>(),
                $"meeting-cancel:{meeting.Id}:3", TenantId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("queue")).ReturnsAsync(true);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.Equal(new[] { "queue", "save" }, calls);
        _outbox.Verify(o => o.EnqueueKeyedAsync(OutboxMessageTypes.CalendarMeetingCancel,
            It.Is<CalendarMeetingCancelPayload>(p => p.ExternalMeetingId == "987654321" && p.Provider == CalendarEventMeetingProviders.Zoom
                && p.OrganizerUserId == UserId && p.EventTitle == "Event" && p.ExternalCalendarConnectionId == meeting.ExternalCalendarConnectionId),
            It.IsAny<string>(), TenantId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, meeting.Status);
    }

    [Fact]
    public async Task Handle_LocalSaveFails_TheCancelMessageIsNeverCommitted()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event" };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var activeMeeting = new CalendarEventMeeting { Id = Guid.NewGuid(), Status = CalendarEventMeetingStatuses.Active, Provider = CalendarEventMeetingProviders.MicrosoftTeams };
        _meetings.Setup(x => x.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(activeMeeting);
        _meetings.Setup(x => x.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(activeMeeting);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        var committed = false;
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result>> op, CancellationToken ct) =>
            {
                var result = await op(ct); // throws before this line on a failed save
                committed = true;
                return result;
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None));

        Assert.False(committed); // the keyed insert ran inside the transaction, so a rollback removes it
    }

    [Fact]
    public async Task Handle_TransactionRetriedAfterAFailedSave_StillQueuesTheCancelOnTheAttemptThatCommits()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event" };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        CalendarEventMeeting Active() => new()
        {
            Id = MeetingId, TenantId = TenantId, CalendarEventId = EventId, Provider = CalendarEventMeetingProviders.MicrosoftTeams,
            ExternalMeetingId = "graph-1", Status = CalendarEventMeetingStatuses.Active, LinkGeneration = 1
        };
        // The tracked instance keeps attempt 1's mutation; the database (rolled back) still says active.
        var tracked = Active();
        _meetings.Setup(x => x.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(tracked);
        _meetings.Setup(x => x.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Active());
        var saves = 0;
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ++saves == 1 ? Task.FromException<int>(new InvalidOperationException("transient")) : Task.FromResult(1));
        // Execution strategy: a failed attempt is rolled back and the whole lambda runs again.
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result>> op, CancellationToken ct) =>
            {
                try { return await op(ct); }
                catch (InvalidOperationException) { return await op(ct); }
            });

        var result = await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _outbox.Verify(o => o.EnqueueKeyedAsync(OutboxMessageTypes.CalendarMeetingCancel, It.IsAny<CalendarMeetingCancelPayload>(),
            $"meeting-cancel:{MeetingId}:1", TenantId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
