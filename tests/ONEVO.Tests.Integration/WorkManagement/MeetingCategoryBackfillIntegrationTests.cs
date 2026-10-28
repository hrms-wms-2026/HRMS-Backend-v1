using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.DataMigrations;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement;

/// <summary>The template database already ran the migration (on no projects). Each test seeds
/// projects without a system row and runs the frozen SQL itself.</summary>
public sealed class MeetingCategoryBackfillIntegrationTests
{
    private static async Task RunBackfillAsync(ApplicationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(MeetingCategoryBackfill.Sql);
        await tx.CommitAsync();
    }

    private static Task<List<TaskCategory>> CategoriesAsync(ApplicationDbContext db, Guid projectId) =>
        db.TaskCategories.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.ProjectId == projectId).OrderBy(c => c.DisplayOrder).ToListAsync();

    private static TaskCategory Category(MyTeamDb h, Guid projectId, string name, int order, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, ProjectId = projectId, Name = name, DisplayOrder = order,
        IsDeleted = deleted, DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
    };

    [Fact]
    public async Task ProjectWithoutMeeting_GetsOneInsertedAfterTheLastCategory()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id); // seeds "General" at 0
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        var meeting = Assert.Single(await CategoriesAsync(db, projectId), c => c.SystemKey == TaskCategorySystemKeys.Meeting);
        Assert.Equal("meeting", meeting.Name);
        Assert.Equal(1, meeting.DisplayOrder);
        Assert.False(meeting.IsDeleted);
    }

    [Fact]
    public async Task ProjectWithNoCategoriesAtAll_GetsMeetingAtZero()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        await db.SaveChangesAsync();
        await db.TaskCategories.IgnoreQueryFilters().Where(c => c.ProjectId == projectId).ExecuteDeleteAsync();

        await RunBackfillAsync(db);

        var meeting = Assert.Single(await CategoriesAsync(db, projectId));
        Assert.Equal(0, meeting.DisplayOrder);
        Assert.Equal(TaskCategorySystemKeys.Meeting, meeting.SystemKey);
    }

    [Fact]
    public async Task ExistingLiveMeeting_IsPromotedInPlace_AndItsTasksStay()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, rootId) = h.AddProject(db, lead.Id);
        var existing = Category(h, projectId, "Meeting", 3);
        db.TaskCategories.Add(existing);
        var statusId = h.AddStatus(db, projectId, marksComplete: false);
        var task = h.AddTask(db, projectId, rootId, statusId, progress: 0, due: null);
        task.CategoryId = existing.Id;
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        var categories = await CategoriesAsync(db, projectId);
        var system = Assert.Single(categories, c => c.SystemKey == TaskCategorySystemKeys.Meeting);
        Assert.Equal(existing.Id, system.Id);
        Assert.Equal("Meeting", system.Name);
        Assert.Equal(2, categories.Count);
        Assert.Equal(existing.Id, (await db.WorkTasks.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == task.Id)).CategoryId);
    }

    [Fact]
    public async Task TwoLiveCaseVariants_PromotesTheLowestDisplayOrder_LeavesTheOther()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        var later = Category(h, projectId, "meeting", 5);
        var earlier = Category(h, projectId, "MEETING", 2);
        db.TaskCategories.AddRange(later, earlier);
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        var categories = await CategoriesAsync(db, projectId);
        Assert.Equal(TaskCategorySystemKeys.Meeting, categories.Single(c => c.Id == earlier.Id).SystemKey);
        Assert.Null(categories.Single(c => c.Id == later.Id).SystemKey);
        Assert.Equal(3, categories.Count);
    }

    [Fact]
    public async Task OnlySoftDeletedExactMeeting_IsRestoredAndPromoted()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        var deleted = Category(h, projectId, "meeting", 4, deleted: true);
        db.TaskCategories.Add(deleted);
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        var restored = (await CategoriesAsync(db, projectId)).Single(c => c.Id == deleted.Id);
        Assert.False(restored.IsDeleted);
        Assert.Null(restored.DeletedAt);
        Assert.Equal(TaskCategorySystemKeys.Meeting, restored.SystemKey);
        Assert.Equal(2, (await CategoriesAsync(db, projectId)).Count);
    }

    [Fact]
    public async Task SoftDeletedExactMeetingPlusLiveVariant_PromotesTheLiveOne()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        var deleted = Category(h, projectId, "meeting", 4, deleted: true);
        var live = Category(h, projectId, "Meeting", 6);
        db.TaskCategories.AddRange(deleted, live);
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);

        var categories = await CategoriesAsync(db, projectId);
        Assert.Equal(TaskCategorySystemKeys.Meeting, categories.Single(c => c.Id == live.Id).SystemKey);
        Assert.True(categories.Single(c => c.Id == deleted.Id).IsDeleted);
    }

    [Fact]
    public async Task OfficeProject_GetsCapitalisedMeeting()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        await db.SaveChangesAsync();
        await db.Projects.IgnoreQueryFilters().Where(p => p.Id == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SystemPurpose, ProjectSystemPurposes.Office));

        await RunBackfillAsync(db);

        var meeting = (await CategoriesAsync(db, projectId)).Single(c => c.SystemKey == TaskCategorySystemKeys.Meeting);
        Assert.Equal("Meeting", meeting.Name);
    }

    [Fact]
    public async Task SecondRun_ChangesNothing()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, _) = h.AddProject(db, lead.Id);
        await db.SaveChangesAsync();

        await RunBackfillAsync(db);
        var afterFirst = (await CategoriesAsync(db, projectId)).Select(c => (c.Id, c.Name, c.SystemKey, c.IsDeleted)).ToList();
        await RunBackfillAsync(db);
        var afterSecond = (await CategoriesAsync(db, projectId)).Select(c => (c.Id, c.Name, c.SystemKey, c.IsDeleted)).ToList();

        Assert.Equal(afterFirst, afterSecond);
    }
}
