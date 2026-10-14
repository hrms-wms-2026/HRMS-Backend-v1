using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Services.CoreHr;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class OfficeProjectReconciliationStartupServiceTests
{
    private static (OfficeProjectReconciliationStartupService Service, Mock<IOfficeProjectReconciler> Reconciler) Build(
        IReadOnlyList<Tenant> tenants, string environment = "Development")
    {
        var repository = new Mock<ITenantRepository>();
        SetupPages(repository, tenants);
        var reconciler = new Mock<IOfficeProjectReconciler>();
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(e => e.EnvironmentName).Returns(environment);

        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        services.AddSingleton(reconciler.Object);
        var provider = services.BuildServiceProvider();

        return (new OfficeProjectReconciliationStartupService(
            provider, hostEnvironment.Object, NullLogger<OfficeProjectReconciliationStartupService>.Instance), reconciler);
    }

    private static IReadOnlyList<Tenant> Page(IEnumerable<Tenant> tenants, Guid? afterId, int take) =>
        tenants.OrderBy(t => t.Id).Where(t => afterId is null || t.Id.CompareTo(afterId.Value) > 0).Take(take).ToList();

    private static void SetupPages(Mock<ITenantRepository> repository, IEnumerable<Tenant> tenants) =>
        repository
            .Setup(r => r.ListPageByIdAsync(It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid? afterId, int take, CancellationToken _) => Page(tenants, afterId, take));

    private static Tenant TenantWith(TenantStatus status) => new()
    {
        Id = Guid.NewGuid(), Name = "T", Slug = $"t-{Guid.NewGuid():N}"[..12], Status = status
    };

    [Fact]
    public async Task RunOnceAsync_ReconcilesLiveTenants_SkipsSuspendedAndCancelled()
    {
        var live = new[] { TenantWith(TenantStatus.Active), TenantWith(TenantStatus.Trial), TenantWith(TenantStatus.Provisioning) };
        var dead = new[] { TenantWith(TenantStatus.Suspended), TenantWith(TenantStatus.Cancelled) };
        var (service, reconciler) = Build(live.Concat(dead).ToList());

        await service.RunOnceAsync(CancellationToken.None);

        foreach (var tenant in live)
            reconciler.Verify(r => r.ReconcileAsync(tenant.Id, It.IsAny<CancellationToken>()), Times.Once);
        foreach (var tenant in dead)
            reconciler.Verify(r => r.ReconcileAsync(tenant.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_OneTenantThrows_OthersStillReconciled()
    {
        var tenants = new[] { TenantWith(TenantStatus.Active), TenantWith(TenantStatus.Active) };
        var (service, reconciler) = Build(tenants);
        reconciler.Setup(r => r.ReconcileAsync(tenants[0].Id, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        await service.RunOnceAsync(CancellationToken.None);

        reconciler.Verify(r => r.ReconcileAsync(tenants[1].Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_MoreThanOnePage_ReconcilesEveryTenant()
    {
        var tenants = Enumerable.Range(0, OfficeProjectReconciliationStartupService.PageSize + 3)
            .Select(_ => TenantWith(TenantStatus.Active)).ToList();
        var (service, reconciler) = Build(tenants);

        await service.RunOnceAsync(CancellationToken.None);

        reconciler.Verify(r => r.ReconcileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(tenants.Count));
    }

    [Fact]
    public async Task ExecuteAsync_TenantListThrows_IsLoggedAndDoesNotEscape()
    {
        var repository = new Mock<ITenantRepository>();
        repository
            .Setup(r => r.ListPageByIdAsync(It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(e => e.EnvironmentName).Returns("Development");
        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        services.AddSingleton(Mock.Of<IOfficeProjectReconciler>());
        var service = new OfficeProjectReconciliationStartupService(
            services.BuildServiceProvider(), hostEnvironment.Object,
            NullLogger<OfficeProjectReconciliationStartupService>.Instance, startupDelay: TimeSpan.Zero, retryDelay: TimeSpan.Zero);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task StartAsync_InTestEnvironment_DoesNothing()
    {
        var (service, reconciler) = Build(new[] { TenantWith(TenantStatus.Active) }, environment: "Test");

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        reconciler.Verify(r => r.ReconcileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static OfficeProjectReconciliationStartupService BuildForRetries(
        Mock<ITenantRepository> repository, Mock<IOfficeProjectReconciler> reconciler)
    {
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(e => e.EnvironmentName).Returns("Development");
        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        services.AddSingleton(reconciler.Object);
        return new OfficeProjectReconciliationStartupService(
            services.BuildServiceProvider(), hostEnvironment.Object,
            NullLogger<OfficeProjectReconciliationStartupService>.Instance,
            startupDelay: TimeSpan.Zero, retryDelay: TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_TenantFailsOnce_IsRetriedInALaterAttempt()
    {
        var tenant = TenantWith(TenantStatus.Active);
        var repository = new Mock<ITenantRepository>();
        SetupPages(repository, new[] { tenant });
        repository.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var reconciler = new Mock<IOfficeProjectReconciler>();
        reconciler.SetupSequence(r => r.ReconcileAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"))
            .ReturnsAsync(OfficeProjectReconcileOutcome.Created);
        var service = BuildForRetries(repository, reconciler);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        reconciler.Verify(r => r.ReconcileAsync(tenant.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_TenantListFailsOnce_RunIsRetried()
    {
        var tenant = TenantWith(TenantStatus.Active);
        var repository = new Mock<ITenantRepository>();
        var listCalls = 0;
        repository.Setup(r => r.ListPageByIdAsync(It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid? afterId, int take, CancellationToken _) =>
            {
                if (listCalls++ == 0)
                    throw new InvalidOperationException("database unavailable");
                return Page(new[] { tenant }, afterId, take);
            });
        var reconciler = new Mock<IOfficeProjectReconciler>();
        var service = BuildForRetries(repository, reconciler);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        reconciler.Verify(r => r.ReconcileAsync(tenant.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_TenantAlwaysFails_StopsAfterMaxAttempts()
    {
        var tenant = TenantWith(TenantStatus.Active);
        var repository = new Mock<ITenantRepository>();
        SetupPages(repository, new[] { tenant });
        repository.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var reconciler = new Mock<IOfficeProjectReconciler>();
        reconciler.Setup(r => r.ReconcileAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("permanent"));
        var service = BuildForRetries(repository, reconciler);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        reconciler.Verify(r => r.ReconcileAsync(tenant.Id, It.IsAny<CancellationToken>()),
            Times.Exactly(OfficeProjectReconciliationStartupService.MaxAttempts));
    }
}
