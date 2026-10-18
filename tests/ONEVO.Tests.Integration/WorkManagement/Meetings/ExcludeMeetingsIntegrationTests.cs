using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Phase 2 spec 2.2: ordinary task queries never see a meeting occurrence; a meeting-aware query ignores
/// only ExcludeMeetings and keeps tenant scoping.</summary>
public sealed class ExcludeMeetingsIntegrationTests
{
    private static async Task<(MyTeamDb H, WorkTask Standard, WorkTask Meeting, WorkTask OtherTenantsMeeting)> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        var other = await h.AddTenantAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var standard = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: null);
        var (meeting, _) = h.AddOccurrence(db, h.AddSeries(db, at), at);

        var otherAt = other.AddPlacement(db);
        var (otherMeeting, _) = other.AddOccurrence(db, other.AddSeries(db, otherAt), otherAt);
        await db.SaveChangesAsync();
        return (h, standard, meeting, otherMeeting);
    }

    [Fact]
    public async Task OrdinaryTaskQueries_NeverSeeAMeeting()
    {
        var (h, standard, meeting, _) = await SeedAsync();
        await using var db = h.NewTenantContext();

        Assert.Equal(new[] { standard.Id }, await db.WorkTasks.Select(t => t.Id).ToListAsync());
        Assert.Null(await new EfWorkTaskRepository(db).GetByIdForTenantAsync(h.TenantId, meeting.Id));
    }

    [Fact]
    public async Task IgnoringExcludeMeetings_ShowsMeetings_ButKeepsTenantScoping()
    {
        var (h, standard, meeting, otherTenantsMeeting) = await SeedAsync();
        await using var db = h.NewTenantContext();

        var ids = await db.WorkTasks.IgnoreQueryFilters([WorkTaskQueryFilters.ExcludeMeetings]).Select(t => t.Id).ToListAsync();

        Assert.Equal(new[] { standard.Id, meeting.Id }.OrderBy(x => x), ids.OrderBy(x => x));
        Assert.DoesNotContain(otherTenantsMeeting.Id, ids);
    }

    [Fact]
    public async Task IncludingDeletedLookup_StillHidesMeetings()
    {
        var (h, _, meeting, _) = await SeedAsync();
        await using (var write = h.NewContext())
        {
            var tracked = await write.WorkTasks.IgnoreQueryFilters().SingleAsync(t => t.Id == meeting.Id);
            tracked.IsDeleted = true;
            tracked.DeletedAt = DateTimeOffset.UtcNow;
            await write.SaveChangesAsync();
        }
        await using var db = h.NewTenantContext();

        Assert.Null(await new EfWorkTaskRepository(db).GetTrackedByIdForTenantIncludingDeletedAsync(h.TenantId, meeting.Id));
    }

    [Fact]
    public async Task StatusUsedOnlyByAMeetingTask_CountsAsInUse()
    {
        var (h, standard, meeting, _) = await SeedAsync();
        Guid meetingOnlyStatus;
        await using (var write = h.NewContext())
        {
            meetingOnlyStatus = h.AddStatus(write, meeting.ProjectId, marksComplete: false);
            var tracked = await write.WorkTasks.IgnoreQueryFilters().SingleAsync(t => t.Id == meeting.Id);
            tracked.StatusId = meetingOnlyStatus;
            await write.SaveChangesAsync();
        }
        await using var db = h.NewTenantContext();

        Assert.True(await new EfWorkTaskRepository(db).AnyActiveByStatusIdAsync(h.TenantId, meetingOnlyStatus));
        Assert.NotEqual(standard.StatusId, meetingOnlyStatus);
    }
}
