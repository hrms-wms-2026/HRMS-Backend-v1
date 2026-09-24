using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.DisconnectCalendarConnection;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class DisconnectCalendarConnectionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();
    private readonly Mock<IExternalCalendarEventLinkRepository> _links = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private DisconnectCalendarConnectionCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<MediatR.Unit>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<MediatR.Unit>>>, CancellationToken>((action, ct) => action(ct));
        return new DisconnectCalendarConnectionCommandHandler(_currentUser.Object, _connections.Object, _links.Object, _events.Object, _unitOfWork.Object);
    }

    private static ExternalCalendarConnection OwnedConnection() => new()
    {
        Id = ConnectionId, TenantId = TenantId, UserId = UserId,
        Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "x@y.com", RefreshTokenEncrypted = [1]
    };

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        var other = OwnedConnection();
        other.UserId = Guid.NewGuid();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(other);

        var result = await sut.Handle(new DisconnectCalendarConnectionCommand(ConnectionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Owner_RemovesConnectionAndOnlyExternallySourcedLinkedEvents()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(OwnedConnection());
        var externalEvent = new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Synced", SourceType = CalendarEventSourceTypes.ExternalSync, CreatedById = UserId };
        var convertedEvent = new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Kept", SourceType = CalendarEventSourceTypes.Manual, CreatedById = UserId };
        var links = new List<ExternalCalendarEventLink>
        {
            new() { Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = externalEvent.Id, ExternalCalendarConnectionId = ConnectionId },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = convertedEvent.Id, ExternalCalendarConnectionId = ConnectionId }
        };
        _links.Setup(x => x.GetByConnectionIdAsync(TenantId, ConnectionId, It.IsAny<CancellationToken>())).ReturnsAsync(links);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, externalEvent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(externalEvent);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, convertedEvent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(convertedEvent);

        var result = await sut.Handle(new DisconnectCalendarConnectionCommand(ConnectionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.Remove(It.Is<CalendarEvent>(e => e.Id == externalEvent.Id)), Times.Once);
        _events.Verify(x => x.Remove(It.Is<CalendarEvent>(e => e.Id == convertedEvent.Id)), Times.Never);
        _links.Verify(x => x.RemoveRange(links), Times.Once);
        _connections.Verify(x => x.Remove(It.Is<ExternalCalendarConnection>(c => c.Id == ConnectionId)), Times.Once);
    }
}
