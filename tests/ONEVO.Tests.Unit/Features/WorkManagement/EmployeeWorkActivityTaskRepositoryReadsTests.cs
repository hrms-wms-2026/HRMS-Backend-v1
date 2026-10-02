using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using WmTaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EmployeeWorkActivityTaskRepositoryReadsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();
    private readonly Guid _projectId = Guid.NewGuid();

    [Fact]
    public async Task ListOpenDueBy_ReturnsOnlyOpenAssignedTasksDueOnOrBeforeTheCutoff_WithProjectAndStatus()
    {
        await using var db = BuildInMemoryDb();
        var (open, done) = SeedStatuses(db);
        var due = NewTask(open.Id, "WEB-1", due: new DateOnly(2026, 9, 10));
        var tooLate = NewTask(open.Id, "WEB-2", due: new DateOnly(2026, 9, 20));
        var noDue = NewTask(open.Id, "WEB-3", due: null);
        var completedByStatus = NewTask(done.Id, "WEB-4", due: new DateOnly(2026, 9, 1));
        var completedByProgress = NewTask(open.Id, "WEB-5", due: new DateOnly(2026, 9, 1), progress: 100);
        var someoneElses = NewTask(open.Id, "WEB-6", due: new DateOnly(2026, 9, 1));
        db.WorkTasks.AddRange(due, tooLate, noDue, completedByStatus, completedByProgress, someoneElses);
        db.TaskAssignments.AddRange(Assign(due.Id), Assign(tooLate.Id), Assign(noDue.Id), Assign(completedByStatus.Id),
            Assign(completedByProgress.Id), Assign(someoneElses.Id, _otherEmployeeId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfWorkTaskRepository(db).ListOpenDueByAsync(_tenantId, _employeeId, new DateOnly(2026, 9, 15));

        var row = Assert.Single(rows);
        Assert.Equal("WEB-1", row.ShortId);
        Assert.Equal("Website", row.ProjectName);
        Assert.Equal("In Progress", row.StatusName);
        Assert.Equal("#2563EB", row.StatusColor);
    }

    [Fact]
    public async Task ListRecentlyChangedAssigned_OrdersByUpdatedThenCreated_AndTakes()
    {
        // AuditableEntityInterceptor stamps CreatedAt/UpdatedAt from IDateTimeProvider.UtcNow on
        // every Added/Modified save, overwriting whatever the entity was constructed with - so
        // timestamps here are controlled through a sequenced clock, one value per SaveChanges call.
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupSequence(c => c.UtcNow)
            .Returns(DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"))  // old created
            .Returns(DateTimeOffset.Parse("2026-08-01T00:00:00+00:00"))  // edited created
            .Returns(DateTimeOffset.Parse("2026-09-10T00:00:00+00:00"))  // fresh created
            .Returns(DateTimeOffset.Parse("2026-09-20T00:00:00+00:00")); // edited modified
        await using var db = BuildInMemoryDb(clock);
        var (open, _) = SeedStatuses(db);
        var old = NewTask(open.Id, "WEB-1");
        var edited = NewTask(open.Id, "WEB-2");
        var fresh = NewTask(open.Id, "WEB-3");

        db.WorkTasks.Add(old);
        db.TaskAssignments.Add(Assign(old.Id));
        await db.SaveChangesAsync();
        db.WorkTasks.Add(edited);
        db.TaskAssignments.Add(Assign(edited.Id));
        await db.SaveChangesAsync();
        db.WorkTasks.Add(fresh);
        db.TaskAssignments.Add(Assign(fresh.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var tracked = await db.WorkTasks.SingleAsync(t => t.Id == edited.Id);
        tracked.Title = "Edited title";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfWorkTaskRepository(db).ListRecentlyChangedAssignedAsync(_tenantId, _employeeId, take: 2);

        Assert.Equal(new[] { "WEB-2", "WEB-3" }, rows.Select(r => r.ShortId));
    }

    [Fact]
    public async Task ListCompletedAt_ReturnsCompletionInstantsOfCompletedAssignedTasksInTheWindow()
    {
        await using var db = BuildInMemoryDb();
        var (open, done) = SeedStatuses(db);
        var inWindow = NewTask(done.Id, "WEB-1", completedAt: "2026-09-05T10:00:00+00:00");
        var byProgress = NewTask(open.Id, "WEB-2", progress: 100, completedAt: "2026-09-06T10:00:00+00:00");
        var outside = NewTask(done.Id, "WEB-3", completedAt: "2026-08-31T23:59:00+00:00");
        var notCompleted = NewTask(open.Id, "WEB-4", completedAt: "2026-09-07T10:00:00+00:00");
        db.WorkTasks.AddRange(inWindow, byProgress, outside, notCompleted);
        db.TaskAssignments.AddRange(Assign(inWindow.Id), Assign(byProgress.Id), Assign(outside.Id), Assign(notCompleted.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfWorkTaskRepository(db).ListCompletedAtForEmployeeAsync(
            _tenantId, _employeeId,
            DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"), DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"));

        Assert.Equal(2, rows.Count);
    }

    private (WmTaskStatus Open, WmTaskStatus Done) SeedStatuses(ApplicationDbContext db)
    {
        db.Projects.Add(new Project { Id = _projectId, TenantId = _tenantId, Name = "Website", Identifier = "WEB", IsActive = true });
        var open = new WmTaskStatus { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, Name = "In Progress", Category = "active", Color = "#2563EB" };
        var done = new WmTaskStatus { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, Name = "Done", Category = "done", MarksTaskComplete = true };
        db.TaskStatuses.AddRange(open, done);
        return (open, done);
    }

    private WorkTask NewTask(
        Guid statusId, string shortId, DateOnly? due = null, int progress = 0,
        string? created = null, string? completedAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = Guid.NewGuid(),
        StatusId = statusId, CategoryId = Guid.NewGuid(), ShortId = shortId, Title = shortId,
        DueDate = due, ProgressPercent = progress,
        CreatedAt = created is null ? DateTimeOffset.Parse("2026-09-01T00:00:00+00:00") : DateTimeOffset.Parse(created),
        CompletedAt = completedAt is null ? null : DateTimeOffset.Parse(completedAt)
    };

    private TaskAssignment Assign(Guid taskId, Guid? employeeId = null) =>
        new() { Id = Guid.NewGuid(), TaskId = taskId, UserId = Guid.NewGuid(), EmployeeId = employeeId ?? _employeeId, AssignedById = Guid.NewGuid() };

    private static ApplicationDbContext BuildInMemoryDb(Mock<IDateTimeProvider>? clock = null) =>
        EmployeeWorkGraphRepositoryReadsTestsDb.Build(clock);
}
