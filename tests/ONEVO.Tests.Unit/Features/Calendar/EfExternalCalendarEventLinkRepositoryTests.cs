using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EfExternalCalendarEventLinkRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();
    private static readonly Guid CalendarEventId = Guid.NewGuid();

    [Fact]
    public async Task GetTrackedByConnectionAndExternalEventAsync_ReturnsMatchingLink()
    {
        await using var db = BuildInMemoryDb();
        var link = MakeLink(connectionId: ConnectionId, externalEventId: "external-123");
        db.ExternalCalendarEventLinks.Add(link);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfExternalCalendarEventLinkRepository(db);
        var result = await repository.GetTrackedByConnectionAndExternalEventAsync(TenantId, ConnectionId, "external-123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(link.Id, result!.Id);
    }

    [Fact]
    public async Task GetByConnectionIdAsync_ReturnsAllLinksForConnection()
    {
        await using var db = BuildInMemoryDb();
        var connectionId1 = Guid.NewGuid();
        var connectionId2 = Guid.NewGuid();

        var link1 = MakeLink(connectionId: connectionId1, externalEventId: "event-1");
        var link2 = MakeLink(connectionId: connectionId1, externalEventId: "event-2");
        var link3 = MakeLink(connectionId: connectionId2, externalEventId: "event-3");

        db.ExternalCalendarEventLinks.AddRange(link1, link2, link3);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfExternalCalendarEventLinkRepository(db);
        var result = await repository.GetByConnectionIdAsync(TenantId, connectionId1, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, l => l.Id == link1.Id);
        Assert.Contains(result, l => l.Id == link2.Id);
        Assert.DoesNotContain(result, l => l.Id == link3.Id);
    }

    private static ExternalCalendarEventLink MakeLink(Guid connectionId, string externalEventId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = CalendarEventId,
        ExternalCalendarConnectionId = connectionId, Provider = CalendarExternalSources.GoogleCalendar,
        ExternalCalendarId = "calendar@google.com", ExternalEventId = externalEventId,
        SyncDirection = ExternalCalendarLinkDirections.Inbound, SyncStatus = ExternalCalendarSyncStatuses.Synced,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ONEVO.Application.Common.ServiceInterfaces.ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
