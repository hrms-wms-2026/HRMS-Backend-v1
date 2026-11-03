using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.Support;
using Xunit;
// Distinct from ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository (used
// above) - this one implements the separate Common.RepositoryInterfaces IEmployeeRepository that
// MilestoneMembershipCoordinator depends on.
using CommonEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.MyTeam;

/// <summary>Requires Docker (Testcontainers PostgreSQL).</summary>
public sealed class LedWorkProgressIntegrationTests
{
    private sealed class StubUser(Guid tenantId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => string.Empty;
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private static WorkApprovalEligibility Eligibility(ApplicationDbContext db) => new(
        new EfWorkApprovalRequestRepository(db),
        new WorkHierarchyService(
            new EfObjectiveRepository(db),
            new MilestoneMembershipCoordinator(
                new CommonEfEmployeeRepository(db), new EfProjectMemberRepository(db), new EfObjectiveRepository(db),
                new EfProjectMemberInvitationRepository(db),
                new CallerIdentityResolver(new CommonEfEmployeeRepository(db), new ONEVO.Infrastructure.Persistence.Repositories.EfEntityAssetRepository(db)),
                new OutboxWriter(db, new NoOpEncryptionService(), new SystemDateTimeProvider()))),
        new EfObjectiveRepository(db));

    private static WorkLeadershipService WorkLeadership(ApplicationDbContext db) => new(
        new EfObjectiveRepository(db),
        new EfProjectMemberRepository(db),
        new EfWorkApprovalRequestRepository(db),
        Eligibility(db),
        NullLogger<WorkLeadershipService>.Instance);

    private static GetLedWorkProgressQueryHandler Handler(ApplicationDbContext db, Guid tenantId, Guid userId) => new(
        new StubUser(tenantId, userId),
        new SystemDateTimeProvider(),
        new EfEmployeeRepository(db),
        WorkLeadership(db),
        new EfWorkTaskRepository(db),
        new EfModuleHourSummaryRepository(db),
        new EfProjectRepository(db));

    private sealed record Seeded(
        MyTeamDb Db, Guid LeadUserId, Guid MemberUserId, Guid ModuleId, Guid SubId, Guid NestedId, Guid DoneStatusId,
        IReadOnlyList<Guid> InProgressTaskIds);

    // Hours per round: module (100 h) gets a 5 h completed task, sub (40 h) a 3 h in-progress one,
    // nestedOwned (20 h) a 4 h not-started one that is overdue.
    private static async Task<Seeded> SeedAsync(int tasksPerModule)
    {
        var helper = await MyTeamDb.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var db = helper.NewContext();
        var creator = helper.AddEmployee(db);   // owns the project root - leads the whole project
        var lead = helper.AddEmployee(db);      // owns one module (and a nested one below it)
        var member = helper.AddEmployee(db);    // plain member of a sub-module only - owns nothing
        var (projectId, rootId) = helper.AddProject(db, creator.Id);
        var module = helper.AddModule(db, projectId, rootId, lead.Id, allocatedHours: 100);
        // Owned by the creator: owning a Module makes you its lead, so the member must not own it.
        var sub = helper.AddModule(db, projectId, module, creator.Id, allocatedHours: 40);
        var nestedOwned = helper.AddModule(db, projectId, sub, lead.Id, allocatedHours: 20); // nested ownership: must not double count
        helper.AddMember(db, projectId, module, lead.Id);
        helper.AddMember(db, projectId, sub, member.Id);
        var doing = helper.AddStatus(db, projectId, false);
        var done = helper.AddStatus(db, projectId, true);
        // tasks.objective_id has no EF relationship, so save the Modules first: the summary
        // trigger reads the objectives row and writes nothing if the task lands before it.
        await db.SaveChangesAsync();
        var inProgress = new List<Guid>();
        for (var i = 0; i < tasksPerModule; i++)
        {
            helper.AddTask(db, projectId, module, done, 100, today.AddDays(-3), assigneeEmployeeId: member.Id, estimatedHours: 5); // completed
            inProgress.Add(helper.AddTask(db, projectId, sub, doing, 20, today.AddDays(5), estimatedHours: 3).Id);                 // in progress
            helper.AddTask(db, projectId, nestedOwned, doing, 0, today.AddDays(-2), assigneeEmployeeId: member.Id, estimatedHours: 4); // overdue
        }
        await db.SaveChangesAsync();
        return new Seeded(helper, lead.UserId, member.UserId, module, sub, nestedOwned, done, inProgress);
    }

    [Fact]
    public async Task Lead_sees_rolled_up_hours_member_sees_nothing()
    {
        var seeded = await SeedAsync(tasksPerModule: 2);
        var helper = seeded.Db;
        await using var db = helper.NewContext();

        var lead = await Handler(db, helper.TenantId, seeded.LeadUserId).Handle(new GetLedWorkProgressQuery(), default);
        var member = await Handler(db, helper.TenantId, seeded.MemberUserId).Handle(new GetLedWorkProgressQuery(), default);

        lead.IsSuccess.Should().BeTrue();
        var project = lead.Value!.Projects.Should().ContainSingle().Subject;
        project.Modules.Select(m => (m.ObjectiveId, m.Depth)).Should().Equal(
            new[] { (seeded.ModuleId, 0), (seeded.SubId, 1), (seeded.NestedId, 2) },
            "the nested owned module rolls into its owned ancestor and shows as a child row, not a second head");

        var head = project.Modules[0];
        head.Hours.Should().Be(new LedHourTotals(100, 10, 6, 8, 76, 0, 6, 0));
        head.AtRisk.Should().BeTrue();
        project.Modules[1].Hours.Should().Be(new LedHourTotals(40, 0, 6, 8, 26, 0, 4, 0));
        project.Modules[1].ParentObjectiveId.Should().Be(seeded.ModuleId);
        project.Modules[2].Hours.Should().Be(new LedHourTotals(20, 0, 0, 8, 12, 0, 2, 0));
        lead.Value.Hours.Should().Be(head.Hours);
        lead.Value.ModulesAtRisk.Should().Be(1);
        lead.Value.OverdueTotal.Should().Be(2);
        lead.Value.OverdueTasks.Should().HaveCount(2).And.OnlyContain(t => t.DaysOverdue == 2 && t.Assignees.Count == 1);

        member.Value!.Projects.Should().BeEmpty("plain membership never makes someone a lead");
    }

    [Fact]
    public async Task Summary_follows_task_writes_through_the_database_triggers()
    {
        var seeded = await SeedAsync(tasksPerModule: 2);
        var helper = seeded.Db;

        await using (var write = helper.NewContext())
        {
            // Finish one in-progress task and re-estimate the other.
            var first = await write.WorkTasks.FindAsync(seeded.InProgressTaskIds[0]);
            first!.StatusId = seeded.DoneStatusId;
            var second = await write.WorkTasks.FindAsync(seeded.InProgressTaskIds[1]);
            second!.EstimatedHours = 10;
            await write.SaveChangesAsync();
        }

        await using (var read = helper.NewContext())
        {
            var summary = (await new EfModuleHourSummaryRepository(read)
                .ListByObjectiveIdsAsync(helper.TenantId, new[] { seeded.SubId }))[seeded.SubId];
            summary.CompletedHours.Should().Be(3);
            summary.InProgressHours.Should().Be(10);
            summary.TaskCount.Should().Be(2);
        }

        await using (var delete = helper.NewContext())
        {
            delete.WorkTasks.Remove((await delete.WorkTasks.FindAsync(seeded.InProgressTaskIds[1]))!);
            await delete.SaveChangesAsync();
        }

        await using (var read = helper.NewContext())
        {
            var summary = (await new EfModuleHourSummaryRepository(read)
                .ListByObjectiveIdsAsync(helper.TenantId, new[] { seeded.SubId }))[seeded.SubId];
            summary.InProgressHours.Should().Be(0, "a soft-deleted task leaves the summary");
            summary.TaskCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task Command_count_is_constant_and_within_budget()
    {
        var small = await SeedAsync(tasksPerModule: 1);
        var large = await SeedAsync(tasksPerModule: 15);

        // OverdueLimit 1 puts both seeds over the limit, so both run the overdue count query; below the limit the
        // handler skips it and takes one query less.
        async Task<int> CountAsync(Seeded seeded)
        {
            var counter = new CountingDbCommandInterceptor();
            await using var db = seeded.Db.NewContext(counter);
            await Handler(db, seeded.Db.TenantId, seeded.LeadUserId).Handle(new GetLedWorkProgressQuery(OverdueLimit: 1), default);
            return counter.Count;
        }

        var smallCount = await CountAsync(small);
        var largeCount = await CountAsync(large);

        smallCount.Should().Be(largeCount);
        largeCount.Should().BeLessThanOrEqualTo(10);
    }
}
