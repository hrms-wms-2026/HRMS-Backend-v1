using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.RemoveEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class RemoveEventMeetingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarConnectionTokenProvider> _tokenProvider = new();
    private readonly Mock<ITeamsMeetingClient> _teamsClient = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private RemoveEventMeetingCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new RemoveEventMeetingCommandHandler(
            _currentUser.Object, _events.Object, _meetings.Object, _connections.Object,
            _tokenProvider.Object, _teamsClient.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_NoMeetingOnEvent_ReturnsSuccessNoOp()
    {
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = UserId });
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEventMeeting?)null);

        var result = await BuildSut().Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _teamsClient.Verify(t => t.CancelMeetingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HasMeeting_CancelsRemotelyAndClearsMeetingLink()
    {
        var evt = new CalendarEvent
        {
            Id = EventId, TenantId = TenantId, CreatedById = UserId, MeetingLink = "https://teams.microsoft.com/l/meetup-join/abc"
        };
        var connection = new ExternalCalendarConnection { Id = Guid.NewGuid() };
        var meeting = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId,
            ExternalCalendarConnectionId = connection.Id, ExternalMeetingId = "graph-meeting-1",
            Status = CalendarEventMeetingStatuses.Active
        };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(meeting);
        _connections.Setup(c => c.GetByIdForTenantAsync(TenantId, connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        _tokenProvider.Setup(t => t.GetFreshAccessTokenAsync(connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("access-token");

        var result = await BuildSut().Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        evt.MeetingLink.Should().BeNull();
        meeting.Status.Should().Be(CalendarEventMeetingStatuses.Cancelled);
        _teamsClient.Verify(t => t.CancelMeetingAsync("access-token", "graph-meeting-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotOrganizer_ReturnsForbidden()
    {
        var evt = new CalendarEvent { Id = EventId, TenantId = TenantId, CreatedById = Guid.NewGuid() };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);

        var result = await BuildSut().Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
