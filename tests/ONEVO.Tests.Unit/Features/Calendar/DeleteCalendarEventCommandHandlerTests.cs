using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent;
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

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarConnectionTokenProvider> _tokenProvider = new();
    private readonly Mock<ITeamsMeetingClient> _teamsClient = new();
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
        return new DeleteCalendarEventCommandHandler(
            _currentUser.Object, _events.Object, _employees.Object, _notifications.Object,
            _meetings.Object, _connections.Object, _tokenProvider.Object, _teamsClient.Object, _unitOfWork.Object);
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
    public async Task Handle_EventHasActiveMeeting_CancelsItRemotelyBeforeDeleting()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event" };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var connection = new ExternalCalendarConnection { Id = Guid.NewGuid() };
        var meeting = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId,
            ExternalCalendarConnectionId = connection.Id, ExternalMeetingId = "graph-meeting-1",
            Status = CalendarEventMeetingStatuses.Active
        };
        _meetings.Setup(x => x.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _connections.Setup(x => x.GetByIdForTenantAsync(TenantId, connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _tokenProvider.Setup(x => x.GetFreshAccessTokenAsync(connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("access-token");

        var result = await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _teamsClient.Verify(x => x.CancelMeetingAsync("access-token", "graph-meeting-1", It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(x => x.Remove(existing), Times.Once);
    }

    [Fact]
    public async Task Handle_NoActiveMeeting_NeverCallsTeamsClient()
    {
        var sut = BuildSut();
        var existing = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Event" };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await sut.Handle(new DeleteCalendarEventCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _teamsClient.Verify(x => x.CancelMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
}
