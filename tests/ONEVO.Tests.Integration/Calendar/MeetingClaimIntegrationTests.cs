using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;

namespace ONEVO.Tests.Integration.Calendar;

/// <summary>Uses 0A's CalendarNotificationDb harness (real PostgreSQL, one database per test).</summary>
public sealed class MeetingClaimIntegrationTests
{
    private static CalendarEventMeeting NewRow(Guid tenantId, Guid eventId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = eventId, ExternalCalendarConnectionId = Guid.NewGuid(),
        Provider = CalendarEventMeetingProviders.MicrosoftTeams, Status = CalendarEventMeetingStatuses.Provisioning,
        LinkGeneration = 1, ProvisioningStartedAt = now, CreatedAt = now
    };

    private static EfCalendarEventMeetingRepository Repo(ApplicationDbContext db) => new(db, new SystemDateTimeProvider());

    [Fact]
    public async Task TwoConcurrentFirstClaims_ExactlyOneWins()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var eventId = (await h.AddEventAsync([], [])).Id;

        var now = DateTimeOffset.UtcNow;
        async Task<bool> ClaimAsync()
        {
            await using var db = h.Context();
            return await Repo(db).TryInsertProvisioningAsync(NewRow(h.TenantId, eventId, now));
        }

        var results = await Task.WhenAll(ClaimAsync(), ClaimAsync());

        Assert.Single(results, r => r);
        await using var verify = h.Context();
        Assert.Equal(1, await verify.CalendarEventMeetings.CountAsync(m => m.CalendarEventId == eventId));
    }

    [Fact]
    public async Task NewGeneration_OnlyFromFailedOrCancelled_ConcurrentClaimsHaveOneWinner()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var evt = await h.AddEventAsync([], []);
        CalendarEventMeeting row;
        await using (var db = h.Context())
        {
            row = NewRow(h.TenantId, evt.Id, now);
            row.Status = CalendarEventMeetingStatuses.Failed;
            row.ExternalMeetingId = "";
            row.JoinUrl = "";
            db.CalendarEventMeetings.Add(row);
            await db.SaveChangesAsync();
        }

        async Task<int?> ClaimAsync()
        {
            await using var db = h.Context();
            return await Repo(db).TryClaimNewGenerationAsync(h.TenantId, row.Id, Guid.NewGuid(), CalendarEventMeetingProviders.Zoom, now);
        }

        var results = await Task.WhenAll(ClaimAsync(), ClaimAsync());

        Assert.Single(results, r => r == 2);
        Assert.Single(results, r => r is null);
        await using var verify = h.Context();
        var stored = await verify.CalendarEventMeetings.AsNoTracking().SingleAsync(m => m.Id == row.Id);
        Assert.Equal(CalendarEventMeetingStatuses.Provisioning, stored.Status);
        Assert.Equal(2, stored.LinkGeneration);
        Assert.Equal(CalendarEventMeetingProviders.Zoom, stored.Provider);
    }

    [Fact]
    public async Task StaleProvisioning_BecomesUnknown_FreshProvisioningDoesNot()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var stale = NewRow(h.TenantId, (await h.AddEventAsync([], [])).Id, now.AddMinutes(-6));
        var fresh = NewRow(h.TenantId, (await h.AddEventAsync([], [])).Id, now.AddMinutes(-1));
        await using (var db = h.Context())
        {
            foreach (var row in new[] { stale, fresh }) { row.ExternalMeetingId = ""; row.JoinUrl = ""; }
            db.CalendarEventMeetings.AddRange(stale, fresh);
            await db.SaveChangesAsync();
        }

        await using var db2 = h.Context();
        var repo = Repo(db2);
        Assert.True(await repo.TryMarkStaleProvisioningUnknownAsync(h.TenantId, stale.Id, now.AddMinutes(-5), now));
        Assert.False(await repo.TryMarkStaleProvisioningUnknownAsync(h.TenantId, fresh.Id, now.AddMinutes(-5), now));
        Assert.True(await repo.TryClaimUnknownAsync(h.TenantId, stale.Id, now));
        Assert.False(await repo.TryClaimUnknownAsync(h.TenantId, stale.Id, now)); // already claimed
    }
}
