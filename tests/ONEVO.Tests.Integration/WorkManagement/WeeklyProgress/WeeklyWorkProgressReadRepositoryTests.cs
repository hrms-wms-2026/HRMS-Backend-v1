using ONEVO.Domain.Features.WorkManagement.Planner.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.WeeklyProgress;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.WorkManagement.Meetings;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.WeeklyProgress;

/// <summary>Weekly Work Progress (2026-10-28 design, 6.3): which of an employee's tasks weigh into a week.</summary>
public sealed class WeeklyWorkProgressReadRepositoryTests
{
    private static readonly DateOnly WeekStart = new(2026, 10, 26);
    private static readonly DateOnly WeekEnd = new(2026, 11, 1);
    private static readonly DateTimeOffset FromUtc = new(2026, 10, 25, 18, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ToUtc = new(2026, 11, 1, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lists_assigned_tasks_due_planned_or_clocked_in_the_week()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var me = at.OrganizerEmployeeId;
        var other = h.AddEmployee(db).Id;
        var done = h.AddStatus(db, at.ProjectId, marksComplete: true);

        var due = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, 0, WeekStart.AddDays(2), assigneeEmployeeId: me, estimatedHours: 5m);
        var completed = h.AddTask(db, at.ProjectId, at.ObjectiveId, done, 100, WeekStart.AddDays(1), assigneeEmployeeId: me, estimatedHours: 3m);
        var planned = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, 0, WeekEnd.AddDays(30), assigneeEmployeeId: me, estimatedHours: 2m);
        var clocked = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, 0, null, assigneeEmployeeId: me);
        var nextWeek = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, 0, WeekEnd.AddDays(1), assigneeEmployeeId: me, estimatedHours: 8m);
        var othersTask = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, 0, WeekStart, assigneeEmployeeId: other, estimatedHours: 8m);
        var (meeting, _) = h.AddOccurrence(db, h.AddSeries(db, at, FromUtc.AddDays(1)), at);
        meeting.DueDate = WeekStart;
        meeting.EstimatedHours = 1m;
        db.TaskAssignments.Add(new TaskAssignment
        {
            Id = Guid.NewGuid(), TaskId = meeting.Id, EmployeeId = me, UserId = Guid.NewGuid(), AssignedById = me,
            AssignedAt = DateTimeOffset.UtcNow,
        });
        var deleted = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, 0, WeekStart, assigneeEmployeeId: me, estimatedHours: 8m);
        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTimeOffset.UtcNow;

        db.PlannedTaskBlocks.Add(new PlannedTaskBlock
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = me, TaskId = planned.Id,
            StartAt = FromUtc.AddDays(1), EndAt = FromUtc.AddDays(1).AddHours(2), CreatedById = me,
        });
        db.TaskClockingSessions.Add(new TaskClockingSession
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = me, TaskId = clocked.Id,
            ClockInAt = ToUtc.AddHours(-3), ClockOutAt = ToUtc.AddHours(-2), DurationMinutes = 60,
        });
        await db.SaveChangesAsync();

        await using var read = h.NewContext();
        var rows = await new EfWeeklyWorkProgressReadRepository(read)
            .ListWeekTaskWeightsAsync(h.TenantId, me, WeekStart, WeekEnd, FromUtc, ToUtc, default);

        var byId = rows.ToDictionary(r => r.TaskId);
        Assert.Equal(new[] { due.Id, completed.Id, planned.Id, clocked.Id }.OrderBy(i => i), byId.Keys.OrderBy(i => i));
        Assert.Equal((5m, TaskStatusCategories.Active), (byId[due.Id].EstimatedHours, byId[due.Id].Category));
        Assert.Equal(TaskStatusCategories.Done, byId[completed.Id].Category);
        Assert.Null(byId[clocked.Id].EstimatedHours);
        Assert.DoesNotContain(nextWeek.Id, byId.Keys);
        Assert.DoesNotContain(othersTask.Id, byId.Keys);
        Assert.DoesNotContain(meeting.Id, byId.Keys);
        Assert.DoesNotContain(deleted.Id, byId.Keys);
    }
}
