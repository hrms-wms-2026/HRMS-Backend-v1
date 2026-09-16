using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Services.Monitoring.Exceptions;
using ONEVO.Tests.Unit.Fakes;
using Xunit;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Services.Monitoring.Exceptions;

/// <summary>
/// Regression coverage for the same background-scope-defaults-to-System bug fixed in
/// ActivityDailySummaryJob/LocationRuleEvaluatorJob. ExceptionDetectionJob had two instances of
/// it: (1) the initial cross-tenant discovery sweep over activity_daily_summary ran with no
/// admin context, so it always scanned zero employees; (2) even after per-tenant switching was
/// added for the main employee loop, the escalation sweep ran in a second loop AFTER the main
/// loop had moved the connection to whichever tenant was switched to last - so GetStaleOpenAsync
/// silently returned zero rows for every tenant but that one.
/// </summary>
public class ExceptionDetectionJobTests
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

    private static readonly ProductivityAggregate ZeroAggregate = new(0, 0, 0, 0, 0, 0, 0m, 0, 0, 0);

    [Fact]
    public async Task RunDetectionAsync_EntersAdminModeThenSwitchesContextOncePerTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var empA1 = Guid.NewGuid();
        var empB1 = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 9, 15);
        var now = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

        var contextModesAtSweep = new List<TenantContextMode>();
        var writableContext = new TenantContextAccessor();

        var exceptions = new Mock<IExceptionRepository>();
        exceptions.Setup(e => e.GetActiveTenantEmployeeKeysAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                contextModesAtSweep.Add(writableContext.ContextMode);
                return new List<(Guid TenantId, Guid EmployeeId)> { (tenantA, empA1), (tenantB, empB1) };
            });
        exceptions.Setup(e => e.GetStaleOpenAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var summaries = new Mock<IActivityDailySummaryRepository>();
        summaries.Setup(s => s.GetRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var reports = new Mock<IProductivityReportRepository>();
        reports.Setup(r => r.GetEmployeeAggregateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ZeroAggregate);

        var tenantSwitcher = new Mock<ITenantContextSwitcher>();
        var switchedTenantIds = new List<Guid>();
        tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Callback((TenantRegistryEntry e, CancellationToken _) => switchedTenantIds.Add(e.TenantId))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(exceptions.Object);
        services.AddSingleton(summaries.Object);
        services.AddSingleton(reports.Object);
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider { UtcNow = now });
        services.AddSingleton<IWritableTenantContext>(writableContext);
        services.AddSingleton(tenantSwitcher.Object);
        services.AddSingleton(AnyTenantRepository());

        var job = new ExceptionDetectionJob(services.BuildServiceProvider(), NullLogger<ExceptionDetectionJob>.Instance);
        await job.RunDetectionAsync(targetDate, CancellationToken.None);

        contextModesAtSweep.Should().ContainSingle().Which.Should().Be(TenantContextMode.Admin);
        switchedTenantIds.Should().BeEquivalentTo([tenantA, tenantB]);
    }

    [Fact]
    public async Task RunDetectionAsync_EscalationSweep_RunsUnderEachTenantsOwnContext()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var empA1 = Guid.NewGuid();
        var empB1 = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 9, 15);
        var now = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

        var exceptions = new Mock<IExceptionRepository>();
        exceptions.Setup(e => e.GetActiveTenantEmployeeKeysAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(tenantA, empA1), (tenantB, empB1)]);

        // Event log records tenant switches and which tenant each escalation query ran for, in
        // call order - proving GetStaleOpenAsync(tenantId, ...) for tenant X only ever runs
        // immediately after tenant X's own SwitchToTenantAsync, not after some other tenant's.
        var eventLog = new List<string>();
        exceptions.Setup(e => e.GetStaleOpenAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback((Guid tenantId, DateTimeOffset _, CancellationToken _) => eventLog.Add($"staleQuery:{tenantId}"))
            .ReturnsAsync([]);

        var summaries = new Mock<IActivityDailySummaryRepository>();
        summaries.Setup(s => s.GetRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var reports = new Mock<IProductivityReportRepository>();
        reports.Setup(r => r.GetEmployeeAggregateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ZeroAggregate);

        var tenantSwitcher = new Mock<ITenantContextSwitcher>();
        tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Callback((TenantRegistryEntry e, CancellationToken _) => eventLog.Add($"switch:{e.TenantId}"))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(exceptions.Object);
        services.AddSingleton(summaries.Object);
        services.AddSingleton(reports.Object);
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider { UtcNow = now });
        services.AddSingleton<IWritableTenantContext>(new TenantContextAccessor());
        services.AddSingleton(tenantSwitcher.Object);
        services.AddSingleton(AnyTenantRepository());

        var job = new ExceptionDetectionJob(services.BuildServiceProvider(), NullLogger<ExceptionDetectionJob>.Instance);
        await job.RunDetectionAsync(targetDate, CancellationToken.None);

        exceptions.Verify(e => e.GetStaleOpenAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        eventLog.Should().HaveCount(4);
        for (var i = 0; i < eventLog.Count; i += 2)
        {
            eventLog[i].Should().StartWith("switch:");
            var switchedTenant = eventLog[i]["switch:".Length..];
            eventLog[i + 1].Should().Be($"staleQuery:{switchedTenant}",
                "the escalation sweep for a tenant must run immediately after that tenant's own switch");
        }
    }
}
