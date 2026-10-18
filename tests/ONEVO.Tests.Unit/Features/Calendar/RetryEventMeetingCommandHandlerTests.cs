using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.Commands.RetryEventMeeting;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class RetryEventMeetingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarEventMeetingRepository> _meetings = new();
    private readonly Mock<IMeetingProvisioner> _provisioner = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly CalendarEvent _evt = new() { Id = EventId, TenantId = TenantId, CreatedById = UserId, Title = "Review", StartDate = Now.AddDays(1), EndDate = Now.AddDays(1).AddHours(1) };
    private readonly ExternalCalendarConnection _connection = new() { Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active };

    private RetryEventMeetingCommandHandler BuildSut(string status)
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(_evt);
        _events.Setup(e => e.GetByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(_evt);
        _events.Setup(e => e.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>());
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
        var row = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = EventId, Status = status, LinkGeneration = 2,
            Provider = CalendarEventMeetingProviders.MicrosoftTeams, ExternalCalendarConnectionId = _connection.Id
        };
        _meetings.Setup(m => m.GetByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _meetings.Setup(m => m.TryClaimUnknownAsync(TenantId, row.Id, Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _meetings.Setup(m => m.GetTrackedByCalendarEventAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _connections.Setup(c => c.GetByIdForTenantAsync(TenantId, _connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_connection);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<CreateEventMeetingResult>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<Result<CreateEventMeetingResult>>>, CancellationToken>((op, ct) => op(ct));
        var links = new EventMeetingLinkService(_events.Object, _meetings.Object, _connections.Object, _provisioner.Object,
            Mock.Of<ICalendarNotificationSender>(), _unitOfWork.Object, _clock.Object, Mock.Of<IOutboxWriter>(),
            Mock.Of<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>());
        return new RetryEventMeetingCommandHandler(_currentUser.Object, _events.Object, _connections.Object, _meetings.Object, links,
            Mock.Of<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>(), _clock.Object);
    }

    [Fact]
    public async Task Unknown_RecoversTheSameGeneration()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.ProvisioningUnknown);
        _provisioner.Setup(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting("g", "https://teams/j/2", null, null), null));

        var result = await sut.Handle(new RetryEventMeetingCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _provisioner.Verify(p => p.ProvisionAsync(It.Is<MeetingProvisioningRequest>(r => r.Recovery && r.Generation == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CalendarEventMeetingStatuses.Active)]
    [InlineData(CalendarEventMeetingStatuses.Failed)]
    public async Task NotUnknown_IsNotRetryable(string status)
    {
        var result = await BuildSut(status).Handle(new RetryEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal("meeting_link_not_retryable", result.Error);
        _provisioner.Verify(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnknownButTheEventEnded_IsUnresolved_NotRetried()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.ProvisioningUnknown);
        _evt.EndDate = Now.AddHours(-1);
        _meetings.Setup(m => m.TryMarkUnknownUnresolvedAsync(TenantId, It.IsAny<Guid>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await sut.Handle(new RetryEventMeetingCommand(EventId), CancellationToken.None);

        Assert.Equal("meeting_link_unresolved", result.Error);
    }

    [Fact]
    public async Task StaleProvisioning_IsRecoveredAsUnknown_ThenRetried()
    {
        var sut = BuildSut(CalendarEventMeetingStatuses.Provisioning);
        _meetings.Setup(m => m.TryMarkStaleProvisioningUnknownAsync(TenantId, It.IsAny<Guid>(), Now.AddMinutes(-5), Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _provisioner.Setup(p => p.ProvisionAsync(It.IsAny<MeetingProvisioningRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Created, new ProvisionedMeeting("g", "https://teams/j/2", null, null), null));

        var result = await sut.Handle(new RetryEventMeetingCommand(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _provisioner.Verify(p => p.ProvisionAsync(It.Is<MeetingProvisioningRequest>(r => r.Recovery), It.IsAny<CancellationToken>()), Times.Once);
    }
}
