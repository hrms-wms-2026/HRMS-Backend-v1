using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarSyncJobTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Tenant MakeTenant() => new()
    {
        Id = TenantId, Name = "Acme", Slug = "acme", Status = TenantStatus.Active
    };

    private static ExternalCalendarConnection MakeConnection() => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(),
        Provider = CalendarExternalSources.GoogleCalendar, ExternalAccountEmail = "me@acme.com",
        ExternalCalendarId = "me@acme.com", RefreshTokenEncrypted = [1],
        SyncDirection = CalendarSyncDirections.PullOnly, Status = ExternalCalendarConnectionStatuses.Active
    };

    [Fact]
    public async Task RunOnceAsync_OneConnectionThrows_StillSyncsRemainingConnections()
    {
        var services = new ServiceCollection();

        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock
            .Setup(t => t.ListAsync(TenantStatus.Active, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant> { MakeTenant() });
        tenantRepoMock
            .Setup(t => t.ListAsync(TenantStatus.Active, null, 100, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant>());

        var failingConnection = MakeConnection();
        var succeedingConnection = MakeConnection();

        var connectionsRepoMock = new Mock<IExternalCalendarConnectionRepository>();
        connectionsRepoMock
            .Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalCalendarConnection> { failingConnection, succeedingConnection });

        var syncServiceMock = new Mock<ICalendarSyncService>();
        syncServiceMock
            .Setup(s => s.SyncConnectionAsync(TenantId, failingConnection.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated DbUpdateException from a concurrent sync-now request"));
        syncServiceMock
            .Setup(s => s.SyncConnectionAsync(TenantId, succeedingConnection.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(connectionsRepoMock.Object);
        services.AddSingleton(syncServiceMock.Object);
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        var provider = services.BuildServiceProvider();

        var job = new CalendarSyncJob(provider, NullLogger<CalendarSyncJob>.Instance);

        // Should not throw - the failing connection's exception must be caught and logged, not
        // propagated, and the second connection must still be synced.
        await job.RunOnceAsync(CancellationToken.None);

        syncServiceMock.Verify(s => s.SyncConnectionAsync(TenantId, failingConnection.Id, It.IsAny<CancellationToken>()), Times.Once);
        syncServiceMock.Verify(s => s.SyncConnectionAsync(TenantId, succeedingConnection.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_OneTenantThrowsDuringSwitch_StillProcessesRemainingTenants()
    {
        var services = new ServiceCollection();

        var badTenant = MakeTenant();
        var goodTenant = new Tenant { Id = Guid.NewGuid(), Name = "Beta", Slug = "beta", Status = TenantStatus.Active };

        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock
            .Setup(t => t.ListAsync(TenantStatus.Active, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant> { badTenant, goodTenant });
        tenantRepoMock
            .Setup(t => t.ListAsync(TenantStatus.Active, null, 100, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant>());

        var connection = MakeConnection();
        var connectionsRepoMock = new Mock<IExternalCalendarConnectionRepository>();
        connectionsRepoMock
            .Setup(c => c.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalCalendarConnection> { connection });

        var syncServiceMock = new Mock<ICalendarSyncService>();
        syncServiceMock
            .Setup(s => s.SyncConnectionAsync(It.IsAny<Guid>(), connection.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var switcherMock = new Mock<ITenantContextSwitcher>();
        switcherMock
            .Setup(s => s.SwitchToTenantAsync(It.Is<TenantRegistryEntry>(e => e.TenantId == badTenant.Id), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated tenant context switch failure"));
        switcherMock
            .Setup(s => s.SwitchToTenantAsync(It.Is<TenantRegistryEntry>(e => e.TenantId == goodTenant.Id), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(connectionsRepoMock.Object);
        services.AddSingleton(syncServiceMock.Object);
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(switcherMock.Object);
        var provider = services.BuildServiceProvider();

        var job = new CalendarSyncJob(provider, NullLogger<CalendarSyncJob>.Instance);

        // Should not throw - the bad tenant's failure must be caught and logged, not propagated,
        // and the good tenant must still be processed (its connection synced).
        await job.RunOnceAsync(CancellationToken.None);

        syncServiceMock.Verify(s => s.SyncConnectionAsync(goodTenant.Id, connection.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
