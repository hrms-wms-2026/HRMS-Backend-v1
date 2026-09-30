using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using WmTaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EmployeeWorkGraphRepositoryReadsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();

    [Fact]
    public async Task ProjectMembers_ListActiveForEmployee_ReturnsOnlyActiveRowsOfThatEmployee()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        db.ProjectMembers.AddRange(
            new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, ObjectiveId = Guid.NewGuid(), EmployeeId = _employeeId, IsActive = true },
            new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, ObjectiveId = Guid.NewGuid(), EmployeeId = _employeeId, IsActive = false },
            new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, ObjectiveId = Guid.NewGuid(), EmployeeId = _otherEmployeeId, IsActive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfProjectMemberRepository(db).ListActiveForEmployeeAsync(_tenantId, _employeeId);

        var row = Assert.Single(rows);
        Assert.Equal(_employeeId, row.EmployeeId);
    }

    [Fact]
    public async Task Objectives_ListActiveOwnedByEmployee_ExcludesInactiveAndOtherOwners()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var owned = new Objective { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Title = "Owned", OwnerId = _employeeId, IsActive = true };
        db.Objectives.AddRange(
            owned,
            new Objective { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Title = "Inactive", OwnerId = _employeeId, IsActive = false },
            new Objective { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Title = "Other", OwnerId = _otherEmployeeId, IsActive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfObjectiveRepository(db).ListActiveOwnedByEmployeeAsync(_tenantId, _employeeId);

        Assert.Equal(owned.Id, Assert.Single(rows).Id);
    }

    [Fact]
    public async Task Projects_GetActiveByIds_ReturnsOnlyRequestedActiveProjects()
    {
        await using var db = BuildInMemoryDb();
        var active = new Project { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Active", Identifier = "ACT", IsActive = true };
        var inactive = new Project { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Inactive", Identifier = "OFF", IsActive = false };
        var notRequested = new Project { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Other", Identifier = "OTH", IsActive = true };
        db.Projects.AddRange(active, inactive, notRequested);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfProjectRepository(db)
            .GetActiveByIdsForTenantAsync(_tenantId, new[] { active.Id, inactive.Id });

        Assert.Equal(active.Id, Assert.Single(rows).Id);
    }

    [Fact]
    public async Task WorkTasks_ListOpenAssigned_ReturnsNotStartedAndActiveOnly_ActiveFirst_WithTotalAndTake()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var objectiveId = Guid.NewGuid();
        var notStarted = NewStatus(projectId, "not_started");
        var active = NewStatus(projectId, "active");
        var done = NewStatus(projectId, "done");
        db.TaskStatuses.AddRange(notStarted, active, done);
        var t1 = NewTask(projectId, objectiveId, notStarted.Id, "WEB-1");
        var t2 = NewTask(projectId, objectiveId, active.Id, "WEB-2");
        var t3 = NewTask(projectId, objectiveId, done.Id, "WEB-3");
        var t4 = NewTask(projectId, objectiveId, active.Id, "WEB-4");
        db.WorkTasks.AddRange(t1, t2, t3, t4);
        db.TaskAssignments.AddRange(Assign(t1.Id), Assign(t2.Id), Assign(t3.Id));
        db.TaskAssignments.Add(new TaskAssignment { Id = Guid.NewGuid(), TaskId = t4.Id, UserId = Guid.NewGuid(), EmployeeId = _otherEmployeeId, AssignedById = Guid.NewGuid() });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var page = await new EfWorkTaskRepository(db).ListOpenAssignedToEmployeeAsync(_tenantId, _employeeId, take: 1);

        Assert.Equal(2, page.TotalCount);
        var first = Assert.Single(page.Items);
        Assert.Equal("WEB-2", first.ShortId);
        Assert.Equal("active", first.Category);
    }

    private WmTaskStatus NewStatus(Guid projectId, string category) =>
        new() { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Name = category, Category = category };

    private WorkTask NewTask(Guid projectId, Guid objectiveId, Guid statusId, string shortId) =>
        new() { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, ObjectiveId = objectiveId, StatusId = statusId, CategoryId = Guid.NewGuid(), ShortId = shortId, Title = shortId };

    private TaskAssignment Assign(Guid taskId) =>
        new() { Id = Guid.NewGuid(), TaskId = taskId, UserId = Guid.NewGuid(), EmployeeId = _employeeId, AssignedById = Guid.NewGuid() };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
