using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EfExternalCalendarConnectionRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task GetByTenantUserProviderAsync_ReturnsMatchingConnection()
    {
        await using var db = BuildInMemoryDb();
        var connection = MakeConnection(status: ExternalCalendarConnectionStatuses.Active);
        db.ExternalCalendarConnections.Add(connection);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfExternalCalendarConnectionRepository(db);
        var result = await repository.GetByTenantUserProviderAsync(TenantId, UserId, CalendarExternalSources.GoogleCalendar, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(connection.Id, result!.Id);
    }

    [Fact]
    public async Task GetActiveAsync_ExcludesDisabledConnections()
    {
        await using var db = BuildInMemoryDb();
        var active = MakeConnection(status: ExternalCalendarConnectionStatuses.Active);
        var failed = MakeConnection(status: ExternalCalendarConnectionStatuses.Failed);
        db.ExternalCalendarConnections.AddRange(active, failed);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfExternalCalendarConnectionRepository(db);
        var result = await repository.GetActiveAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(active.Id, result[0].Id);
    }

    private static ExternalCalendarConnection MakeConnection(string status) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId,
        Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "user@example.com",
        RefreshTokenEncrypted = [1, 2, 3], ScopesJson = "[]",
        SyncDirection = CalendarSyncDirections.TwoWay, Status = status, CreatedAt = DateTimeOffset.UtcNow
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
