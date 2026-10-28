using System.Text.Json;
using Moq;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarMeetingCancelOutboxHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OrganizerUserId = Guid.NewGuid();
    private readonly ExternalCalendarConnection _connection = new() { Id = Guid.NewGuid(), Status = ExternalCalendarConnectionStatuses.Active };
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarConnectionTokenProvider> _tokens = new();
    private readonly Mock<ITeamsMeetingClient> _teams = new();
    private readonly Mock<IZoomMeetingClient> _zoom = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();

    private CalendarMeetingCancelOutboxHandler BuildSut()
    {
        _connections.Setup(c => c.GetByIdForTenantAsync(TenantId, _connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_connection);
        _tokens.Setup(t => t.GetFreshAccessTokenAsync(_connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("token");
        return new CalendarMeetingCancelOutboxHandler(_connections.Object, _tokens.Object, _teams.Object, _zoom.Object, _notifications.Object);
    }

    private string Payload() => JsonSerializer.Serialize(new CalendarMeetingCancelPayload(
        TenantId, CalendarEventMeetingProviders.MicrosoftTeams, "graph-1", _connection.Id, OrganizerUserId, Guid.NewGuid(), "Review"));

    [Fact]
    public async Task Cancels_TheRemoteMeeting()
    {
        await BuildSut().HandleAsync(Payload(), CancellationToken.None);

        _teams.Verify(t => t.CancelMeetingAsync("token", "graph-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectionGone_TellsTheOrganizer_AndParksTheMessage()
    {
        var sut = BuildSut();
        _connections.Setup(c => c.GetByIdForTenantAsync(TenantId, _connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync((ExternalCalendarConnection?)null);

        await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => sut.HandleAsync(Payload(), CancellationToken.None));

        _notifications.Verify(n => n.NotifyOrganizerAboutMeetingAsync(
            TenantId, OrganizerUserId, CalendarMeetingNoticeCodes.CancelFailed, "Review", "Microsoft Teams", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProviderRefusesPermanently_ParksTheMessage()
    {
        var sut = BuildSut();
        _teams.Setup(t => t.CancelMeetingAsync("token", "graph-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Permanent, "403"));

        await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => sut.HandleAsync(Payload(), CancellationToken.None));
    }

    [Fact]
    public async Task TransientFailure_Propagates_ForTheOutboxBackoff()
    {
        var sut = BuildSut();
        _teams.Setup(t => t.CancelMeetingAsync("token", "graph-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, "503"));

        await Assert.ThrowsAsync<MeetingProviderException>(() => sut.HandleAsync(Payload(), CancellationToken.None));
        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unauthorized_RefreshesOnce_ThenSucceeds()
    {
        var sut = BuildSut();
        _tokens.Setup(t => t.ForceRefreshAccessTokenAsync(_connection, "microsoft", It.IsAny<CancellationToken>())).ReturnsAsync("token2");
        _teams.Setup(t => t.CancelMeetingAsync("token", "graph-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingProviderException(MeetingProviderFailureKind.Unauthorized, "401"));

        await sut.HandleAsync(Payload(), CancellationToken.None);

        _teams.Verify(t => t.CancelMeetingAsync("token2", "graph-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
