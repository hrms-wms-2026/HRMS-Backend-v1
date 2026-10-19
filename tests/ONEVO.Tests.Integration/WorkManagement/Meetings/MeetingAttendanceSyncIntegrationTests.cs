using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;
using static ONEVO.Tests.Integration.WorkManagement.Meetings.MeetingAttendanceWorld;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Plan C Task 7 (spec 5.8): one link reused by a weekly series yields one provider session per held meeting; each
/// session lands on its occurrence, records are upserted once, and each occurrence walks pending -> provisional -> synced
/// over the checkpoints (30m, 2h, 24h after it ends), or ends unavailable / sync_failed after the one 72 h retry.</summary>
public sealed class MeetingAttendanceSyncIntegrationTests
{
    [Fact]
    public async Task NewOccurrencesWithALink_StartPending_AtTheFirstCheckpoint()
    {
        var w = await SeedAsync();

        var first = await DetailsAsync(w, 0);

        Assert.Equal((MeetingAttendanceStatuses.Pending, (DateTimeOffset?)End(0).AddMinutes(30), 0),
            (first.AttendanceStatus, first.AttendanceNextCheckAt, first.AttendanceCheckpoint));
    }

    [Fact]
    public async Task ThreeWeeklySessionsOnOneLink_LandOnThreeOccurrences()
    {
        var w = await SeedAsync();
        var stub = new SessionsStub
        {
            Answer = (_, _) =>
            [
                Session("r-0", Week(0).AddMinutes(1), Rec("a", w.AttendeeEmail, Week(0).AddMinutes(1), 20)),
                Session("r-1", Week(1).AddMinutes(-5), Rec("a", w.AttendeeEmail, Week(1), 25)),
                Session("r-2", Week(2).AddMinutes(2), Rec("a", w.AttendeeEmail, Week(2).AddMinutes(2), 10)),
            ],
        };

        var outcome = await SyncAsync(w, stub, End(2).AddMinutes(30));

        Assert.Equal(MeetingAttendanceSyncOutcome.Synced, outcome);
        await using var check = w.H.NewContext();
        var sessions = await check.MeetingAttendanceSessions.AsNoTracking().ToListAsync();
        Assert.Equal(new[] { ("r-0", w.Tasks[0]), ("r-1", w.Tasks[1]), ("r-2", w.Tasks[2]) },
            sessions.OrderBy(s => s.SessionStart).Select(s => (s.ProviderSessionId, s.OccurrenceTaskId!.Value)));
        var rows = await check.CalendarEventMeetingAttendances.AsNoTracking().ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal((w.Attendee, w.LinkId), (r.EmployeeId!.Value, r.CalendarEventMeetingId)));
        Assert.Equal(w.Tasks.Order(), rows.Select(r => r.OccurrenceTaskId!.Value).Order());
        foreach (var k in new[] { 0, 1, 2 })
            Assert.Equal(MeetingAttendanceStatuses.Provisional, (await DetailsAsync(w, k)).AttendanceStatus);
    }

    [Fact]
    public async Task TwoSessionsInOneOccurrence_AreBothAssigned_AndTimeIsTheUnion()
    {
        var w = await SeedAsync();
        var stub = new SessionsStub
        {
            Answer = (_, _) =>
            [
                Session("first", Week(0), Rec("a", w.AttendeeEmail, Week(0), 10), Rec("a", w.AttendeeEmail, Week(0).AddMinutes(5), 5)),
                Session("restart", Week(0).AddMinutes(15), Rec("a2", w.AttendeeEmail.ToUpperInvariant(), Week(0).AddMinutes(15), 15)),
            ],
        };

        await SyncAsync(w, stub, End(0).AddMinutes(30));

        await using var check = w.H.NewContext();
        Assert.All(await check.MeetingAttendanceSessions.AsNoTracking().ToListAsync(), s => Assert.Equal(w.Tasks[0], s.OccurrenceTaskId));
        var rows = await check.CalendarEventMeetingAttendances.AsNoTracking()
            .Where(r => r.OccurrenceTaskId == w.Tasks[0] && r.EmployeeId == w.Attendee).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(TimeSpan.FromMinutes(25), MeetingAttendanceMatcher.Union(rows.Select(r => (r.JoinedAt, r.LeftAt)), End(0)));
    }

    [Fact]
    public async Task RepeatedCheckpoints_AddNoDuplicateRows()
    {
        var w = await SeedAsync();
        var stub = new SessionsStub { Answer = (_, _) => [Session("r-0", Week(0), Rec("a", w.AttendeeEmail, Week(0), 20))] };

        foreach (var after in new[] { TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24) })
            await SyncAsync(w, stub, End(0) + after);

        await using var check = w.H.NewContext();
        Assert.Single(await check.MeetingAttendanceSessions.ToListAsync());
        Assert.Single(await check.CalendarEventMeetingAttendances.ToListAsync());
        var settled = await DetailsAsync(w, 0);
        Assert.Equal(MeetingAttendanceStatuses.Synced, settled.AttendanceStatus);
        Assert.NotNull(settled.AttendanceFinalizedAt);
        Assert.Null(settled.AttendanceNextCheckAt);
        Assert.Contains((await check.Set<OutboxMessage>().AsNoTracking().ToListAsync()), m => m.Type == OutboxMessageTypes.CalendarIncidentEvaluate);
    }

    [Fact]
    public async Task ProviderErrorAtEveryCheckpoint_IsSyncFailed_NotNoJoin()
    {
        var w = await SeedAsync();
        var stub = new SessionsStub { Answer = (_, _) => throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, "boom") };

        var outcomes = new List<MeetingAttendanceSyncOutcome>();
        foreach (var after in new[] { TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24) })
            outcomes.Add(await SyncAsync(w, stub, End(0) + after));
        var afterLast = await DetailsAsync(w, 0);
        outcomes.Add(await SyncAsync(w, stub, afterLast.AttendanceNextCheckAt!.Value));

        Assert.All(outcomes, o => Assert.Equal(MeetingAttendanceSyncOutcome.ProviderError, o));
        Assert.Equal((MeetingAttendanceStatuses.SyncFailed, (DateTimeOffset?)End(0).AddHours(24).AddHours(72)),
            (afterLast.AttendanceStatus, afterLast.AttendanceNextCheckAt));
        var final = await DetailsAsync(w, 0);
        Assert.Equal((MeetingAttendanceStatuses.SyncFailed, CalendarAttendanceSyncErrors.ProviderError), (final.AttendanceStatus, final.AttendanceLastError));
        Assert.NotNull(final.AttendanceFinalizedAt);
        await using var check = w.H.NewContext();
        Assert.Empty(await check.CalendarEventMeetingAttendances.ToListAsync());
    }

    [Fact]
    public async Task NoSessionsAtTheLastCheckpoint_IsUnavailable_ThenTheRetryRecoversALateReport()
    {
        var w = await SeedAsync();
        var stub = new SessionsStub();

        foreach (var after in new[] { TimeSpan.FromMinutes(30), TimeSpan.FromHours(2) })
        {
            await SyncAsync(w, stub, End(0) + after);
            Assert.Equal(MeetingAttendanceStatuses.Pending, (await DetailsAsync(w, 0)).AttendanceStatus);
        }
        await SyncAsync(w, stub, End(0).AddHours(24));
        var unavailable = await DetailsAsync(w, 0);
        stub.Answer = (_, _) => [Session("late", Week(0), Rec("a", w.AttendeeEmail, Week(0), 20))];
        await SyncAsync(w, stub, unavailable.AttendanceNextCheckAt!.Value);

        Assert.Equal((MeetingAttendanceStatuses.Unavailable, (DateTimeOffset?)End(0).AddHours(96)),
            (unavailable.AttendanceStatus, unavailable.AttendanceNextCheckAt));
        var recovered = await DetailsAsync(w, 0);
        Assert.Equal(MeetingAttendanceStatuses.Synced, recovered.AttendanceStatus);
        Assert.NotNull(recovered.AttendanceFinalizedAt);
    }

    private static async Task<ONEVO.Application.Common.Models.Result<MeetingAttendanceStatusResponse>> RecheckAsync(World w, SessionsStub stub,
        DateTimeOffset at, int k = 0, Guid? caller = null)
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(at));
        return await s.AttendanceSync(stub).RecheckAsync(w.H.TenantId, caller ?? w.Organizer, w.SeriesId, Week(k), CancellationToken.None);
    }

    [Fact]
    public async Task RecheckAfterSevenDays_Is409Expired()
    {
        var w = await SeedAsync();
        var stub = new SessionsStub { Answer = (_, _) => [Session("r-0", Week(0), Rec("a", w.AttendeeEmail, Week(0), 20))] };

        var expired = await RecheckAsync(w, stub, End(0).AddDays(7).AddMinutes(1));
        var attendee = await RecheckAsync(w, stub, End(0).AddDays(1), caller: w.Attendee);
        var ok = await RecheckAsync(w, stub, End(0).AddDays(6));

        Assert.Equal((409, MeetingErrorCodes.AttendanceRecheckExpired), (expired.StatusCode, expired.ErrorCode));
        Assert.Equal((403, MeetingErrorCodes.OrganizerRequired), (attendee.StatusCode, attendee.ErrorCode));
        Assert.True(ok.IsSuccess, ok.Error);
        Assert.Equal(MeetingAttendanceStatuses.Synced, ok.Value!.Status);   // past the last checkpoint: settles
    }

    [Fact]
    public async Task StatusNeverMovesBackFromSynced()
    {
        var w = await SeedAsync();
        var stub = new SessionsStub { Answer = (_, _) => [Session("r-0", Week(0), Rec("a", w.AttendeeEmail, Week(0), 20))] };
        await RecheckAsync(w, stub, End(0).AddDays(2));

        stub.Answer = (_, _) => throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, "boom");
        var failing = await RecheckAsync(w, stub, End(0).AddDays(3));
        stub.Answer = (_, _) => [];
        var empty = await RecheckAsync(w, stub, End(0).AddDays(4));

        Assert.Equal((MeetingAttendanceStatuses.Synced, MeetingAttendanceStatuses.Synced), (failing.Value!.Status, empty.Value!.Status));
        Assert.Equal(MeetingAttendanceStatuses.Synced, (await DetailsAsync(w, 0)).AttendanceStatus);
        await using var check = w.H.NewContext();
        Assert.Single(await check.CalendarEventMeetingAttendances.ToListAsync());
    }

    [Fact]
    public async Task ReadOnlyBlocksTheRecheck()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(End(0).AddDays(1)));

        var result = await s.AttendanceSync(new SessionsStub(), new ONEVO.Application.Features.WorkManagement.Meetings.MeetingsOptions { Create = true, ReadOnly = true })
            .RecheckAsync(w.H.TenantId, w.Organizer, w.SeriesId, Week(0), CancellationToken.None);

        Assert.Equal((409, MeetingErrorCodes.MeetingsReadOnly), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task LegacyCalendarEventLinks_AreNotTouched()
    {
        var w = await SeedAsync();
        Guid legacyLinkId;
        await using (var db = w.H.NewContext())
        {
            var organizer = await db.Employees.SingleAsync(e => e.Id == w.Organizer);
            var calendarEvent = new CalendarEvent
            {
                Id = Guid.NewGuid(), TenantId = w.H.TenantId, Title = "Legacy sync", CreatedById = organizer.UserId,
                StartDate = Week(0), EndDate = End(0),
            };
            db.PersonalCalendarEvents.Add(calendarEvent);
            var legacy = new CalendarEventMeeting
            {
                Id = Guid.NewGuid(), TenantId = w.H.TenantId, CalendarEventId = calendarEvent.Id, OwnerKind = CalendarMeetingOwnerKinds.CalendarEvent,
                ExternalCalendarConnectionId = (await db.ExternalCalendarConnections.SingleAsync()).Id, Provider = CalendarEventMeetingProviders.MicrosoftTeams,
                ExternalMeetingId = "legacy-ext", Status = CalendarEventMeetingStatuses.Active, LinkGeneration = 1, CreatedAt = Now,
            };
            db.CalendarEventMeetings.Add(legacy);
            db.CalendarEventMeetingAttendances.Add(new CalendarEventMeetingAttendance
            {
                Id = Guid.NewGuid(), TenantId = w.H.TenantId, CalendarEventMeetingId = legacy.Id, ExternalParticipantEmail = "x@example.com",
                JoinedAt = Week(0), LeftAt = End(0), CreatedAt = Now,
            });
            await db.SaveChangesAsync();
            legacyLinkId = legacy.Id;
        }
        var stub = new SessionsStub { Answer = (_, _) => [Session("r-0", Week(0), Rec("a", w.AttendeeEmail, Week(0), 20))] };

        await using (var db = w.H.NewContext())
        {
            var s = new MeetingTestServices(db, new MeetingTestClock(End(0).AddHours(1)));
            var due = await s.AttendanceSync(stub).ListDueLinksAsync(w.H.TenantId, End(0).AddHours(1), CancellationToken.None);
            Assert.Equal([w.LinkId], due);
        }

        await SyncAsync(w, stub, End(0).AddHours(1));

        Assert.All(stub.Calls, c => Assert.Equal("ext-1", c.MeetingId));
        await using var check = w.H.NewContext();
        var legacyRow = await check.CalendarEventMeetingAttendances.AsNoTracking().SingleAsync(r => r.CalendarEventMeetingId == legacyLinkId);
        Assert.Null(legacyRow.OccurrenceTaskId);
        var legacyLink = await check.CalendarEventMeetings.AsNoTracking().SingleAsync(m => m.Id == legacyLinkId);
        Assert.Null(legacyLink.LastAttendanceSyncedAt);
    }

    [Fact]
    public async Task AParkedLink_IsCancelledByTheSweep_OnceNothingNeedsIt()
    {
        var w = await SeedAsync();
        // Cancelled after the first meeting was held: its attendance still needs the link, so the provider cancel is parked.
        await using (var db = w.H.NewContext())
        {
            var cancelled = await new MeetingTestServices(db, new MeetingTestClock(End(0).AddHours(1))).Cancels.CancelAsync(
                w.H.TenantId, w.Organizer, w.OrganizerUser, w.SeriesId, Week(1), MeetingScopes.All, CancellationToken.None);
            Assert.True(cancelled.IsSuccess, cancelled.Error);
        }

        async Task<(IReadOnlyList<Guid> Parked, bool Cancelled)> SweepAsync(DateTimeOffset at)
        {
            await using var db = w.H.NewContext();
            var s = new MeetingTestServices(db, new MeetingTestClock(at));
            var parked = await s.Links.ListParkedLinkIdsAsync(w.H.TenantId);
            var cancelled = false;
            foreach (var id in parked)
                cancelled |= await s.LinkService(new ProvisionerStub()).CancelIfUnneededAsync(w.H.TenantId, id, CancellationToken.None);
            return (parked, cancelled);
        }

        var whileNeeded = await SweepAsync(End(0).AddHours(2));
        var stub = new SessionsStub { Answer = (_, _) => [Session("r-0", Week(0), Rec("a", w.AttendeeEmail, Week(0), 20))] };
        foreach (var after in new[] { TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24) })
            await SyncAsync(w, stub, End(0) + after);
        var insideRecheckWindow = await SweepAsync(End(0).AddDays(3));
        var afterIt = await SweepAsync(End(0).AddDays(8));

        Assert.Equal([w.LinkId], whileNeeded.Parked);
        Assert.False(whileNeeded.Cancelled);
        Assert.False(insideRecheckWindow.Cancelled);
        Assert.True(afterIt.Cancelled);
        await using var check = w.H.NewContext();
        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, (await check.CalendarEventMeetings.SingleAsync(m => m.Id == w.LinkId)).Status);
        Assert.Single(await check.Set<OutboxMessage>().Where(m => m.DeduplicationKey == $"meeting-cancel:{w.LinkId}:1").ToListAsync());
    }
}
