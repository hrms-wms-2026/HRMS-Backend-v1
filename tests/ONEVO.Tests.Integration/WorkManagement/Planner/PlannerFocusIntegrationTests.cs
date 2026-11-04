using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.WorkManagement.Meetings;
using Xunit;
using EfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.WorkManagement.Planner;

/// <summary>Planner = Calendar (2026-10-27 design, sections 4.2 and 5.3, call C4): GET /planner/me/focus.</summary>
public sealed class PlannerFocusIntegrationTests
{
    private const string Colombo = "Asia/Colombo";
    // Monday 2026-11-02 01:30 in Colombo, still 2026-11-01 in UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static DateTimeOffset Local(int hour, int minute = 0, int dayOffset = 0)
        => new DateTimeOffset(2026, 11, 2, hour, minute, 0, TimeSpan.FromHours(5.5)).AddDays(dayOffset);

    private sealed record World(MyTeamDb H, MeetingPlacement At, Guid Employee, Guid Task);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        db.Employees.Local.Single(e => e.Id == at.OrganizerEmployeeId).DisplayTimezone = Colombo;
        await db.SaveChangesAsync();
        var task = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: Today.AddDays(-2),
            assigneeEmployeeId: at.OrganizerEmployeeId, estimatedHours: 5);
        task.Title = "Payroll export fix";
        await db.SaveChangesAsync();
        return new World(h, at, at.OrganizerEmployeeId, task.Id);
    }

    private static PlannerService Service(ApplicationDbContext db, bool enabled = true, DateTimeOffset? now = null)
        => new(new EfPlannerRepository(db), new EfMeetingRepository(db),
            new CalendarTimezoneResolver(new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db), new EfLegalEntityRepository(db)),
            new EfEmployeeRepository(db), new UnitOfWork(db), new MeetingTestClock(now ?? Now),
            Options.Create(new PlannerOptions { Enabled = enabled }));

    private static async Task<Result<PlannerFocusResponse>> FocusAsync(World w, bool enabled = true, DateTimeOffset? now = null)
    {
        await using var db = w.H.NewContext();
        return await Service(db, enabled, now).GetFocusAsync(w.H.TenantId, w.Employee, default);
    }

    private static void AddSession(ApplicationDbContext db, World w, Guid employeeId, DateTimeOffset start, DateTimeOffset? end)
        => db.TaskClockingSessions.Add(new TaskClockingSession
        {
            Id = Guid.NewGuid(), TenantId = w.H.TenantId, TaskId = w.Task, EmployeeId = employeeId,
            ClockInAt = start.ToUniversalTime(), ClockOutAt = end?.ToUniversalTime(),
            DurationMinutes = end is { } e ? (int)(e - start).TotalMinutes : null, CreatedById = employeeId,
        });

    private static PlannerFocusItem Only(PlannerFocusResponse r) => r.Groups.SelectMany(g => g.Items).Single();

    [Fact]
    public async Task Focus_lists_assigned_open_tasks_with_project_and_logged_minutes_by_anyone()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var colleague = w.H.AddEmployee(db).Id;
            AddSession(db, w, w.Employee, Local(9, dayOffset: -3), Local(10, dayOffset: -3));
            AddSession(db, w, colleague, Local(11, dayOffset: -3), Local(11, 30, dayOffset: -3));
            await db.SaveChangesAsync();
        }

        var result = await FocusAsync(w);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal((Today, new DateOnly(2026, 11, 8)), (result.Value!.Today, result.Value.WeekEnd));
        var item = Only(result.Value);
        Assert.Equal((w.Task, w.At.ProjectId, "Payroll export fix", PlannerFocusReasons.Overdue),
            (item.TaskId, item.ProjectId, item.Title, item.Reason));
        Assert.False(string.IsNullOrWhiteSpace(item.ProjectName));
        Assert.Equal((300, 90, 210), (item.EstimatedMinutes, item.LoggedMinutes, item.RemainingMinutes));
    }

    [Fact]
    public async Task An_open_session_counts_up_to_now()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            AddSession(db, w, w.Employee, Now.AddMinutes(-40), null);
            await db.SaveChangesAsync();
        }

        Assert.Equal(40, Only((await FocusAsync(w)).Value!).LoggedMinutes);
        Assert.Equal(70, Only((await FocusAsync(w, now: Now.AddMinutes(30))).Value!).LoggedMinutes);
    }

    [Fact]
    public async Task Planned_blocks_from_today_on_show_on_the_item()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var service = Service(db);
            Assert.True((await service.CreateBlockAsync(w.H.TenantId, w.Employee, new CreatePlannedBlockRequest(w.Task, Local(9, dayOffset: 2), Local(11, dayOffset: 2)), default)).IsSuccess);
        }

        var item = Only((await FocusAsync(w)).Value!);

        Assert.Equal((120, (DateOnly?)Today.AddDays(2), 1, false), (item.PlannedAheadMinutes, item.FirstPlannedDate, item.PlannedDayCount, item.FullyPlanned));
    }

    [Fact]
    public async Task Done_meeting_deleted_and_unassigned_tasks_are_left_out()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            w.H.AddTask(db, w.At.ProjectId, w.At.ObjectiveId, w.At.StatusId, progress: 100, due: Today, assigneeEmployeeId: w.Employee);
            // A real meeting occurrence (a meeting task needs its meeting_details row), assigned to the employee.
            var meeting = w.H.AddOccurrence(db, w.H.AddSeries(db, w.At, Local(10).ToUniversalTime()), w.At).Task;
            db.TaskAssignments.Add(new TaskAssignment
            {
                Id = Guid.NewGuid(), TaskId = meeting.Id, EmployeeId = w.Employee, UserId = Guid.NewGuid(),
                AssignedById = w.Employee, AssignedAt = Now,
            });
            w.H.AddTask(db, w.At.ProjectId, w.At.ObjectiveId, w.At.StatusId, progress: 0, due: Today, assigneeEmployeeId: w.Employee)
                .IsDeleted = true;
            w.H.AddTask(db, w.At.ProjectId, w.At.ObjectiveId, w.At.StatusId, progress: 0, due: Today);
            await db.SaveChangesAsync();
        }

        Assert.Equal(w.Task, Only((await FocusAsync(w)).Value!).TaskId);
    }

    [Fact]
    public async Task An_unassigned_task_keeps_its_future_block_but_leaves_the_focus_list()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            Assert.True((await Service(db).CreateBlockAsync(w.H.TenantId, w.Employee,
                new CreatePlannedBlockRequest(w.Task, Local(9, dayOffset: 1), Local(10, dayOffset: 1)), default)).IsSuccess);
            db.TaskAssignments.RemoveRange(db.TaskAssignments.Where(a => a.TaskId == w.Task));
            await db.SaveChangesAsync();
        }

        var focus = (await FocusAsync(w)).Value!;
        await using var ctx = w.H.NewContext();
        var range = (await Service(ctx).GetRangeAsync(w.H.TenantId, w.Employee, Today.AddDays(1), Today.AddDays(1), default)).Value!;

        Assert.All(focus.Groups, g => Assert.Equal(0, g.Count));
        Assert.False(Assert.Single(range.Days.Single().Blocks).StillAssigned);
    }

    [Fact]
    public async Task Planner_disabled_is_503()
    {
        var w = await SeedAsync();

        var result = await FocusAsync(w, enabled: false);

        Assert.Equal((503, PlannerErrorCodes.Disabled), (result.StatusCode, result.ErrorCode));
    }
}
