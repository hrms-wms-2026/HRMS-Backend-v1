using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

/// <summary>
/// Regression coverage for the same background-scope-defaults-to-System bug fixed in
/// ActivityDailySummaryJob/LocationRuleEvaluatorJob: SprintLifecycleJob's cross-tenant sweep over
/// sprints (FORCE RLS, admin-or-matching-tenant policy) ran with no admin/tenant context
/// established, so GetByStatusAsync silently returned zero rows and sprints never auto-advanced.
/// </summary>
public class SprintLifecycleJobAdminModeTests
{
    private static ApplicationDbContext MakeDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var clock = new Mock<IDateTimeProvider>();
        var currentUser = new Mock<ICurrentUser>();
        var publisher = new Mock<IPublisher>();
        var tenant = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenant.Object);
    }

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
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        var contextModesAtSweep = new List<TenantContextMode>();
        var writableContext = new TenantContextAccessor();

        var sprintA = new Sprint
        {
            Id = Guid.NewGuid(), TenantId = tenantA, ProjectId = Guid.NewGuid(), ObjectiveId = Guid.NewGuid(),
            Name = "Sprint A", Status = SprintStatuses.Future,
            StartDate = today.AddDays(-1), EndDate = today.AddDays(13)
        };
        var sprintB = new Sprint
        {
            Id = Guid.NewGuid(), TenantId = tenantB, ProjectId = Guid.NewGuid(), ObjectiveId = Guid.NewGuid(),
            Name = "Sprint B", Status = SprintStatuses.Future,
            StartDate = today.AddDays(-1), EndDate = today.AddDays(13)
        };

        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(s => s.GetByStatusAsync(SprintStatuses.Future, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                contextModesAtSweep.Add(writableContext.ContextMode);
                return new List<Sprint> { sprintA, sprintB };
            });
        sprints.Setup(s => s.GetByStatusAsync(SprintStatuses.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tenantSwitcher = new Mock<ITenantContextSwitcher>();
        var switchedTenantIds = new List<Guid>();
        tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Callback((TenantRegistryEntry e, CancellationToken _) => switchedTenantIds.Add(e.TenantId))
            .Returns(Task.CompletedTask);

        await using var db = MakeDb();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(sprints.Object);
        services.AddSingleton(new Mock<IWorkTaskRepository>().Object);
        services.AddSingleton(new Mock<ITaskStatusRepository>().Object);
        services.AddSingleton(new Mock<IProjectMemberRepository>().Object);
        services.AddSingleton(new Mock<IMilestoneMembershipCoordinator>().Object);
        services.AddSingleton(new Mock<INotificationDispatcher>().Object);
        services.AddSingleton(new Mock<IObjectiveRepository>().Object);
        services.AddSingleton<IWritableTenantContext>(writableContext);
        services.AddSingleton(tenantSwitcher.Object);
        services.AddSingleton(AnyTenantRepository());

        var job = new SprintLifecycleJob(services.BuildServiceProvider(), NullLogger<SprintLifecycleJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        contextModesAtSweep.Should().ContainSingle().Which.Should().Be(TenantContextMode.Admin);
        switchedTenantIds.Should().BeEquivalentTo([tenantA, tenantB]);
        sprintA.Status.Should().Be(SprintStatuses.Active);
        sprintB.Status.Should().Be(SprintStatuses.Active);
    }
}
