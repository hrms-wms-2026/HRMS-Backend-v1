using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
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
        await using var db = h.NewContext();
        var lead = h.AddEmployee(db);
        var (projectId, rootId) = h.AddProject(db, lead.Id);
        var statusId = h.AddStatus(db, projectId, marksComplete: false);
        var standard = h.AddTask(db, projectId, rootId, statusId, progress: 0, due: null);
        var meeting = h.AddTask(db, projectId, rootId, statusId, progress: 0, due: null);
        meeting.TaskKind = WorkTaskKinds.Meeting;

        var otherTenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "Other", Slug = $"other-{Guid.NewGuid():N}"[..20],
            CompanySizeRange = "1-10", Status = TenantStatus.Active,
        };
        db.Tenants.Add(otherTenant);
        var otherMeeting = h.AddTask(db, projectId, rootId, statusId, progress: 0, due: null);
        otherMeeting.TenantId = otherTenant.Id;
        otherMeeting.TaskKind = WorkTaskKinds.Meeting;
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
