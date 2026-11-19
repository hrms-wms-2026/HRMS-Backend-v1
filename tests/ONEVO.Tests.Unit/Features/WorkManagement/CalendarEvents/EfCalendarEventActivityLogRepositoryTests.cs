using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.CalendarEvents;

public sealed class EfCalendarEventActivityLogRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task AddAsync_ThenListByEventIdAsync_ReturnsNewestFirst()
    {
        await using var db = BuildInMemoryDb();
        var repo = new EfCalendarEventActivityLogRepository(db);
        var eventId = Guid.NewGuid();
        var older = new CalendarEventActivityLog { Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = eventId, Action = CalendarEventActivityActions.Created, PerformedById = Guid.NewGuid(), PerformedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var newer = new CalendarEventActivityLog { Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = eventId, Action = CalendarEventActivityActions.Updated, PerformedById = Guid.NewGuid(), PerformedAt = DateTimeOffset.UtcNow };
        await repo.AddAsync(older, CancellationToken.None);
        await repo.AddAsync(newer, CancellationToken.None);
        await db.SaveChangesAsync();

        var result = await repo.ListByEventIdAsync(eventId, CancellationToken.None);

        Assert.Equal(new[] { newer.Id, older.Id }, result.Select(r => r.Id));
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
