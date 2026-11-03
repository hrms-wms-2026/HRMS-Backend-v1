using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Request;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.WorkManagement.Meetings;
using Xunit;
using EfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.WorkManagement.Planner;

/// <summary>Team planner Phase A (2026-11-03): one employee's days and a manager's team, read-only, for the reporting line.
/// Requires Docker.</summary>
public sealed class TeamPlannerServiceIntegrationTests
{
    private const string Colombo = "Asia/Colombo";
    // 2026-11-02 (Monday) 01:30 in Colombo.
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static DateTimeOffset Local(int hour, int minute = 0, int dayOffset = 0)
        => new DateTimeOffset(2026, 11, 2, hour, minute, 0, TimeSpan.FromHours(5.5)).AddDays(dayOffset);

    private sealed record World(MyTeamDb H, MeetingPlacement At, Guid Employee, Guid Task, Guid Manager, Guid Stranger);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        db.Employees.Local.Single(e => e.Id == at.OrganizerEmployeeId).DisplayTimezone = Colombo;
        var manager = h.AddEmployee(db, lastName: "Manager");
        manager.DisplayTimezone = Colombo;
        var stranger = h.AddEmployee(db, lastName: "Stranger");
        AddLine(h, db, manager.Id, at.OrganizerEmployeeId, 1);
        await db.SaveChangesAsync();
        var task = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: Today, assigneeEmployeeId: at.OrganizerEmployeeId);
        task.Title = "Write the report";
        await db.SaveChangesAsync();
        return new World(h, at, at.OrganizerEmployeeId, task.Id, manager.Id, stranger.Id);
    }

    private static void AddLine(MyTeamDb h, ApplicationDbContext db, Guid ancestor, Guid descendant, int depth)
        => db.EmployeeHierarchyClosures.Add(new EmployeeHierarchyClosure
        {
            TenantId = h.TenantId, AncestorEmployeeId = ancestor, DescendantEmployeeId = descendant, Depth = depth,
            SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow,
        });

    private static TeamPlannerService Service(ApplicationDbContext db, bool enabled = true)
        => new(new EfPlannerRepository(db), new EfMeetingRepository(db),
            new CalendarTimezoneResolver(new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db), new EfLegalEntityRepository(db)),
            new EfEmployeeRepository(db), new EfLeaveRequestReadRepository(db), new MeetingTestClock(Now),
            Options.Create(new PlannerOptions { Enabled = enabled }));

    private static PlannerService Planner(ApplicationDbContext db)
        => new(new EfPlannerRepository(db), new EfMeetingRepository(db),
            new CalendarTimezoneResolver(new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db), new EfLegalEntityRepository(db)),
            new EfEmployeeRepository(db), new UnitOfWork(db), new MeetingTestClock(Now), Options.Create(new PlannerOptions { Enabled = true }));

    private static async Task<Result<PlannerPersonRangeResponse>> RangeAsync(World w, Guid caller, DateOnly from, DateOnly to,
        bool callerCanReadLeave = true)
    {
        await using var db = w.H.NewContext();
        return await Service(db).GetPersonRangeAsync(w.H.TenantId, caller, w.Employee, from, to, callerCanReadLeave, default);
    }

    [Fact]
    public async Task PersonRange_Self_ReturnsBlocksMeetingsLeaveAndMinutes()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var planned = await Planner(db).CreateBlockAsync(w.H.TenantId, w.Employee,
                new CreatePlannedBlockRequest(w.Task, Local(10), Local(12)), default);
            Assert.True(planned.IsSuccess, planned.Error);
            var series = w.H.AddSeries(db, w.At, Local(15).ToUniversalTime());
            w.H.AddOccurrence(db, series, w.At);
            await db.SaveChangesAsync();
        }

        var result = await RangeAsync(w, w.Employee, Today, Today);

        Assert.True(result.IsSuccess, result.Error);
        var day = Assert.Single(result.Value!.Days);
        Assert.Equal(Colombo, result.Value.Timezone);
        Assert.Equal("Write the report", Assert.Single(day.Blocks).TaskTitle);
        Assert.Equal(120, day.BlockMinutes);
        Assert.Equal(30, day.MeetingMinutes);
        Assert.True(day.WorkingMinutes > 0);
        Assert.Equal(day.WorkingMinutes, day.CapacityMinutes);
        Assert.False(day.OnLeave);
    }

    [Fact]
    public async Task PersonRange_ReportingManager_CanRead()
    {
        var w = await SeedAsync();

        var result = await RangeAsync(w, w.Manager, Today, Today.AddDays(6));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(7, result.Value!.Days.Count);
    }

    [Fact]
    public async Task PersonRange_NotInChain_IsNotFound()
    {
        var w = await SeedAsync();

        var result = await RangeAsync(w, w.Stranger, Today, Today);

        Assert.Equal(404, result.StatusCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task PersonRange_PrivateMeeting_IsMaskedForAManager()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var series = w.H.AddSeries(db, w.At, Local(15).ToUniversalTime());
            series.Title = "1:1 with HR";
            series.IsPrivate = true;
            var (_, details) = w.H.AddOccurrence(db, series, w.At);
            details.IsPrivate = true;
            await db.SaveChangesAsync();
        }

        var own = (await RangeAsync(w, w.Employee, Today, Today)).Value!;
        var asManager = (await RangeAsync(w, w.Manager, Today, Today)).Value!;

        Assert.Equal("1:1 with HR", Assert.Single(own.Days[0].Meetings).Title);
        Assert.Equal("Private meeting", Assert.Single(asManager.Days[0].Meetings).Title);
    }

    [Fact]
    public async Task PersonRange_LeaveRules()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var type = w.H.AddLeaveType(db);
            w.H.AddApprovedLeave(db, w.Employee, type, Today.AddDays(1), Today.AddDays(1));
            var halfDay = w.H.AddApprovedLeave(db, w.Employee, type, Today.AddDays(2), Today.AddDays(2));
            halfDay.StartAt = Local(10, dayOffset: 2).ToUniversalTime();
            halfDay.EndAt = Local(14, dayOffset: 2).ToUniversalTime();
            w.H.AddApprovedLeave(db, w.Employee, type, Today.AddDays(3), Today.AddDays(3)).Status = LeaveRequestStatuses.Pending;
            await db.SaveChangesAsync();
        }

        var days = (await RangeAsync(w, w.Employee, Today.AddDays(1), Today.AddDays(3))).Value!.Days;

        Assert.True(days[0].OnLeave);
        Assert.Equal(0, days[0].CapacityMinutes);
        Assert.Equal("approved", Assert.Single(days[0].Leaves).Status);
        Assert.False(days[1].OnLeave);
        Assert.Equal(days[1].WorkingMinutes - 240, days[1].CapacityMinutes);
        Assert.False(days[2].OnLeave);
        Assert.Equal(days[2].WorkingMinutes, days[2].CapacityMinutes);
        Assert.Equal("pending", Assert.Single(days[2].Leaves).Status);
    }

    [Fact]
    public async Task PersonRange_ManagerWithoutLeaveRead_SeesApprovedLeaveAsBusyAndNoPending()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var type = w.H.AddLeaveType(db);
            w.H.AddApprovedLeave(db, w.Employee, type, Today.AddDays(1), Today.AddDays(1));
            w.H.AddApprovedLeave(db, w.Employee, type, Today.AddDays(2), Today.AddDays(2)).Status = LeaveRequestStatuses.Pending;
            await db.SaveChangesAsync();
        }

        var days = (await RangeAsync(w, w.Manager, Today.AddDays(1), Today.AddDays(2), callerCanReadLeave: false)).Value!.Days;

        Assert.False(days[0].OnLeave);
        Assert.Equal("busy", Assert.Single(days[0].Leaves).Status);
        // The seeded leave is a UTC day, so its tail runs into the next Colombo day; that tail is busy too, never pending.
        Assert.DoesNotContain(days[1].Leaves, l => l.Status == "pending");
    }

    [Fact]
    public async Task PersonRange_TooLong_IsInvalidRange()
    {
        var w = await SeedAsync();

        var result = await RangeAsync(w, w.Employee, Today, Today.AddDays(42));

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(PlannerErrorCodes.InvalidRange, result.ErrorCode);
    }
}
