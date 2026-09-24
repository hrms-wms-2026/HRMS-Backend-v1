using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CreateEventMeetingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarConnectionTokenProvider> _tokenProvider = new();
    private readonly Mock<ITeamsMeetingClient> _teamsClient = new();
    private readonly Mock<IZoomMeetingClient> _zoomClient = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private CreateEventMeetingCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CreateEventMeetingResult>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CreateEventMeetingResult>>>, CancellationToken>((op, ct) => op(ct));
        return new CreateEventMeetingCommandHandler(
            _currentUser.Object, _events.Object, _connections.Object, _tokenProvider.Object,
            _teamsClient.Object, _zoomClient.Object, _meetings.Object, _unitOfWork.Object);
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
