using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Delegate task planning (2026-11-11), "Task form reads — least privilege": what a planning delegate may read is
/// the principal's assigned task (or its parent) and that task's project.</summary>
public sealed class DelegateScopeRepositoryIntegrationTests
{
    [Fact]
    public async Task TaskScope_IsTheAssignedTaskAndItsParent_Only()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid principal, assigned, parent, sibling, someoneElses;
        await using (var db = h.NewContext())
        {
            var ceo = h.AddEmployee(db);
            var other = h.AddEmployee(db);
            principal = ceo.Id;
            var (projectId, root) = h.AddProject(db, ceo.Id);
            var status = h.AddStatus(db, projectId, marksComplete: false);
            var parentTask = h.AddTask(db, projectId, root, status, 0, null);
            parent = parentTask.Id;
            assigned = h.AddTask(db, projectId, root, status, 0, null, parentTaskId: parent, assigneeEmployeeId: ceo.Id).Id;
            sibling = h.AddTask(db, projectId, root, status, 0, null, parentTaskId: parent, assigneeEmployeeId: other.Id).Id;
            someoneElses = h.AddTask(db, projectId, root, status, 0, null, assigneeEmployeeId: other.Id).Id;
            await db.SaveChangesAsync();
        }

        await using var read = h.NewTenantContext();
        var scope = new EfDelegateScopeRepository(read);
        Assert.True(await scope.IsAssignedOrParentOfAssignedAsync(h.TenantId, principal, assigned));
        Assert.True(await scope.IsAssignedOrParentOfAssignedAsync(h.TenantId, principal, parent));
        Assert.False(await scope.IsAssignedOrParentOfAssignedAsync(h.TenantId, principal, sibling));
        Assert.False(await scope.IsAssignedOrParentOfAssignedAsync(h.TenantId, principal, someoneElses));
    }

    [Fact]
    public async Task ProjectScope_IsAProjectWithATaskAssignedToThePrincipal()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid principal, withTask, withoutTask;
        await using (var db = h.NewContext())
        {
            var ceo = h.AddEmployee(db);
            var other = h.AddEmployee(db);
            principal = ceo.Id;
            var (p1, r1) = h.AddProject(db, ceo.Id);
            var (p2, r2) = h.AddProject(db, ceo.Id);
            withTask = p1;
            withoutTask = p2;
            h.AddTask(db, p1, r1, h.AddStatus(db, p1, marksComplete: false), 0, null, assigneeEmployeeId: ceo.Id);
            h.AddTask(db, p2, r2, h.AddStatus(db, p2, marksComplete: false), 0, null, assigneeEmployeeId: other.Id);
            await db.SaveChangesAsync();
        }

        await using var read = h.NewTenantContext();
        var scope = new EfDelegateScopeRepository(read);
        Assert.True(await scope.HasAssignedTaskInProjectAsync(h.TenantId, principal, withTask));
        Assert.False(await scope.HasAssignedTaskInProjectAsync(h.TenantId, principal, withoutTask));
    }
}
