using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Services.Monitoring.Notifications;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Services.Monitoring.Notifications;

/// <summary>
/// Regression coverage for the same background-scope-defaults-to-System bug fixed in
/// ActivityDailySummaryJob/LocationRuleEvaluatorJob: WellnessRuleEvaluatorJob's cross-tenant
/// discovery sweep over device_state_snapshots (FORCE RLS, admin-or-matching-tenant policy) ran
/// with no admin/tenant context established, so it silently scanned zero employees every tick.
/// </summary>
public class WellnessRuleEvaluatorJobTests
{
    private static ITenantRepository AnyTenantRepository()
    {
        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Tenant
            {
                Id = id, Name = "Test", Slug = "test", Status = TenantStatus.Active
            });
        return tenants.Object;
    }

    [Fact]
    public async Task RunOnceAsync_EntersAdminModeThenSwitchesContextOncePerTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var empA1 = Guid.NewGuid();
        var empB1 = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var contextModesAtSweep = new List<TenantContextMode>();
        var writableContext = new TenantContextAccessor();

        var deviceState = new Mock<IDeviceStateSnapshotRepository>();
        deviceState.Setup(d => d.GetActiveEmployeeKeysAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                contextModesAtSweep.Add(writableContext.ContextMode);
                return new List<(Guid TenantId, Guid EmployeeId)> { (tenantA, empA1), (tenantB, empB1) };
            });
        deviceState.Setup(d => d.GetRecentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tenantSwitcher = new Mock<ITenantContextSwitcher>();
        var switchedTenantIds = new List<Guid>();
        tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Callback((TenantRegistryEntry e, CancellationToken _) => switchedTenantIds.Add(e.TenantId))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(deviceState.Object);
        services.AddSingleton(new Mock<INotificationRepository>().Object);
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider { UtcNow = now });
        services.AddSingleton<IWritableTenantContext>(writableContext);
        services.AddSingleton(tenantSwitcher.Object);
        services.AddSingleton(AnyTenantRepository());

        var job = new WellnessRuleEvaluatorJob(services.BuildServiceProvider(), NullLogger<WellnessRuleEvaluatorJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        contextModesAtSweep.Should().ContainSingle().Which.Should().Be(TenantContextMode.Admin);
        switchedTenantIds.Should().BeEquivalentTo([tenantA, tenantB]);
    }
}
