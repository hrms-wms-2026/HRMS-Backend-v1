using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintLifecycleJobTests
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
    public async Task RunOnceAsync_OverdueSprint_NotifiesEveryAudienceMemberOnceWithProjectName()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var sprintId = Guid.NewGuid();
        var employee1 = Guid.NewGuid();
        var employee2 = Guid.NewGuid();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        var sprint = new Sprint
        {
            Id = sprintId, TenantId = tenantId, ProjectId = projectId, Name = "Sprint 1",
            Status = SprintStatuses.Active, EndDate = new DateOnly(2020, 1, 1), OverdueNotifiedAt = null
        };

        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(s => s.GetByStatusAsync(SprintStatuses.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync([sprint]);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(t => t.GetBySprintIdAsync(tenantId, sprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var statuses = new Mock<ITaskStatusRepository>();

        var projects = new Mock<IProjectRepository>();
        projects.Setup(p => p.GetByIdForTenantAsync(tenantId, projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = projectId, TenantId = tenantId, Name = "Project X" });

        var access = new Mock<ISprintAccessService>();
        access.Setup(a => a.GetAudienceEmployeeIdsAsync(tenantId, sprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([employee1, employee2]);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(m => m.GetActiveAssigneeAsync(tenantId, employee1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employee1, UserId = user1, TenantId = tenantId });
        membership.Setup(m => m.GetActiveAssigneeAsync(tenantId, employee2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employee2, UserId = user2, TenantId = tenantId });

        var notifications = new Mock<INotificationDispatcher>();

        await using var db = MakeDb();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(sprints.Object);
        services.AddSingleton(tasks.Object);
        services.AddSingleton(statuses.Object);
        services.AddSingleton(projects.Object);
        services.AddSingleton(access.Object);
        services.AddSingleton(membership.Object);
        services.AddSingleton(notifications.Object);
        services.AddSingleton<IWritableTenantContext>(new TenantContextAccessor());
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());
        services.AddSingleton(AnyTenantRepository());

        var job = new SprintLifecycleJob(services.BuildServiceProvider(), NullLogger<SprintLifecycleJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        notifications.Verify(n => n.SendTemplatedAsync(
            tenantId, user1, "work_sprint_overdue",
            It.Is<IReadOnlyDictionary<string, string>>(d => d["sprintName"] == "Sprint 1" && d["objectiveName"] == "Project X"),
            "sprint", sprintId, It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.SendTemplatedAsync(
            tenantId, user2, "work_sprint_overdue",
            It.Is<IReadOnlyDictionary<string, string>>(d => d["sprintName"] == "Sprint 1" && d["objectiveName"] == "Project X"),
            "sprint", sprintId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ShouldNotifyOverdue_PastEndDateWithUnfinishedTasksNotYetNotified_ReturnsTrue()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 15),
            allTasksComplete: false, alreadyNotified: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldNotifyOverdue_EndDateNotYetPassed_ReturnsFalse()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 10),
            allTasksComplete: false, alreadyNotified: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldNotifyOverdue_AllTasksComplete_ReturnsFalse()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 15),
            allTasksComplete: true, alreadyNotified: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldNotifyOverdue_AlreadyNotified_ReturnsFalse()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 20),
            allTasksComplete: false, alreadyNotified: true);

        Assert.False(result);
    }
}
