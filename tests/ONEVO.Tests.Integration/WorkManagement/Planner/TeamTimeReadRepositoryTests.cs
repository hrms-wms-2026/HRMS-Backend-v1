using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.WorkManagement.Meetings;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Planner;

/// <summary>My Team widgets (2026-10-29 design, section 7): batched block, session and meeting reads.</summary>
public sealed class TeamTimeReadRepositoryTests
{
    private static readonly DateTimeOffset DayStart = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayEnd = DayStart.AddDays(1);

    private static PlannedTaskBlock AddBlock(MyTeamDb h, ApplicationDbContext db, Guid employeeId, Guid taskId, int fromHour, int toHour)
    {
        var block = new PlannedTaskBlock
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employeeId, TaskId = taskId,
            StartAt = DayStart.AddHours(fromHour), EndAt = DayStart.AddHours(toHour), CreatedById = employeeId,
        };
        db.PlannedTaskBlocks.Add(block);
        return block;
    }

    private static TaskClockingSession AddSession(MyTeamDb h, ApplicationDbContext db, Guid employeeId, Guid taskId,
        DateTimeOffset clockIn, DateTimeOffset? clockOut)
    {
        var session = new TaskClockingSession
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employeeId, TaskId = taskId, ClockInAt = clockIn, ClockOutAt = clockOut,
        };
        db.TaskClockingSessions.Add(session);
        return session;
    }

    [Fact]
    public async Task Blocks_for_tasks_include_subtasks_and_skip_deleted()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid parentId, childId, otherId;
        MeetingPlacement at;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);
            var outside = h.AddModule(db, at.ProjectId, at.ObjectiveId, at.OrganizerEmployeeId);
            var parent = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: null);
            var child = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: null, parentTaskId: parent.Id);
            var other = h.AddTask(db, at.ProjectId, outside, at.StatusId, progress: 0, due: null);
            (parentId, childId, otherId) = (parent.Id, child.Id, other.Id);
            await db.SaveChangesAsync();
            AddBlock(h, db, at.OrganizerEmployeeId, parentId, 9, 10);
            AddBlock(h, db, at.OrganizerEmployeeId, childId, 10, 11);
            AddBlock(h, db, at.OrganizerEmployeeId, otherId, 11, 12);
            var deleted = AddBlock(h, db, at.OrganizerEmployeeId, parentId, 13, 14);
            deleted.IsDeleted = true;
            AddSession(h, db, at.OrganizerEmployeeId, childId, DayStart.AddHours(10), DayStart.AddHours(11));
            AddSession(h, db, at.OrganizerEmployeeId, otherId, DayStart.AddHours(11), DayStart.AddHours(12));
            await db.SaveChangesAsync();
        }

        await using var read = h.NewTenantContext();
        var repo = new EfTeamTimeReadRepository(read);
        var blocks = await repo.ListBlocksForTasksAsync(h.TenantId, [at.ObjectiveId], DayStart, DayEnd);
        var sessions = await repo.ListSessionsForTasksAsync(h.TenantId, [at.ObjectiveId], DayStart, DayEnd);

        Assert.Equal(new[] { childId, parentId }.Order(), blocks.Select(b => b.TaskId).Order());
        Assert.Equal(childId, Assert.Single(sessions).TaskId);
    }

    [Fact]
    public async Task Sessions_for_employees_include_open_session_started_before_window()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid employee, taskId;
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            employee = at.OrganizerEmployeeId;
            var task = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: null);
            taskId = task.Id;
            await db.SaveChangesAsync();
            AddSession(h, db, employee, taskId, DayStart.AddHours(-3), null);
            AddSession(h, db, employee, taskId, DayStart.AddHours(-5), DayStart.AddHours(-4));   // ended before the window
            await db.SaveChangesAsync();
        }

        await using var read = h.NewTenantContext();
        var sessions = await new EfTeamTimeReadRepository(read).ListSessionsForEmployeesAsync(h.TenantId, [employee], DayStart, DayEnd);

        var open = Assert.Single(sessions);
        Assert.Equal((employee, taskId, (DateTimeOffset?)null), (open.EmployeeId, open.TaskId, open.ClockOut));
    }

    [Fact]
    public async Task Tasks_and_timezones_follow_the_documented_rules()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid done, open, withZone, withEntityZone;
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            done = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 100, due: null).Id;
            open = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 40, due: null).Id;
            var a = h.AddEmployee(db);
            a.DisplayTimezone = "Asia/Colombo";
            withZone = a.Id;
            withEntityZone = h.AddEmployee(db).Id;
            await db.SaveChangesAsync();
        }

        await using var read = h.NewTenantContext();
        var repo = new EfTeamTimeReadRepository(read);
        var tasks = await repo.GetTasksAsync(h.TenantId, [done, open]);
        var zones = await repo.GetTimezonesAsync(h.TenantId, [withZone, withEntityZone]);

        Assert.True(tasks[done].IsComplete);
        Assert.Equal((false, 40m), (tasks[open].IsComplete, tasks[open].ProgressPercent));
        Assert.Equal("Asia/Colombo", zones[withZone]);
        Assert.Equal("UTC", zones[withEntityZone]);   // the seeded legal entity's zone
    }

    [Fact]
    public async Task Batched_meetings_match_single_employee_reader()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid organizer, attendee, decliner;
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            organizer = at.OrganizerEmployeeId;
            attendee = h.AddEmployee(db).Id;
            decliner = h.AddEmployee(db).Id;
            var series = h.AddSeries(db, at, DayStart.AddHours(9));
            h.AddOccurrence(db, series, at);
            h.AddSeriesParticipant(db, series, attendee, CalendarEventParticipantStatuses.Accepted);
            h.AddSeriesParticipant(db, series, decliner, CalendarEventParticipantStatuses.Rejected);
            var second = h.AddSeries(db, at, DayStart.AddHours(14));
            var (secondTask, _) = h.AddOccurrence(db, second, at);
            h.AddEmployeeOverride(db, secondTask.Id, attendee, MeetingAttendeeChanges.Add);
            await db.SaveChangesAsync();
        }

        await using var read = h.NewTenantContext();
        var repo = new EfMeetingRepository(read);
        var batched = await repo.ListEmployeesDayMeetingsAsync(h.TenantId, [organizer, attendee, decliner], DayStart, DayEnd);

        foreach (var id in new[] { organizer, attendee, decliner })
        {
            await using var single = h.NewTenantContext();
            var expected = await new EfMeetingRepository(single).ListEmployeeDayMeetingsAsync(h.TenantId, id, DayStart, DayEnd);
            Assert.Equal(expected, batched[id]);
        }
        Assert.Equal(2, batched[organizer].Count);
        Assert.Equal(2, batched[attendee].Count);
        Assert.True(Assert.Single(batched[decliner]).Declined);
    }

    [Fact]
    public async Task Other_tenant_rows_are_invisible()
    {
        var h = await MyTeamDb.CreateAsync();
        var other = await h.AddTenantAsync();
        Guid otherEmployee, otherObjective;
        await using (var db = other.NewContext())
        {
            var at = other.AddPlacement(db);
            otherEmployee = at.OrganizerEmployeeId;
            otherObjective = at.ObjectiveId;
            var task = other.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: null);
            await db.SaveChangesAsync();
            AddBlock(other, db, otherEmployee, task.Id, 9, 10);
            AddSession(other, db, otherEmployee, task.Id, DayStart.AddHours(9), DayStart.AddHours(10));
            await db.SaveChangesAsync();
        }

        await using var read = h.NewTenantContext();
        var repo = new EfTeamTimeReadRepository(read);

        Assert.Empty(await repo.ListBlocksForEmployeesAsync(h.TenantId, [otherEmployee], DayStart, DayEnd));
        Assert.Empty(await repo.ListSessionsForEmployeesAsync(h.TenantId, [otherEmployee], DayStart, DayEnd));
        Assert.Empty(await repo.ListBlocksForTasksAsync(h.TenantId, [otherObjective], DayStart, DayEnd));
        Assert.Empty(await repo.ListSessionsForTasksAsync(h.TenantId, [otherObjective], DayStart, DayEnd));
        Assert.Empty(await repo.GetTimezonesAsync(h.TenantId, [otherEmployee]));
    }
}
