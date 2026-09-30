using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using WmTaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EmployeeTaskPeriodRepositoryReadsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);

    [Fact]
    public async Task ListForEmployeePeriod_IncludesTasksDueCompletedOrAssignedInThePeriod_AndNothingElse()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var status = new WmTaskStatus { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Name = "s", Category = "active", MarksTaskComplete = false };
        var done = new WmTaskStatus { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Name = "d", Category = "done", MarksTaskComplete = true };
        db.TaskStatuses.AddRange(status, done);

        var dueInPeriod = Task(projectId, status.Id, "DUE", dueDate: new DateOnly(2026, 9, 10));
        var completedInPeriod = Task(projectId, done.Id, "DONE", completedAt: DateTimeOffset.Parse("2026-09-05T10:00:00+00:00"));
        var assignedInPeriod = Task(projectId, status.Id, "ASSIGNED");
        var outsideEverything = Task(projectId, status.Id, "OLD", dueDate: new DateOnly(2026, 7, 1));
        var otherEmployees = Task(projectId, status.Id, "OTHER", dueDate: new DateOnly(2026, 9, 10));
        db.WorkTasks.AddRange(dueInPeriod, completedInPeriod, assignedInPeriod, outsideEverything, otherEmployees);

        db.TaskAssignments.AddRange(
            Assign(dueInPeriod.Id, _employeeId, "2026-06-01T00:00:00+00:00"),
            Assign(completedInPeriod.Id, _employeeId, "2026-06-01T00:00:00+00:00"),
            Assign(assignedInPeriod.Id, _employeeId, "2026-09-03T00:00:00+00:00"),
            Assign(outsideEverything.Id, _employeeId, "2026-06-01T00:00:00+00:00"),
            Assign(otherEmployees.Id, _otherEmployeeId, "2026-09-03T00:00:00+00:00"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfWorkTaskRepository(db).ListForEmployeePeriodAsync(_tenantId, _employeeId, From, To);

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.DueDate == new DateOnly(2026, 9, 10));
        Assert.Contains(rows, r => r.MarksTaskComplete && r.CompletedAt is not null);
        Assert.Contains(rows, r => r.DueDate is null && !r.MarksTaskComplete);
    }

    [Fact]
    public async Task ListForEmployeePeriod_CarriesStoryPointsAndProgress()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var status = new WmTaskStatus { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Name = "s", Category = "active" };
        db.TaskStatuses.Add(status);
        var task = Task(projectId, status.Id, "P", dueDate: new DateOnly(2026, 9, 10), points: 5, progress: 40);
        db.WorkTasks.Add(task);
        db.TaskAssignments.Add(Assign(task.Id, _employeeId, "2026-06-01T00:00:00+00:00"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var row = Assert.Single(await new EfWorkTaskRepository(db).ListForEmployeePeriodAsync(_tenantId, _employeeId, From, To));

        Assert.Equal(5, row.StoryPoints);
        Assert.Equal(40, row.ProgressPercent);
    }

    private WorkTask Task(Guid projectId, Guid statusId, string shortId, DateOnly? dueDate = null,
        DateTimeOffset? completedAt = null, int? points = null, int progress = 0) =>
        new()
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, ObjectiveId = Guid.NewGuid(),
            StatusId = statusId, CategoryId = Guid.NewGuid(), ShortId = shortId, Title = shortId,
            DueDate = dueDate, CompletedAt = completedAt, StoryPoints = points, ProgressPercent = progress
        };

    private static TaskAssignment Assign(Guid taskId, Guid employeeId, string assignedAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(), TaskId = taskId, UserId = Guid.NewGuid(), EmployeeId = employeeId,
            AssignedById = Guid.NewGuid(), AssignedAt = DateTimeOffset.Parse(assignedAtUtc)
        };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var clock = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
