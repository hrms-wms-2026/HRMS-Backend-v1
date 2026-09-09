using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.TriggerCalendarSync;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class TriggerCalendarSyncCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<ICalendarSyncService> _syncService = new();

    private TriggerCalendarSyncCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new TriggerCalendarSyncCommandHandler(_currentUser.Object, _connections.Object, _syncService.Object);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection { Id = ConnectionId, TenantId = TenantId, UserId = Guid.NewGuid(), Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1] });

        var result = await sut.Handle(new TriggerCalendarSyncCommand(ConnectionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _syncService.Verify(x => x.SyncConnectionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Owner_InvokesSyncService()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection { Id = ConnectionId, TenantId = TenantId, UserId = UserId, Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1] });

        var result = await sut.Handle(new TriggerCalendarSyncCommand(ConnectionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _syncService.Verify(x => x.SyncConnectionAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
