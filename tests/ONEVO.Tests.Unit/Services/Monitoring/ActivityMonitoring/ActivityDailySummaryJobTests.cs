using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using INotificationRepository = ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces.INotificationRepository;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Services.Monitoring.ActivityMonitoring;

/// <summary>
/// Regression coverage for the bug where the nightly aggregation ran under the background
/// scope's default TenantContextMode.System, which the FORCE RLS tenant_isolation policy on
/// activity_snapshots/app_usage_snapshots/activity_daily_summary admits for neither USING nor
/// WITH CHECK. The cross-tenant key sweep silently returned zero rows for every tenant, so
/// activity_daily_summary (and therefore application-usage data on the Summary/Attendance
/// History screens) was never populated. Mirrors LocationRuleEvaluatorJobTests.
/// </summary>
public class ActivityDailySummaryJobTests
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

    private static ActivitySnapshot MakeSnapshot(Guid tenantId, Guid employeeId, DateTimeOffset capturedAt) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, AgentDeviceId = Guid.NewGuid(),
        CapturedAt = capturedAt, KeyboardEventsCount = 10, MouseEventsCount = 10,
        ActiveSeconds = 60, IdleSeconds = 0, IntensityScore = 50m,
        ForegroundProcessName = "chrome.exe", CreatedAt = capturedAt
    };

    [Fact]
    public async Task RunAggregationAsync_EntersAdminModeThenSwitchesContextOncePerTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var empA1 = Guid.NewGuid();
        var empB1 = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 15);
        var now = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

        var contextModesAtSweep = new List<TenantContextMode>();
        var writableContext = new TenantContextAccessor();

        var snapshots = new Mock<IActivitySnapshotRepository>();
        snapshots.Setup(s => s.GetEmployeeKeysForDateAsync(date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                contextModesAtSweep.Add(writableContext.ContextMode);
                return new List<(Guid TenantId, Guid EmployeeId)> { (tenantA, empA1), (tenantB, empB1) };
            });
        // No snapshots for either employee once switched in - keeps this test focused on
        // tenant-context establishment, not the aggregation math (covered by
        // ActivityDailySummaryAggregatorTests).
        snapshots.Setup(s => s.GetAllByEmployeeDateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), date, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var appUsage = new Mock<IAppUsageSnapshotRepository>();
        var meetings = new Mock<IMeetingSignalRepository>();
        var summaries = new Mock<IActivityDailySummaryRepository>();
        var notifications = new Mock<INotificationRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var tenantSwitcher = new Mock<ITenantContextSwitcher>();
        var switchedTenantIds = new List<Guid>();
        tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Callback((TenantRegistryEntry e, CancellationToken _) => switchedTenantIds.Add(e.TenantId))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(snapshots.Object);
        services.AddSingleton(appUsage.Object);
        services.AddSingleton(meetings.Object);
        services.AddSingleton(summaries.Object);
        services.AddSingleton(notifications.Object);
        services.AddSingleton(unitOfWork.Object);
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider { UtcNow = now });
        services.AddSingleton<IWritableTenantContext>(writableContext);
        services.AddSingleton(tenantSwitcher.Object);
        services.AddSingleton(AnyTenantRepository());

        var job = new ActivityDailySummaryJob(services.BuildServiceProvider(), NullLogger<ActivityDailySummaryJob>.Instance);
        await job.RunAggregationAsync(date, CancellationToken.None);

        contextModesAtSweep.Should().ContainSingle().Which.Should().Be(TenantContextMode.Admin);
        switchedTenantIds.Should().BeEquivalentTo([tenantA, tenantB]);
    }

    [Fact]
    public async Task RunAggregationAsync_MultipleTenantsWithData_SavesEachTenantSeparately()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var empA1 = Guid.NewGuid();
        var empB1 = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 15);
        var capturedAt = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

        var snapshots = new Mock<IActivitySnapshotRepository>();
        snapshots.Setup(s => s.GetEmployeeKeysForDateAsync(date, It.IsAny<CancellationToken>()))
            .ReturnsAsync([(tenantA, empA1), (tenantB, empB1)]);
        snapshots.Setup(s => s.GetAllByEmployeeDateAsync(tenantA, empA1, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeSnapshot(tenantA, empA1, capturedAt)]);
        snapshots.Setup(s => s.GetAllByEmployeeDateAsync(tenantB, empB1, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeSnapshot(tenantB, empB1, capturedAt)]);

        var appUsage = new Mock<IAppUsageSnapshotRepository>();
        appUsage.Setup(a => a.GetAllByEmployeeDateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), date, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var meetings = new Mock<IMeetingSignalRepository>();
        meetings.Setup(m => m.GetAllByEmployeeDateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), date, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var upsertedTenantIds = new List<Guid>();
        var summaries = new Mock<IActivityDailySummaryRepository>();
        summaries.Setup(s => s.UpsertAsync(It.IsAny<ActivityDailySummary>(), It.IsAny<CancellationToken>()))
            .Callback((ActivityDailySummary sum, CancellationToken _) => upsertedTenantIds.Add(sum.TenantId))
            .Returns(Task.CompletedTask);

        var notifications = new Mock<INotificationRepository>();

        // Records tenant-switch and save events in call order, so we can assert each tenant's
        // work is flushed before the next tenant's context is established - not batched once
        // at the end under whichever tenant was switched to last.
        var eventLog = new List<string>();
        var tenantSwitcher = new Mock<ITenantContextSwitcher>();
        tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Callback((TenantRegistryEntry e, CancellationToken _) => eventLog.Add($"switch:{e.TenantId}"))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => eventLog.Add("save"))
            .ReturnsAsync(1);

        var services = new ServiceCollection();
        services.AddSingleton(snapshots.Object);
        services.AddSingleton(appUsage.Object);
        services.AddSingleton(meetings.Object);
        services.AddSingleton(summaries.Object);
        services.AddSingleton(notifications.Object);
        services.AddSingleton(unitOfWork.Object);
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider { UtcNow = now });
        services.AddSingleton<IWritableTenantContext>(new TenantContextAccessor());
        services.AddSingleton(tenantSwitcher.Object);
        services.AddSingleton(AnyTenantRepository());

        var job = new ActivityDailySummaryJob(services.BuildServiceProvider(), NullLogger<ActivityDailySummaryJob>.Instance);
        await job.RunAggregationAsync(date, CancellationToken.None);

        upsertedTenantIds.Should().BeEquivalentTo([tenantA, tenantB]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Each tenant's save must happen immediately after that tenant's switch and before the
        // next tenant's switch: switch:A, save, switch:B, save (order of A/B depends on
        // GroupBy's encounter order, but the save-immediately-after-switch shape must hold).
        eventLog.Should().HaveCount(4);
        for (var i = 0; i < eventLog.Count; i += 2)
        {
            eventLog[i].Should().StartWith("switch:");
            eventLog[i + 1].Should().Be("save");
        }
    }
}
