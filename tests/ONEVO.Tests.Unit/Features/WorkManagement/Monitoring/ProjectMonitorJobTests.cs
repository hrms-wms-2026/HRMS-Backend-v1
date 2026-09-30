using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Monitoring;

public class ProjectMonitorJobTests
{
    private static ApplicationDbContext MakeDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var clock = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    private static ITenantRepository AnyTenantRepository()
    {
        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Tenant { Id = id, Name = "Test", Slug = "test", Status = TenantStatus.Active });
        return tenants.Object;
    }

    [Fact]
    public async Task RunOnce_EvaluatesEveryActiveProject_PerTenant_AndKeepsGoingWhenOneTenantFails()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var a1 = new Project { Id = Guid.NewGuid(), TenantId = tenantA };
        var a2 = new Project { Id = Guid.NewGuid(), TenantId = tenantA };
        var b1 = new Project { Id = Guid.NewGuid(), TenantId = tenantB };

        var projects = new Mock<IProjectRepository>();
        projects.Setup(p => p.ListActiveAcrossTenantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([a1, a2, b1]);

        var monitor = new Mock<IProjectMonitorService>();
        monitor.Setup(m => m.EvaluateProjectAsync(tenantA, a1.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad data"));

        await using var db = MakeDb();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(projects.Object);
        services.AddSingleton(monitor.Object);
        services.AddSingleton<IWritableTenantContext>(new TenantContextAccessor());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        services.AddSingleton(AnyTenantRepository());

        var job = new ProjectMonitorJob(services.BuildServiceProvider(), NullLogger<ProjectMonitorJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        monitor.Verify(m => m.EvaluateProjectAsync(tenantA, a1.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        monitor.Verify(m => m.EvaluateProjectAsync(tenantB, b1.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
