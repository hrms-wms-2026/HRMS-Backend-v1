using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>
/// Spec 5.1 leak coverage / D11: a meeting task never surfaces through the ordinary task reads - module and project
/// lists, my tasks, task counts and progress metrics, lookups by id - even when it is assigned to the reader. (Meeting
/// reads themselves - one 404 body, outsiders with every role - are covered by MeetingAccess/MeetingRead tests;
/// calendar, monitoring and external push are Plans C/D.)
/// </summary>
public sealed class MeetingLeakIntegrationTests
{
    private sealed record World(MyTeamDb H, MeetingPlacement At, Guid Standard, Guid Meeting);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var standard = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            assigneeEmployeeId: at.OrganizerEmployeeId);
        var (meeting, _) = h.AddOccurrence(db, h.AddSeries(db, at), at);
        meeting.DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        db.TaskAssignments.Add(new TaskAssignment
        {
            Id = Guid.NewGuid(), TaskId = meeting.Id, EmployeeId = at.OrganizerEmployeeId, UserId = Guid.NewGuid(),
            AssignedById = at.OrganizerEmployeeId, AssignedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return new World(h, at, standard.Id, meeting.Id);
    }

    [Fact]
    public async Task ModuleAndProjectLists_NeverContainTheMeeting()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewTenantContext();
        var tasks = new EfWorkTaskRepository(db);

        Assert.Equal([w.Standard], (await tasks.GetByObjectiveIdAsync(w.H.TenantId, w.At.ObjectiveId)).Select(t => t.Id));
        Assert.Equal([w.Standard], (await tasks.GetByProjectAsync(w.H.TenantId, w.At.ProjectId)).Select(t => t.Id));
    }

    [Fact]
    public async Task MyTasks_NeverContainTheMeeting_EvenWhenItIsAssignedToMe()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewTenantContext();
        var mine = await new EfWorkTaskRepository(db).GetMyActiveTasksAsync(w.H.TenantId, w.At.OrganizerEmployeeId,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));

        Assert.Contains(mine, r => r.Id == w.Standard);
        Assert.DoesNotContain(mine, r => r.Id == w.Meeting);
    }

    [Fact]
    public async Task CountsAndProgressMetrics_ExcludeTheMeeting()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewTenantContext();
        var tasks = new EfWorkTaskRepository(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(1, (await tasks.CountByObjectivesAsync(w.H.TenantId, [w.At.ObjectiveId], today))[w.At.ObjectiveId].Total);
        Assert.Equal(1, (await tasks.CountByProjectsAsync(w.H.TenantId, [w.At.ProjectId], today))[w.At.ProjectId].Total);
        Assert.Single(await tasks.ListTopLevelProgressRowsAsync(w.H.TenantId, [w.At.ObjectiveId]));
        Assert.Single(await tasks.ListTopLevelOverdueAsync(w.H.TenantId, [w.At.ObjectiveId], today, take: 10));
    }

    [Fact]
    public async Task ATaskLookupById_IsNotFound_ForAMeeting()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewTenantContext();
        var tasks = new EfWorkTaskRepository(db);

        Assert.Null(await tasks.GetByIdForTenantAsync(w.H.TenantId, w.Meeting));
        Assert.Null(await tasks.GetTrackedByIdForTenantAsync(w.H.TenantId, w.Meeting));
    }
}
