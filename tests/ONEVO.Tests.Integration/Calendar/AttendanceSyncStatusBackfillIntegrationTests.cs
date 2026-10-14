using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.DataMigrations;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.Calendar;

public sealed class AttendanceSyncStatusBackfillIntegrationTests
{
    private static async Task<CalendarEventMeeting> AddMeetingAsync(ApplicationDbContext db, Guid tenantId, DateTimeOffset? syncedAt, int attendanceRows)
    {
        var evt = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Title = "Review", StartDate = DateTimeOffset.UtcNow.AddDays(-3),
            EndDate = DateTimeOffset.UtcNow.AddDays(-3).AddHours(1), SourceType = CalendarEventSourceTypes.Manual
        };
        var meeting = new CalendarEventMeeting
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = evt.Id, ExternalCalendarConnectionId = Guid.NewGuid(),
            Provider = CalendarEventMeetingProviders.MicrosoftTeams, ExternalMeetingId = $"m-{Guid.NewGuid():N}", JoinUrl = "https://teams.example/j",
            Status = CalendarEventMeetingStatuses.Active, LastAttendanceSyncedAt = syncedAt, CreatedAt = DateTimeOffset.UtcNow
        };
        db.PersonalCalendarEvents.Add(evt);
        db.CalendarEventMeetings.Add(meeting);
        for (var i = 0; i < attendanceRows; i++)
            db.CalendarEventMeetingAttendances.Add(new CalendarEventMeetingAttendance
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventMeetingId = meeting.Id,
                ExternalParticipantName = $"P{i}", JoinedAt = evt.StartDate, CreatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();
        return meeting;
    }

    [Fact]
    public async Task SyncedWithRows_IsMarkedSynced_ZeroRowsIsRequeued_NeverSyncedIsUntouched()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var withRows = await AddMeetingAsync(db, h.TenantId, DateTimeOffset.UtcNow.AddDays(-2), attendanceRows: 2);
        var zeroRows = await AddMeetingAsync(db, h.TenantId, DateTimeOffset.UtcNow.AddDays(-2), attendanceRows: 0);
        var neverSynced = await AddMeetingAsync(db, h.TenantId, syncedAt: null, attendanceRows: 0);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync(AttendanceSyncStatusBackfill.Sql);
            await tx.CommitAsync();
        }

        var rows = await db.CalendarEventMeetings.AsNoTracking().Where(m => m.TenantId == h.TenantId).ToDictionaryAsync(m => m.Id);
        Assert.Equal(CalendarAttendanceSyncStatuses.Synced, rows[withRows.Id].AttendanceSyncStatus);
        Assert.NotNull(rows[withRows.Id].LastAttendanceSyncedAt);
        Assert.Null(rows[zeroRows.Id].AttendanceSyncStatus);
        Assert.Null(rows[zeroRows.Id].LastAttendanceSyncedAt);
        Assert.Null(rows[neverSynced.Id].AttendanceSyncStatus);
        Assert.Null(rows[neverSynced.Id].LastAttendanceSyncedAt);
    }
}
