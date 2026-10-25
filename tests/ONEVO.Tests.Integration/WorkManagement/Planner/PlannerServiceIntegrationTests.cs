using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using EfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.WorkManagement.Meetings;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Planner;

/// <summary>Day planner (2026-10-26 design, Part B): today-only task blocks, planned vs actual, and the reporting-line
/// manager's read-only view.</summary>
public sealed class PlannerServiceIntegrationTests
{
    private const string Colombo = "Asia/Colombo";
    // 2026-11-02 01:30 in Colombo, still 2026-11-01 in UTC: "today" must follow the employee's zone.
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static DateTimeOffset Local(int hour, int minute = 0, int dayOffset = 0)
        => new(2026, 11, 2 + dayOffset, hour, minute, 0, TimeSpan.FromHours(5.5));

    private sealed record World(MyTeamDb H, MeetingPlacement At, Guid Employee, Guid Task);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        db.Employees.Local.Single(e => e.Id == at.OrganizerEmployeeId).DisplayTimezone = Colombo;
        await db.SaveChangesAsync();
        var task = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: Today, assigneeEmployeeId: at.OrganizerEmployeeId);
        task.Title = "Write the report";
        await db.SaveChangesAsync();
        return new World(h, at, at.OrganizerEmployeeId, task.Id);
    }

    private static PlannerService Service(ApplicationDbContext db, bool enabled = true, DateTimeOffset? now = null)
    {
        var employees = new EfEmployeeRepository(db);
        return new PlannerService(new EfPlannerRepository(db), new EfMeetingRepository(db),
            new CalendarTimezoneResolver(new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db), new EfLegalEntityRepository(db)),
            employees, new UnitOfWork(db),
            new MeetingTestClock(now ?? Now), Options.Create(new PlannerOptions { Enabled = enabled }));
    }

    private static async Task<Result<PlannerDayResponse>> PlanAsync(World w, Guid taskId, DateTimeOffset start, DateTimeOffset end)
    {
        await using var db = w.H.NewContext();
        return await Service(db).CreateBlockAsync(w.H.TenantId, w.Employee, new CreatePlannedBlockRequest(taskId, start, end), default);
    }

    private static async Task<Result<PlannerDayResponse>> DayAsync(World w, Guid caller, Guid employee, DateOnly? date = null)
    {
        await using var db = w.H.NewContext();
        return await Service(db).GetDayAsync(w.H.TenantId, caller, employee, date, default);
    }

    [Fact]
    public async Task Plan_a_task_today_and_read_it_back()
    {
        var w = await SeedAsync();

        var planned = await PlanAsync(w, w.Task, Local(9), Local(11));

        Assert.True(planned.IsSuccess, planned.Error);
        var day = (await DayAsync(w, w.Employee, w.Employee)).Value!;
        Assert.Equal((Today, Colombo, true), (day.Date, day.Timezone, day.Editable));
        var block = Assert.Single(day.Blocks);
        Assert.Equal(("Write the report", 120, true), (block.TaskTitle, block.PlannedMinutes, block.StillAssigned));
        Assert.Equal(120, day.Totals.PlannedMinutes);
        Assert.Contains(day.DueTasks, t => t.TaskId == w.Task);
    }

    [Fact]
    public async Task Today_uses_the_employee_zone()
    {
        var w = await SeedAsync();

        // 08:00 in Colombo on 2 Nov is 02:30 UTC on 2 Nov, while UTC "now" is still 1 Nov.
        Assert.True((await PlanAsync(w, w.Task, Local(8), Local(9))).IsSuccess);
    }

    [Fact]
    public async Task Yesterday_or_tomorrow_is_refused()
    {
        var w = await SeedAsync();

        foreach (var offset in new[] { -1, 1 })
        {
            var result = await PlanAsync(w, w.Task, Local(9, dayOffset: offset), Local(10, dayOffset: offset));
            Assert.Equal((400, PlannerErrorCodes.NotToday), (result.StatusCode, result.ErrorCode));
        }
    }

    [Fact]
    public async Task Overlapping_block_is_409_and_short_blocks_are_400()
    {
        var w = await SeedAsync();
        await PlanAsync(w, w.Task, Local(9), Local(10));

        var overlap = await PlanAsync(w, w.Task, Local(9, 30), Local(10, 30));
        var tooShort = await PlanAsync(w, w.Task, Local(11), Local(11, 10));

        Assert.Equal((409, PlannerErrorCodes.BlockOverlap), (overlap.StatusCode, overlap.ErrorCode));
        Assert.Equal((400, PlannerErrorCodes.InvalidTime), (tooShort.StatusCode, tooShort.ErrorCode));
    }

    [Fact]
    public async Task Meeting_or_unassigned_task_is_not_plannable()
    {
        var w = await SeedAsync();
        Guid unassigned, meeting;
        await using (var db = w.H.NewContext())
        {
            unassigned = w.H.AddTask(db, w.At.ProjectId, w.At.ObjectiveId, w.At.StatusId, progress: 0, due: null).Id;
            meeting = w.H.AddOccurrence(db, w.H.AddSeries(db, w.At, Local(14).ToUniversalTime()), w.At).Task.Id;
            await db.SaveChangesAsync();
        }

        foreach (var taskId in new[] { unassigned, meeting })
        {
            var result = await PlanAsync(w, taskId, Local(9), Local(10));
            Assert.Equal((400, PlannerErrorCodes.TaskNotPlannable), (result.StatusCode, result.ErrorCode));
        }
    }

    [Fact]
    public async Task Unassigned_task_keeps_its_block_but_cannot_be_planned_again()
    {
        var w = await SeedAsync();
        await PlanAsync(w, w.Task, Local(9), Local(10));
        await using (var db = w.H.NewContext())
        {
            db.TaskAssignments.RemoveRange(db.TaskAssignments.Where(a => a.TaskId == w.Task));
            await db.SaveChangesAsync();
        }

        var day = (await DayAsync(w, w.Employee, w.Employee)).Value!;
        var again = await PlanAsync(w, w.Task, Local(11), Local(12));

        Assert.False(Assert.Single(day.Blocks).StillAssigned);
        Assert.Equal(PlannerErrorCodes.TaskNotPlannable, again.ErrorCode);
    }

    [Fact]
    public async Task Planner_disabled_is_503()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var result = await Service(db, enabled: false).GetDayAsync(w.H.TenantId, w.Employee, w.Employee, null, default);

        Assert.Equal((503, PlannerErrorCodes.Disabled), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task Another_employees_block_is_404_on_move_and_delete()
    {
        var w = await SeedAsync();
        var blockId = (await PlanAsync(w, w.Task, Local(9), Local(10))).Value!.Blocks.Single().Id;
        Guid stranger;
        await using (var db = w.H.NewContext())
        {
            stranger = w.H.AddEmployee(db).Id;
            await db.SaveChangesAsync();
        }

        await using var ctx = w.H.NewContext();
        var service = Service(ctx);
        var move = await service.MoveBlockAsync(w.H.TenantId, stranger, blockId, new MovePlannedBlockRequest(Local(12), Local(13)), default);
        var delete = await service.DeleteBlockAsync(w.H.TenantId, stranger, blockId, default);

        Assert.Equal((404, 404), (move.StatusCode, delete.StatusCode));
    }

    [Fact]
    public async Task Actuals_come_from_clocked_sessions_and_ended_meetings()
    {
        var w = await SeedAsync();
        await PlanAsync(w, w.Task, Local(0), Local(1));
        await using (var db = w.H.NewContext())
        {
            db.TaskClockingSessions.Add(new TaskClockingSession
            {
                Id = Guid.NewGuid(), TenantId = w.H.TenantId, TaskId = w.Task, EmployeeId = w.Employee,
                ClockInAt = Local(0, 30).ToUniversalTime(), ClockOutAt = Local(1, 15).ToUniversalTime(), DurationMinutes = 45, CreatedById = w.Employee,
            });
            var series = w.H.AddSeries(db, w.At, Local(0).ToUniversalTime());
            series.DurationMinutes = 60;
            w.H.AddSeriesParticipant(db, series, w.Employee, CalendarEventParticipantStatuses.Accepted);
            w.H.AddOccurrence(db, series, w.At);
            await db.SaveChangesAsync();
        }

        await using var ctx = w.H.NewContext();
        var day = (await Service(ctx, now: Local(1, 30)).GetDayAsync(w.H.TenantId, w.Employee, w.Employee, null, default)).Value!;

        Assert.Equal(45, Assert.Single(day.Blocks).ActualMinutes);
        var meeting = Assert.Single(day.Meetings);
        Assert.Equal((60, 60), (meeting.PlannedMinutes, meeting.ActualMinutes));
        Assert.Equal((120, 105), (day.Totals.PlannedMinutes, day.Totals.ActualMinutes));
    }

    [Fact]
    public async Task A_meeting_counts_only_its_part_inside_the_day()
    {
        // 23:00 yesterday to 01:00 today (local): only the hour after midnight is today's.
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var series = w.H.AddSeries(db, w.At, Local(23, dayOffset: -1).ToUniversalTime());
            series.DurationMinutes = 120;
            w.H.AddSeriesParticipant(db, series, w.Employee, CalendarEventParticipantStatuses.Accepted);
            w.H.AddOccurrence(db, series, w.At);
            await db.SaveChangesAsync();
        }

        await using var ctx = w.H.NewContext();
        var day = (await Service(ctx, now: Local(2)).GetDayAsync(w.H.TenantId, w.Employee, w.Employee, null, default)).Value!;

        var meeting = Assert.Single(day.Meetings);
        Assert.Equal((60, 60), (meeting.PlannedMinutes, meeting.ActualMinutes));
    }

    [Fact]
    public async Task Ancestor_can_view_peer_gets_404_and_private_meetings_are_masked()
    {
        var w = await SeedAsync();
        Guid manager, skipLevel, peer;
        await using (var db = w.H.NewContext())
        {
            manager = w.H.AddEmployee(db).Id;
            skipLevel = w.H.AddEmployee(db).Id;
            peer = w.H.AddEmployee(db).Id;
            foreach (var (ancestor, depth) in new[] { (manager, 1), (skipLevel, 2) })
                db.EmployeeHierarchyClosures.Add(new EmployeeHierarchyClosure
                {
                    TenantId = w.H.TenantId, AncestorEmployeeId = ancestor, DescendantEmployeeId = w.Employee, Depth = depth,
                    SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow,
                });
            var series = w.H.AddSeries(db, w.At, Local(15).ToUniversalTime());
            series.Title = "1:1 with HR";
            series.IsPrivate = true;
            var (_, details) = w.H.AddOccurrence(db, series, w.At);
            details.IsPrivate = true;
            await db.SaveChangesAsync();
        }

        var own = (await DayAsync(w, w.Employee, w.Employee)).Value!;
        var asSkipLevel = await DayAsync(w, skipLevel, w.Employee);
        var asPeer = await DayAsync(w, peer, w.Employee);

        Assert.Equal("1:1 with HR", Assert.Single(own.Meetings).Title);
        Assert.True(asSkipLevel.IsSuccess, asSkipLevel.Error);
        Assert.False(asSkipLevel.Value!.Editable);
        Assert.Equal("Private meeting", Assert.Single(asSkipLevel.Value.Meetings).Title);
        Assert.Equal(404, asPeer.StatusCode);
    }

    [Fact]
    public async Task Past_day_is_read_only_and_future_day_is_refused()
    {
        var w = await SeedAsync();

        var past = await DayAsync(w, w.Employee, w.Employee, Today.AddDays(-1));
        var future = await DayAsync(w, w.Employee, w.Employee, Today.AddDays(1));

        Assert.False(past.Value!.Editable);
        Assert.Equal((400, PlannerErrorCodes.NotToday), (future.StatusCode, future.ErrorCode));
    }
}
