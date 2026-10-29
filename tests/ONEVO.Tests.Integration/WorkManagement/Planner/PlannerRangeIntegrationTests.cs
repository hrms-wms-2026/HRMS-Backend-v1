using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.WorkManagement.Meetings;
using Xunit;
using EfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.WorkManagement.Planner;

/// <summary>Planner = Calendar (2026-10-27 design, section 5.2): GET /planner/me/range for the calendar grid.</summary>
public sealed class PlannerRangeIntegrationTests
{
    private const string Colombo = "Asia/Colombo";
    // 2026-11-02 01:30 in Colombo, still 2026-11-01 in UTC.
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
        var task = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: Today, assigneeEmployeeId: at.OrganizerEmployeeId);
        task.Title = "Write the report";
        await db.SaveChangesAsync();
        return new World(h, at, at.OrganizerEmployeeId, task.Id);
    }

    private static PlannerService Service(ApplicationDbContext db, bool enabled = true)
        => new(new EfPlannerRepository(db), new EfMeetingRepository(db),
            new CalendarTimezoneResolver(new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db), new EfLegalEntityRepository(db)),
            new EfEmployeeRepository(db), new UnitOfWork(db), new MeetingTestClock(Now),
            Options.Create(new PlannerOptions { Enabled = enabled }));

    private static async Task PlanAsync(World w, DateTimeOffset start, DateTimeOffset end)
    {
        await using var db = w.H.NewContext();
        var result = await Service(db).CreateBlockAsync(w.H.TenantId, w.Employee, new CreatePlannedBlockRequest(w.Task, start, end), default);
        Assert.True(result.IsSuccess, result.Error);
    }

    private static async Task<Result<PlannerRangeResponse>> RangeAsync(World w, DateOnly from, DateOnly to, bool enabled = true)
    {
        await using var db = w.H.NewContext();
        return await Service(db, enabled).GetRangeAsync(w.H.TenantId, w.Employee, from, to, default);
    }

    [Fact]
    public async Task A_week_returns_seven_days_with_their_blocks_minutes_and_edit_flags()
    {
        var w = await SeedAsync();
        await PlanAsync(w, Local(9), Local(11));
        await PlanAsync(w, Local(14, dayOffset: 1), Local(15, dayOffset: 1));

        var result = await RangeAsync(w, Today.AddDays(-1), Today.AddDays(5));

        Assert.True(result.IsSuccess, result.Error);
        var r = result.Value!;
        Assert.Equal((Colombo, Today, 7), (r.Timezone, r.Today, r.Days.Count));
        Assert.Equal(Today.AddDays(-1), r.Days[0].Date);
        Assert.False(r.Days[0].Editable);
        Assert.True(r.Days[1].Editable);
        Assert.Equal((120, 60, 0), (r.Days[1].BlockMinutes, r.Days[2].BlockMinutes, r.Days[3].BlockMinutes));
        var block = Assert.Single(r.Days[1].Blocks);
        Assert.Equal((w.Task, w.At.ProjectId, "Write the report", 120, true),
            (block.TaskId, block.ProjectId, block.TaskTitle, block.PlannedMinutes, block.StillAssigned));
    }

    [Fact]
    public async Task TheRange_NamesEachBlocksProject()
    {
        var w = await SeedAsync();
        await PlanAsync(w, Local(9), Local(10));
        string projectName;
        await using (var db = w.H.NewContext())
            projectName = (await db.Projects.AsNoTracking().SingleAsync(p => p.Id == w.At.ProjectId)).Name;

        var block = Assert.Single((await RangeAsync(w, Today, Today)).Value!.Days[0].Blocks);

        Assert.False(string.IsNullOrEmpty(projectName));
        Assert.Equal(projectName, block.ProjectName);
    }

    [Fact]
    public async Task TheRange_AndTheQueue_CarryTheCategoryKind()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var task = await db.WorkTasks.SingleAsync(t => t.Id == w.Task);
            var category = await db.TaskCategories.SingleAsync(c => c.Id == task.CategoryId);
            category.Name = "Defects";
            category.Kind = TaskCategoryKinds.Bug;
            await db.SaveChangesAsync();
        }
        await PlanAsync(w, Local(9), Local(10));

        var block = Assert.Single((await RangeAsync(w, Today, Today)).Value!.Days[0].Blocks);
        await using var read = w.H.NewContext();
        var focus = (await Service(read).GetFocusAsync(w.H.TenantId, w.Employee, default)).Value!;
        var item = focus.Groups.SelectMany(g => g.Items).Single(i => i.TaskId == w.Task);

        Assert.Equal(("Defects", TaskCategoryKinds.Bug), (block.CategoryName, block.CategoryKind));
        Assert.Equal(("Defects", TaskCategoryKinds.Bug), (item.CategoryName, item.CategoryKind));
        Assert.NotEqual(default, item.CreatedAt);
    }

    [Fact]
    public async Task Work_window_comes_from_the_employees_legal_entity()
    {
        var w = await SeedAsync();

        var day = (await RangeAsync(w, Today, Today)).Value!.Days.Single();

        // MyTeamDb's legal entity: 09:00-17:00, 60 minutes break, every day a working day.
        Assert.Equal(new PlannerWorkWindow(new TimeOnly(9, 0), new TimeOnly(17, 0), 60, new TimeOnly(12, 30), true, 420), day.WorkWindow);
        Assert.Equal(420, day.WorkingMinutes);
    }

    [Fact]
    public async Task No_schedule_means_a_null_window_and_zero_working_minutes()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var entity = await db.LegalEntities.SingleAsync(e => e.Id == w.H.LegalEntityId);
            entity.WorkStartTime = null;
            entity.WorkEndTime = null;
            await db.SaveChangesAsync();
        }

        var day = (await RangeAsync(w, Today, Today)).Value!.Days.Single();

        Assert.Equal(((PlannerWorkWindow?)null, 0), (day.WorkWindow, day.WorkingMinutes));
    }

    [Fact]
    public async Task Days_after_the_horizon_are_returned_read_only()
    {
        var w = await SeedAsync();

        var days = (await RangeAsync(w, Today, Today.AddDays(41))).Value!.Days;

        Assert.Equal(42, days.Count);
        Assert.True(days[28].Editable);
        Assert.False(days[29].Editable);
    }

    [Fact]
    public async Task A_range_over_42_days_or_backwards_is_400_invalid_range()
    {
        var w = await SeedAsync();

        var tooLong = await RangeAsync(w, Today, Today.AddDays(42));
        var backwards = await RangeAsync(w, Today, Today.AddDays(-1));

        Assert.Equal((400, PlannerErrorCodes.InvalidRange), (tooLong.StatusCode, tooLong.ErrorCode));
        Assert.Equal((400, PlannerErrorCodes.InvalidRange), (backwards.StatusCode, backwards.ErrorCode));
    }

    [Fact]
    public async Task Planner_disabled_is_503()
    {
        var w = await SeedAsync();

        var result = await RangeAsync(w, Today, Today, enabled: false);

        Assert.Equal((503, PlannerErrorCodes.Disabled), (result.StatusCode, result.ErrorCode));
    }
}
