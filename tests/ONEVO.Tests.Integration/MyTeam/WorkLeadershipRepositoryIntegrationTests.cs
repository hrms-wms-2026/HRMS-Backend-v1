using FluentAssertions;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Integration.MyTeam;

/// <summary>Requires Docker (Testcontainers PostgreSQL).</summary>
public sealed class WorkLeadershipRepositoryIntegrationTests
{
    [Fact]
    public async Task Owned_tree_membership_project_and_task_reads_are_scoped_correctly()
    {
        var helper = await MyTeamDb.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid leadId, strangerId, projectId, rootId, moduleId, subId, achievedId, otherLeProjectId, inactiveProjectId;
        Guid topOverdueTaskId;
        await using (var db = helper.NewContext())
        {
            var creator = helper.AddEmployee(db);   // project creator = root-module owner (leads the whole project)
            var lead = helper.AddEmployee(db);       // owns one module, plus a nested one below it
            var stranger = helper.AddEmployee(db);   // never owns or is a member of anything relevant here
            leadId = lead.Id; strangerId = stranger.Id;
            (projectId, rootId) = helper.AddProject(db, creator.Id);
            moduleId = helper.AddModule(db, projectId, rootId, leadId);
            subId = helper.AddModule(db, projectId, moduleId, strangerId);
            achievedId = helper.AddModule(db, projectId, rootId, leadId, achieved: true);
            helper.AddMember(db, projectId, moduleId, leadId);

            var otherLe = new ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity { Id = Guid.NewGuid(), TenantId = helper.TenantId, Name = "Other LE" };
            db.LegalEntities.Add(otherLe);
            (otherLeProjectId, _) = helper.AddProject(db, leadId, legalEntityId: otherLe.Id);
            (inactiveProjectId, _) = helper.AddProject(db, leadId, active: false);

            var doing = helper.AddStatus(db, projectId, marksComplete: false);
            var done = helper.AddStatus(db, projectId, marksComplete: true);
            var parent = helper.AddTask(db, projectId, moduleId, doing, 0, today.AddDays(-5), assigneeEmployeeId: strangerId);
            topOverdueTaskId = parent.Id;
            helper.AddTask(db, projectId, moduleId, doing, 0, today.AddDays(-9), parentTaskId: parent.Id); // subtask: excluded from top-level reads
            helper.AddTask(db, projectId, subId, done, 100, today.AddDays(-20));                          // complete: not overdue
            helper.AddTask(db, projectId, subId, doing, 50, today.AddDays(3));
            await db.SaveChangesAsync();
        }

        await using var read = helper.NewContext();
        var objectives = new EfObjectiveRepository(read);
        var members = new EfProjectMemberRepository(read);
        var projects = new EfProjectRepository(read);
        var tasks = new EfWorkTaskRepository(read);

        (await objectives.AnyActiveOwnedAsync(helper.TenantId, leadId, helper.LegalEntityId)).Should().BeTrue();
        (await objectives.ListActiveOwnedIdsAsync(helper.TenantId, leadId, helper.LegalEntityId))
            .Select(x => x.ObjectiveId)
            .Should().BeEquivalentTo(new[] { moduleId },
                "achieved modules are excluded, and the lead's root modules in the other-legal-entity and inactive projects are filtered out");
        (await objectives.ListActiveTreeForProjectsAsync(helper.TenantId, new[] { projectId }))
            .Select(r => r.Id).Should().BeEquivalentTo(new[] { rootId, moduleId, subId, achievedId });
        (await members.ListActiveMembershipObjectiveIdsAsync(helper.TenantId, leadId, new[] { projectId }))
            .Should().BeEquivalentTo(new[] { moduleId });
        (await projects.ListByIdsAsync(helper.TenantId, new[] { projectId })).Should().ContainSingle(p => p.Id == projectId);

        var rows = await tasks.ListTopLevelProgressRowsAsync(helper.TenantId, new[] { moduleId, subId });
        rows.Should().HaveCount(3, "the subtask is excluded from a top-level read");

        var overdue = await tasks.ListTopLevelOverdueAsync(helper.TenantId, new[] { moduleId, subId }, today, take: 10);
        overdue.Should().ContainSingle().Which.TaskId.Should().Be(topOverdueTaskId);
        overdue[0].AssigneeEmployeeIds.Should().Equal(strangerId);
    }
}
