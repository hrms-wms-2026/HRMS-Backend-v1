using FluentAssertions;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Integration.MyTeam;

/// <summary>Real-PostgreSQL check for EfTaskClockingSessionRepository.GetForEmployeeOverlappingAsync,
/// which backs the tray Daily Summary task list.</summary>
public sealed class TaskClockingSessionDayIntegrationTests
{
    [Fact]
    public async Task Returns_only_this_employees_sessions_overlapping_the_window_with_task_status_and_project()
    {
        var helper = await MyTeamDb.CreateAsync();
        var windowStart = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = windowStart.AddDays(1);

        Guid employeeId;
        WorkTask doingTask, doneTask, deletedTask;
        string projectName;
        await using (var db = helper.NewContext())
        {
            var employee = helper.AddEmployee(db);
            var other = helper.AddEmployee(db);
            employeeId = employee.Id;
            var (projectId, rootId) = helper.AddProject(db, employee.Id);
            projectName = db.Projects.Local.Single(p => p.Id == projectId).Name;
            var doing = helper.AddStatus(db, projectId, marksComplete: false);
            var done = helper.AddStatus(db, projectId, marksComplete: true);
            doingTask = helper.AddTask(db, projectId, rootId, doing, 40, null);
            doneTask = helper.AddTask(db, projectId, rootId, done, 100, null);
            deletedTask = helper.AddTask(db, projectId, rootId, doing, 10, null);
            deletedTask.IsDeleted = true;
            deletedTask.DeletedAt = windowStart;

            void Session(WorkTask task, Guid who, DateTimeOffset start, DateTimeOffset? end) =>
                db.TaskClockingSessions.Add(new TaskClockingSession
                {
                    Id = Guid.NewGuid(), TenantId = helper.TenantId, TaskId = task.Id, EmployeeId = who,
                    ClockInAt = start, ClockOutAt = end,
                    DurationMinutes = end is null ? null : (int)(end.Value - start).TotalMinutes,
                });

            Session(doingTask, employee.Id, windowStart.AddMinutes(-30), windowStart.AddMinutes(20)); // crosses midnight
            Session(doingTask, employee.Id, windowStart.AddHours(9), null);                          // still open
            Session(doneTask, employee.Id, windowStart.AddHours(4), windowStart.AddHours(5));
            Session(doneTask, employee.Id, windowStart.AddHours(-5), windowStart.AddHours(-4));      // yesterday only
            Session(doneTask, other.Id, windowStart.AddHours(2), windowStart.AddHours(3));            // someone else
            Session(deletedTask, employee.Id, windowStart.AddHours(6), windowStart.AddHours(7));      // deleted task
            await db.SaveChangesAsync();
        }

        await using var read = helper.NewContext();
        var rows = await new EfTaskClockingSessionRepository(read)
            .GetForEmployeeOverlappingAsync(helper.TenantId, employeeId, windowStart, windowEnd);

        rows.Should().HaveCount(3);
        rows.Select(r => r.ClockInAt).Should().BeInAscendingOrder();
        rows.Where(r => r.TaskId == doingTask.Id).Should().HaveCount(2)
            .And.Contain(r => r.ClockOutAt == null);
        var doneRow = rows.Single(r => r.TaskId == doneTask.Id);
        doneRow.Title.Should().Be(doneTask.Title);
        doneRow.ShortId.Should().Be(doneTask.ShortId);
        doneRow.ProjectName.Should().Be(projectName);
        doneRow.StatusName.Should().Be("Done");
        doneRow.StatusCategory.Should().Be(TaskStatusCategories.Done);
        doneRow.StatusMarksTaskComplete.Should().BeTrue();
        doneRow.ProgressPercent.Should().Be(100);
        rows.Should().NotContain(r => r.TaskId == deletedTask.Id);
    }
}
