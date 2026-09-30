using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Integration.Support;
using Xunit;

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

    private static GetLedWorkProgressQueryHandler Handler(ApplicationDbContext db, Guid tenantId, Guid userId) => new(
        new StubUser(tenantId, userId),
        new SystemDateTimeProvider(),
        new EfEmployeeRepository(db),
        new WorkLeadershipService(new EfObjectiveRepository(db), new EfProjectMemberRepository(db), NullLogger<WorkLeadershipService>.Instance),
        new EfWorkTaskRepository(db),
        new EfProjectRepository(db));

    private static async Task<(MyTeamDb Db, Guid LeadUserId, Guid MemberUserId, Guid ModuleId)> SeedAsync(int tasksPerModule)
    {
        var helper = await MyTeamDb.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var db = helper.NewContext();
        var creator = helper.AddEmployee(db);   // owns the project root - leads the whole project
        var lead = helper.AddEmployee(db);      // owns one module (and a nested one below it)
        var member = helper.AddEmployee(db);    // plain member of a sub-module only
        var (projectId, rootId) = helper.AddProject(db, creator.Id);
        var module = helper.AddModule(db, projectId, rootId, lead.Id);
        var sub = helper.AddModule(db, projectId, module, member.Id);
        var nestedOwned = helper.AddModule(db, projectId, sub, lead.Id); // nested ownership: must not double count
        helper.AddMember(db, projectId, module, lead.Id);
        helper.AddMember(db, projectId, sub, member.Id);
        var doing = helper.AddStatus(db, projectId, false);
        var done = helper.AddStatus(db, projectId, true);
        for (var i = 0; i < tasksPerModule; i++)
        {
            helper.AddTask(db, projectId, module, done, 100, today.AddDays(-3), assigneeEmployeeId: member.Id); // completed
            helper.AddTask(db, projectId, sub, doing, 20, today.AddDays(5));                                    // in progress
            helper.AddTask(db, projectId, nestedOwned, doing, 0, today.AddDays(-2), assigneeEmployeeId: member.Id); // overdue
        }
        await db.SaveChangesAsync();
        return (helper, lead.UserId, member.UserId, module);
    }

    [Fact]
    public async Task Lead_sees_rolled_up_counts_member_sees_nothing()
    {
        var (helper, leadUserId, memberUserId, moduleId) = await SeedAsync(tasksPerModule: 2);
        await using var db = helper.NewContext();

        var lead = await Handler(db, helper.TenantId, leadUserId).Handle(new GetLedWorkProgressQuery(), default);
        var member = await Handler(db, helper.TenantId, memberUserId).Handle(new GetLedWorkProgressQuery(), default);

        lead.IsSuccess.Should().BeTrue();
        var project = lead.Value!.Projects.Should().ContainSingle().Subject;
        var head = project.Modules.Should().ContainSingle("the nested owned module rolls into its owned ancestor").Subject;
        head.ObjectiveId.Should().Be(moduleId);
        head.Totals.Should().Be(new LedWorkTotals(6, 2, 2, 0, 2));
        lead.Value.Totals.Should().Be(head.Totals);
        lead.Value.OverdueTotal.Should().Be(2);
        lead.Value.OverdueTasks.Should().HaveCount(2).And.OnlyContain(t => t.DaysOverdue == 2 && t.Assignees.Count == 1);

        member.Value!.Projects.Should().BeEmpty("plain membership never makes someone a lead");
    }

    [Fact]
    public async Task Command_count_is_constant_and_within_budget()
    {
        var small = await SeedAsync(tasksPerModule: 1);
        var large = await SeedAsync(tasksPerModule: 15);

        async Task<int> CountAsync((MyTeamDb Db, Guid LeadUserId, Guid MemberUserId, Guid ModuleId) seeded)
        {
            var counter = new CountingDbCommandInterceptor();
            await using var db = seeded.Db.NewContext(counter);
            await Handler(db, seeded.Db.TenantId, seeded.LeadUserId).Handle(new GetLedWorkProgressQuery(), default);
            return counter.Count;
        }

        var smallCount = await CountAsync(small);
        var largeCount = await CountAsync(large);

        smallCount.Should().Be(largeCount);
        largeCount.Should().BeLessThanOrEqualTo(9);
    }
}
