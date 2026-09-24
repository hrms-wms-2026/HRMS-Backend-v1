using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetMyCalendarConnections;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GetMyCalendarConnectionsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExternalCalendarConnectionRepository> _connections = new();

    private GetMyCalendarConnectionsQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new GetMyCalendarConnectionsQueryHandler(_currentUser.Object, _connections.Object);
    }

    [Fact]
    public async Task Handle_ReturnsCallersConnections_NeverIncludingTokenFields()
    {
        var sut = BuildSut();
        _connections.Setup(x => x.GetForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ExternalCalendarConnection
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId,
                    Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "me@acme.com",
                    ExternalCalendarName = "Primary", SyncDirection = CalendarSyncDirections.TwoWay,
                    Status = ExternalCalendarConnectionStatuses.Active,
                    RefreshTokenEncrypted = [9, 9, 9], AccessTokenEncrypted = [8, 8, 8]
                }
            ]);

        var result = await sut.Handle(new GetMyCalendarConnectionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Connections);
        Assert.Equal("me@acme.com", result.Value.Connections[0].ExternalAccountEmail);
        // CalendarConnectionItem's constructor has no token-field parameters at all - the type
        // system already guarantees they can't leak; this test documents that guarantee.
    }
}
