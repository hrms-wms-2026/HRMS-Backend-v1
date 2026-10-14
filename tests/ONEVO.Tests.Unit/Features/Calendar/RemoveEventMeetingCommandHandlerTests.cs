using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.RemoveEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class RemoveEventMeetingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarConnectionTokenProvider> _tokens = new();
    private readonly Mock<ITeamsMeetingClient> _teams = new();
    private readonly Mock<IZoomMeetingClient> _zoom = new();
    private readonly Mock<IMeetingProvisioner> _provisioner = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly CalendarEvent _evt = new()
    {
        Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Review", MeetingLink = "https://teams/j/1",
        StartDate = Now.AddDays(1), EndDate = Now.AddDays(1).AddHours(1)
    };
    private readonly ExternalCalendarConnection _connection = new() { Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active };
    private CalendarEventMeeting _row = null!;

    private RemoveEventMeetingCommandHandler BuildSut(string? status, string provider = CalendarEventMeetingProviders.MicrosoftTeams)
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(_evt);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>());
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
        _row = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId, Provider = provider, Status = status ?? "",
            ExternalMeetingId = "remote-1", JoinUrl = "https://teams/j/1", LinkGeneration = 2, ExternalCalendarConnectionId = _connection.Id
        };
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(status is null ? null : _row);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(status is null ? null : _row);
        _connections.Setup(c => c.GetByIdForTenantAsync(TenantId, _connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_connection);
        _tokens.Setup(t => t.GetFreshAccessTokenAsync(_connection, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result>>, CancellationToken>((op, ct) => op(ct));
        var employees = new Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>();
        employees.Setup(e => e.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        var links = new EventMeetingLinkService(_events.Object, _meetings.Object, _connections.Object, _provisioner.Object,
            _notifications.Object, _unitOfWork.Object, _clock.Object, Mock.Of<IOutboxWriter>());
        return new RemoveEventMeetingCommandHandler(_currentUser.Object, _events.Object, _meetings.Object, _connections.Object,
            _tokens.Object, _teams.Object, _zoom.Object, links, employees.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task NoRow_ManualLink_IsClearedLocally()
    {
        var result = await BuildSut(status: null).Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(_evt.MeetingLink);
    }

    [Fact]
    public async Task Active_CancelsRemotely_ClearsTheLink_AndTellsEveryoneOncePerGeneration()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.Active);
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" };
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [EventId] = [guest] });

        var result = await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _teams.Verify(t => t.CancelMeetingAsync("token", "remote-1", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, _row.Status);
        Assert.Null(_evt.MeetingLink);
        _notifications.Verify(n => n.NotifyMeetingLinkRemovedAsync(
            TenantId, "Review", _evt.StartDate, null, It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().EmailKey == $"link-removed:{_row.Id}:2:{guest.Id}"),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Active_ZoomInProgress_Is409_AndLeavesEverything()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.Active, CalendarEventMeetingProviders.Zoom);
        _zoom.Setup(z => z.CancelMeetingAsync("token", "remote-1", It.IsAny<CancellationToken>())).ThrowsAsync(new ZoomMeetingInProgressException());

        var result = await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(CalendarEventMeetingStatuses.Active, _row.Status);
        Assert.Equal("https://teams/j/1", _evt.MeetingLink);
    }

    [Fact]
    public async Task Active_ProviderUnavailable_Is502_AndLeavesEverything()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.Active);
        _teams.Setup(t => t.CancelMeetingAsync("token", "remote-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, "503"));

        var result = await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal(502, result.StatusCode);
        Assert.Equal(CalendarEventMeetingStatuses.Active, _row.Status);
    }

    [Fact]
    public async Task Unknown_ResolvedRemotely_IsCancelled()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.ProvisioningUnknown);
        _meetings.Setup(m => m.TryClaimUnknownAsync(TenantId, _row.Id, Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _provisioner.Setup(p => p.ResolveForRemovalAsync(It.Is<MeetingProvisioningRequest>(r => r.Generation == 2), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, _row.Status);
    }

    [Fact]
    public async Task Unknown_CantResolve_IsUnresolved_AndTheOrganizerIsWarned()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.ProvisioningUnknown, CalendarEventMeetingProviders.Zoom);
        _meetings.Setup(m => m.TryClaimUnknownAsync(TenantId, _row.Id, Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _provisioner.Setup(p => p.ResolveForRemovalAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal("meeting_link_unresolved", result.Error);
        Assert.Equal(CalendarEventMeetingStatuses.Unresolved, _row.Status);
        _notifications.Verify(n => n.NotifyOrganizerAboutMeetingAsync(
            TenantId, UserId, CalendarMeetingNoticeCodes.Unresolved, "Review", "Zoom", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CalendarEventMeetingStatuses.Provisioning, "meeting_link_in_progress")]
    [InlineData(CalendarEventMeetingStatuses.Unresolved, "meeting_link_unresolved")]
    public async Task InProgressOrUnresolved_Refuses(string status, string error)
    {
        var result = await BuildSut(status).Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal(error, result.Error);
    }

    [Fact]
    public async Task NotOrganizer_Forbidden()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.Active);
        _evt.CreatedById = Guid.NewGuid();

        Assert.Equal(403, (await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task StaleProvisioning_IsRecoveredAsUnknown_ThenResolvedRemotely()
    {
        // The request that claimed the row died: Remove must not answer "in progress" forever.
        var sut = BuildSut(CalendarEventMeetingStatuses.Provisioning);
        _row.ProvisioningStartedAt = Now.AddMinutes(-6);
        _meetings.Setup(m => m.TryMarkStaleProvisioningUnknownAsync(TenantId, _row.Id, Now.AddMinutes(-5), Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _meetings.Setup(m => m.TryClaimUnknownAsync(TenantId, _row.Id, Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _provisioner.Setup(p => p.ResolveForRemovalAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await sut.Handle(new RemoveEventMeetingCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, _row.Status);
    }
}
