using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>A meeting task's ShortId, category and placeholder status (spec 4.1; final-review I7).</summary>
public sealed class MeetingTaskDefaultsIntegrationTests
{
    [Fact]
    public async Task MeetingNumbers_AreSeparateFromTaskNumbers()
    {
        var h = await MyTeamDb.CreateAsync();
        MeetingPlacement at;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);
            await db.SaveChangesAsync();
        }

        await using var context = h.NewContext();
        var projects = new EfProjectRepository(context);

        Assert.Equal(1, await projects.IncrementAndGetNextMeetingNumberAsync(h.TenantId, at.ProjectId));
        Assert.Equal(2, await projects.IncrementAndGetNextMeetingNumberAsync(h.TenantId, at.ProjectId));
        Assert.Equal(1, await projects.IncrementAndGetNextTaskNumberAsync(h.TenantId, at.ProjectId));
    }

    [Fact]
    public async Task MissingMeetingCategory_IsCreatedOnce()
    {
        var h = await MyTeamDb.CreateAsync();
        MeetingPlacement at;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);   // MyTeamDb projects have only a "General" category
            await db.SaveChangesAsync();
        }

        Guid first, second;
        await using (var db = h.NewContext())
        {
            first = (await Defaults(db).ResolveAsync(h.TenantId, at.ProjectId, at.OrganizerEmployeeId, CancellationToken.None)).Value.CategoryId;
            await db.SaveChangesAsync();
        }
        await using (var db = h.NewContext())
        {
            second = (await Defaults(db).ResolveAsync(h.TenantId, at.ProjectId, at.OrganizerEmployeeId, CancellationToken.None)).Value.CategoryId;
            await db.SaveChangesAsync();
        }

        Assert.Equal(first, second);
        await using var check = h.NewContext();
        var meetingCategories = await check.TaskCategories
            .Where(c => c.ProjectId == at.ProjectId && c.SystemKey == TaskCategorySystemKeys.Meeting).ToListAsync();
        Assert.Single(meetingCategories);
    }

    [Fact]
    public async Task PlaceholderStatus_IsTheFirstActiveStatus()
    {
        var h = await MyTeamDb.CreateAsync();
        MeetingPlacement at;
        Guid secondActive;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);                                   // "Doing", DisplayOrder 1
            h.AddStatus(db, at.ProjectId, marksComplete: true);        // "Done"
            secondActive = h.AddStatus(db, at.ProjectId, marksComplete: false);
            (await db.TaskStatuses.FindAsync(secondActive))!.DisplayOrder = 5;
            await db.SaveChangesAsync();
        }

        await using var context = h.NewContext();
        var resolved = await Defaults(context).ResolveAsync(h.TenantId, at.ProjectId, at.OrganizerEmployeeId, CancellationToken.None);

        Assert.True(resolved.IsSuccess);
        Assert.Equal(at.StatusId, resolved.Value.StatusId);
    }

    private static MeetingTaskDefaults Defaults(ONEVO.Infrastructure.Persistence.ApplicationDbContext db)
        => new(new EfTaskCategoryRepository(db), new EfTaskStatusRepository(db));
}
