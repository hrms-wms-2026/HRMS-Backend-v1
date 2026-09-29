using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CreateEventMeetingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid ParticipantEmployeeId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarConnectionTokenProvider> _tokenProvider = new();
    private readonly Mock<ITeamsMeetingClient> _teamsClient = new();
    private readonly Mock<IZoomMeetingClient> _zoomClient = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();

    private CreateEventMeetingCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CreateEventMeetingResult>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CreateEventMeetingResult>>>, CancellationToken>((op, ct) => op(ct));
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [EventId] = [new CalendarEventParticipant { EmployeeId = ParticipantEmployeeId, EventId = EventId }]
            });
        _employees.Setup(e => e.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        return new CreateEventMeetingCommandHandler(
            _currentUser.Object, _events.Object, _connections.Object, _tokenProvider.Object,
            _teamsClient.Object, _zoomClient.Object, _meetings.Object, _unitOfWork.Object,
            _notifications.Object, _employees.Object);
    }

    private static CalendarEvent MakeEvent() => new()
    {
        Id = EventId, TenantId = TenantId, Title = "Sprint planning", CreatedById = UserId,
        StartDate = DateTimeOffset.UtcNow.AddHours(1), EndDate = DateTimeOffset.UtcNow.AddHours(2)
    };

    [Fact]
    public async Task Handle_NoMicrosoftConnection_ReturnsMeetingProviderNotConnectedConflict()
    {
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeEvent());
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, "outlook_calendar", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);

        var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("meeting_provider_not_connected");
    }

    [Fact]
    public async Task Handle_ConnectionMissingMeetingScope_ReturnsMeetingProviderNotConnectedConflict()
    {
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeEvent());
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, "outlook_calendar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection
            {
                Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active,
                ScopesJson = "[\"Calendars.ReadWrite\"]" // no OnlineMeetings.ReadWrite
            });

        var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("meeting_provider_not_connected");
    }

    [Fact]
    public async Task Handle_ValidConnection_CreatesTeamsMeetingAndSetsMeetingLink()
    {
        var evt = MakeEvent();
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active,
            ScopesJson = "[\"Calendars.ReadWrite\",\"OnlineMeetings.ReadWrite\"]"
        };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, "outlook_calendar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _tokenProvider.Setup(t => t.GetFreshAccessTokenAsync(connection, "microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");
        _teamsClient.Setup(t => t.CreateMeetingAsync("access-token", evt.Title, evt.StartDate, evt.EndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamsMeetingDto("graph-meeting-1", "https://teams.microsoft.com/l/meetup-join/abc", null, "123456"));

        var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.JoinUrl.Should().Be("https://teams.microsoft.com/l/meetup-join/abc");
        evt.MeetingLink.Should().Be("https://teams.microsoft.com/l/meetup-join/abc");
        _meetings.Verify(m => m.AddAsync(
            It.Is<CalendarEventMeeting>(cm => cm.ExternalMeetingId == "graph-meeting-1" && cm.CalendarEventId == EventId),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.NotifyMeetingLinkAddedAsync(
            TenantId, evt.Title, evt.StartDate, null,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(ParticipantEmployeeId)),
            "Ada Owner", "https://teams.microsoft.com/l/meetup-join/abc", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EventWhoseEarlierMeetingWasRemoved_ReusesThatRowInsteadOfInsertingASecond()
    {
        var sut = BuildSut();
        var evt = MakeEvent();
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active,
            ScopesJson = "[\"user:read:user\",\"meeting:write:meeting\"]"
        };
        var oldConnectionId = Guid.NewGuid();
        var cancelled = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId, ExternalCalendarConnectionId = oldConnectionId,
            Provider = CalendarEventMeetingProviders.MicrosoftTeams, ExternalMeetingId = "old-teams-id",
            JoinUrl = "https://teams.microsoft.com/l/meetup-join/old", Status = CalendarEventMeetingStatuses.Cancelled,
            LastAttendanceSyncedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(cancelled);
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, "zoom", It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _tokenProvider.Setup(t => t.GetFreshAccessTokenAsync(connection, "zoom", It.IsAny<CancellationToken>())).ReturnsAsync("zoom-token");
        _zoomClient.Setup(z => z.CreateMeetingAsync("zoom-token", evt.Title, evt.StartDate, evt.EndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ZoomMeetingDto("zoom-42", "https://us05web.zoom.us/j/42", "https://us05web.zoom.us/s/42", "9876"));

        var result = await sut.Handle(new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.Zoom), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _meetings.Verify(m => m.AddAsync(It.IsAny<CalendarEventMeeting>(), It.IsAny<CancellationToken>()), Times.Never);
        _meetings.Verify(m => m.Update(cancelled), Times.Once);
        cancelled.Status.Should().Be(CalendarEventMeetingStatuses.Active);
        cancelled.Provider.Should().Be(CalendarEventMeetingProviders.Zoom);
        cancelled.ExternalCalendarConnectionId.Should().Be(connection.Id);
        cancelled.ExternalMeetingId.Should().Be("zoom-42");
        cancelled.JoinUrl.Should().Be("https://us05web.zoom.us/j/42");
        cancelled.OrganizerJoinUrl.Should().Be("https://us05web.zoom.us/s/42");
        cancelled.PasscodeOrPin.Should().Be("9876");
        cancelled.LastAttendanceSyncedAt.Should().BeNull();
        evt.MeetingLink.Should().Be("https://us05web.zoom.us/j/42");
    }

    [Fact]
    public async Task Handle_EventThatAlreadyHasAnActiveMeeting_ReturnsConflictWithoutCreatingAnotherRemoteMeeting()
    {
        var sut = BuildSut();
        var evt = MakeEvent();
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active,
            ScopesJson = "[\"user:read:user\",\"meeting:write:meeting\"]"
        };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId, Status = CalendarEventMeetingStatuses.Active });
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, "zoom", It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _tokenProvider.Setup(t => t.GetFreshAccessTokenAsync(connection, "zoom", It.IsAny<CancellationToken>())).ReturnsAsync("zoom-token");

        var result = await sut.Handle(new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.Zoom), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Be("meeting_already_exists");
        _zoomClient.Verify(z => z.CreateMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        _meetings.Verify(m => m.AddAsync(It.IsAny<CalendarEventMeeting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EventWithGuests_EmailsThemTheNewJoinLink()
    {
        var sut = BuildSut();
        var evt = MakeEvent();
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active,
            ScopesJson = "[\"Calendars.ReadWrite\",\"OnlineMeetings.ReadWrite\"]"
        };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>
            {
                [EventId] = [new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" }]
            });
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, "outlook_calendar", It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _tokenProvider.Setup(t => t.GetFreshAccessTokenAsync(connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("access-token");
        _teamsClient.Setup(t => t.CreateMeetingAsync("access-token", evt.Title, evt.StartDate, evt.EndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamsMeetingDto("graph-meeting-1", "https://teams.microsoft.com/l/meetup-join/abc", null, "123456"));

        var result = await sut.Handle(new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notifications.Verify(n => n.NotifyGuestsAsync(
            TenantId, evt.Title, evt.StartDate, evt.Location,
            It.Is<IReadOnlyList<string>>(emails => emails.Single() == "vendor@example.com"),
            "Ada Owner", "https://teams.microsoft.com/l/meetup-join/abc", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotOrganizer_ReturnsForbidden()
    {
        var evt = MakeEvent();
        evt.CreatedById = Guid.NewGuid(); // someone else
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);

        var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ValidZoomConnection_CreatesZoomMeetingAndSetsMeetingLink()
    {
        var evt = MakeEvent();
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active,
            ScopesJson = "[\"meeting:write:meeting\",\"meeting:read:meeting\"]"
        };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.Zoom, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _tokenProvider.Setup(t => t.GetFreshAccessTokenAsync(connection, "zoom", It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");
        _zoomClient.Setup(z => z.CreateMeetingAsync("access-token", evt.Title, evt.StartDate, evt.EndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ZoomMeetingDto("987654321", "https://us05web.zoom.us/j/987654321", null, "123456"));

        var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.Zoom), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.JoinUrl.Should().Be("https://us05web.zoom.us/j/987654321");
        evt.MeetingLink.Should().Be("https://us05web.zoom.us/j/987654321");
        _meetings.Verify(m => m.AddAsync(
            It.Is<CalendarEventMeeting>(cm => cm.Provider == CalendarEventMeetingProviders.Zoom && cm.ExternalMeetingId == "987654321"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ZoomConnectionMissingWriteScope_ReturnsMeetingProviderNotConnectedConflict()
    {
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeEvent());
        _connections.Setup(c => c.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.Zoom, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection
            {
                Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active,
                ScopesJson = "[\"meeting:read:meeting\"]" // no meeting:write:meeting
            });

        var result = await BuildSut().Handle(new CreateEventMeetingCommand(EventId, CalendarEventMeetingProviders.Zoom), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("meeting_provider_not_connected");
    }
}
