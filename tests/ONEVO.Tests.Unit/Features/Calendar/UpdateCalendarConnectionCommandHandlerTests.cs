using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.UpdateCalendarConnection;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class UpdateCalendarConnectionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private UpdateCalendarConnectionCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<ONEVO.Application.Features.Calendar.DTOs.Responses.CalendarConnectionItem>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<ONEVO.Application.Features.Calendar.DTOs.Responses.CalendarConnectionItem>>>, CancellationToken>(
                (action, ct) => action(ct));
        return new UpdateCalendarConnectionCommandHandler(_currentUser.Object, _connections.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_ConnectionNotFound_ReturnsNotFound()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCalendarConnection?)null);

        var result = await sut.Handle(new UpdateCalendarConnectionCommand(ConnectionId, CalendarSyncDirections.PullOnly), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection { Id = ConnectionId, TenantId = TenantId, UserId = Guid.NewGuid(), Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1] });

        var result = await sut.Handle(new UpdateCalendarConnectionCommand(ConnectionId, CalendarSyncDirections.PullOnly), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InvalidSyncDirection_ReturnsFailure400()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalCalendarConnection { Id = ConnectionId, TenantId = TenantId, UserId = UserId, Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1] });

        var result = await sut.Handle(new UpdateCalendarConnectionCommand(ConnectionId, "not_a_real_direction"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Owner_UpdatesSyncDirection()
    {
        var sut = BuildSut();
        var existing = new ExternalCalendarConnection { Id = ConnectionId, TenantId = TenantId, UserId = UserId, Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1], SyncDirection = CalendarSyncDirections.TwoWay };
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await sut.Handle(new UpdateCalendarConnectionCommand(ConnectionId, CalendarSyncDirections.PullOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarSyncDirections.PullOnly, existing.SyncDirection);
        _connections.Verify(x => x.Update(existing), Times.Once);
    }
}
