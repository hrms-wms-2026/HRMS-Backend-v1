using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner;
using ONEVO.Tests.Integration.Calendar;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.WorkManagement.Meetings;
using Xunit;
using EfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.WorkManagement.Planner;

/// <summary>Delegate task planning (2026-11-11), "Audit": a delegate's plan, move and remove on the principal's planner
/// each commit one audit row with the block; the principal's own writes commit none.</summary>
public sealed class PlannerDelegateAuditIntegrationTests
{
    private const string Colombo = "Asia/Colombo";
    // 2026-11-02 01:30 in Colombo: the 2026-11-02 slots below are today, so they can be planned.
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset Local(int hour) => new(2026, 11, 2, hour, 0, 0, TimeSpan.FromHours(5.5));

    private sealed record World(MyTeamDb H, Guid Principal, Employee Secretary, Guid Task);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        db.Employees.Local.Single(e => e.Id == at.OrganizerEmployeeId).DisplayTimezone = Colombo;
        var secretary = h.AddEmployee(db);
        await db.SaveChangesAsync();
        var task = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: null, assigneeEmployeeId: at.OrganizerEmployeeId);
        await db.SaveChangesAsync();
        return new World(h, at.OrganizerEmployeeId, secretary, task.Id);
    }

    private static PlannerService Service(MyTeamDb h, ApplicationDbContext db, Guid actingUserId)
    {
        var clock = new MeetingTestClock(Now);
        var employees = new EfEmployeeRepository(db);
        var audit = new MeetingDelegateAudit(new EfAuditLogRepository(db), new CalendarNotificationDb.StubCurrentUser(h.TenantId, actingUserId), clock);
        return new PlannerService(new EfPlannerRepository(db), new EfMeetingRepository(db),
            new CalendarTimezoneResolver(new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db), new EfLegalEntityRepository(db)),
            employees, new UnitOfWork(db), clock, Options.Create(new PlannerOptions { Enabled = true }), delegateAudit: audit);
    }

    private static async Task<List<AuditLog>> AuditRowsAsync(World w)
    {
        await using var db = w.H.NewContext();
        return await db.AuditLogs.AsNoTracking().Where(a => a.TenantId == w.H.TenantId && a.ResourceType == "PlannedTaskBlock")
            .OrderBy(a => a.CreatedAt).ToListAsync();
    }

    [Fact]
    public async Task ADelegatesPlanMoveAndRemove_EachCommitOneAuditRow_WithTheDelegateAsActor()
    {
        var w = await SeedAsync();
        Guid blockId;

        await using (var db = w.H.NewTenantContext())
        {
            var planned = await Service(w.H, db, w.Secretary.UserId).CreateBlockAsync(w.H.TenantId, w.Principal,
                new CreatePlannedBlockRequest(w.Task, Local(9), Local(10)), default, actingDelegateEmployeeId: w.Secretary.Id);
            Assert.True(planned.IsSuccess, planned.Error);
            blockId = Assert.Single(planned.Value!.Blocks).Id;
        }
        await using (var db = w.H.NewTenantContext())
        {
            var moved = await Service(w.H, db, w.Secretary.UserId).MoveBlockAsync(w.H.TenantId, w.Principal, blockId,
                new MovePlannedBlockRequest(Local(11), Local(12)), default, actingDelegateEmployeeId: w.Secretary.Id);
            Assert.True(moved.IsSuccess, moved.Error);
        }
        await using (var db = w.H.NewTenantContext())
        {
            var removed = await Service(w.H, db, w.Secretary.UserId).DeleteBlockAsync(w.H.TenantId, w.Principal, blockId, default,
                actingDelegateEmployeeId: w.Secretary.Id);
            Assert.True(removed.IsSuccess, removed.Error);
        }

        var rows = await AuditRowsAsync(w);
        // One row each (the fixed test clock gives them the same CreatedAt, so no order is asserted).
        Assert.Equal(new[] { "planner.block_moved_for_principal", "planner.block_planned_for_principal", "planner.block_removed_for_principal" },
            rows.Select(r => r.Action).Order(StringComparer.Ordinal));
        Assert.All(rows, r => Assert.Equal((w.Secretary.UserId, (Guid?)blockId), (r.UserId, r.ResourceId)));
        Assert.All(rows, r => Assert.Contains(w.Principal.ToString(), r.NewValuesJson));
    }

    [Fact]
    public async Task ThePrincipalsOwnWrite_CommitsNoAuditRow()
    {
        var w = await SeedAsync();
        var principalUser = w.H.NewContext().Employees.AsNoTracking().Single(e => e.Id == w.Principal).UserId;

        await using (var db = w.H.NewTenantContext())
        {
            var planned = await Service(w.H, db, principalUser).CreateBlockAsync(w.H.TenantId, w.Principal,
                new CreatePlannedBlockRequest(w.Task, Local(9), Local(10)), default);
            Assert.True(planned.IsSuccess, planned.Error);
        }

        Assert.Empty(await AuditRowsAsync(w));
    }
}
