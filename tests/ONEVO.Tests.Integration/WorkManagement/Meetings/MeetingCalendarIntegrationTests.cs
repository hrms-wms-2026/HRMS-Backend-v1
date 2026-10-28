using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;
using ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Tests.Integration.Calendar;
using Xunit;
using static ONEVO.Tests.Integration.WorkManagement.Meetings.MeetingAttendanceWorld;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Plan C Task 11 (spec 5.9): GET /calendar shows a caller's task meetings next to calendar events - the materialized
/// occurrences and the virtual ones further out, each with the id it has (or will have) as a task - and the conflict check
/// shows a meeting the caller cannot see as "Busy" only.</summary>
public sealed class MeetingCalendarIntegrationTests
{
    private static readonly DateTimeOffset To = Week(4).AddHours(1);

    private static MeetingCalendarProjection Projection(ApplicationDbContext db, MeetingTestServices s)
        => new(s.Meetings, s.Links, new ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository(db), s.Recurrence, s.Clock);

    private static async Task<IReadOnlyList<CalendarEventItem>> CalendarAsync(World w, Guid employeeId)
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(Now));
        return await Projection(db, s).ListAsync(w.H.TenantId, employeeId, Now, To, CancellationToken.None);
    }

    private static async Task<Guid> UserOfAsync(World w, Guid employeeId)
    {
        await using var db = w.H.NewContext();
        return (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employeeId)).UserId;
    }

    [Fact]
    public async Task Calendar_ShowsMaterializedAndVirtualOccurrences_WithDeterministicIds()
    {
        var w = await SeedAsync();
        var attendeeUser = await UserOfAsync(w, w.Attendee);

        CalendarEventsResponse response;
        await using (var db = w.H.NewContext())
        {
            var s = new MeetingTestServices(db, new MeetingTestClock(Now));
            var handler = new GetCalendarEventsQueryHandler(new CalendarNotificationDb.StubCurrentUser(w.H.TenantId, attendeeUser),
                new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db), new EfCalendarEventRepository(db),
                new ONEVO.Infrastructure.Services.Calendar.IcalNetRecurrenceExpander(), Projection(db, s));
            var result = await handler.Handle(new GetCalendarEventsQuery(Now, To), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
            response = result.Value!;
        }

        var meetings = response.Events.Where(e => e.ItemKind == "meeting").OrderBy(e => e.StartDate).ToList();
        Assert.Equal(5, meetings.Count);
        Assert.Equal([Week(0), Week(1), Week(2), Week(3), Week(4)], meetings.Select(m => m.StartDate));
        Assert.Equal(w.Tasks, meetings.Take(3).Select(m => m.Id));
        Assert.Equal(w.Tasks.Select(t => (Guid?)t), meetings.Take(3).Select(m => m.MeetingTaskId));
        Assert.Equal([MeetingOccurrenceIds.For(w.SeriesId, Week(3)), MeetingOccurrenceIds.For(w.SeriesId, Week(4))], meetings.Skip(3).Select(m => m.Id));
        Assert.All(meetings.Skip(3), m => Assert.Null(m.MeetingTaskId));
        Assert.All(meetings, m => Assert.Equal((w.SeriesId, "Weekly sync", MeetingStatuses.Scheduled, false),
            (m.MeetingSeriesId!.Value, m.Title, m.MeetingStatus, m.IsOrganizer)));
        Assert.All(meetings, m => Assert.Equal(m.StartDate, m.OriginalStart));
        Assert.Equal("https://teams.example/join/1", meetings[0].MeetingLink);
        Assert.NotNull(meetings[0].OrganizerName);
    }

    [Fact]
    public async Task Calendar_ExcludesSuppressedAndCancelled()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var cancelled = await new MeetingTestServices(db, new MeetingTestClock(Now)).Cancels.CancelAsync(
                w.H.TenantId, w.Organizer, w.OrganizerUser, w.SeriesId, Week(1), MeetingScopes.This, CancellationToken.None);
            Assert.True(cancelled.IsSuccess, cancelled.Error);
        }
        await using (var db = w.H.NewContext())
        {
            db.MeetingOccurrenceSuppressions.Add(new MeetingOccurrenceSuppression
            {
                TenantId = w.H.TenantId, SeriesId = w.SeriesId, OriginalStart = Week(3), MovedToTaskId = w.Tasks[2],
                CreatedByEmployeeId = w.Organizer,
            });
            await db.SaveChangesAsync();
        }

        var items = await CalendarAsync(w, w.Attendee);

        Assert.Equal([Week(0), Week(2), Week(4)], items.OrderBy(i => i.StartDate).Select(i => i.StartDate));

        await using (var db = w.H.NewContext())
        {
            var all = await new MeetingTestServices(db, new MeetingTestClock(Now)).Cancels.CancelAsync(
                w.H.TenantId, w.Organizer, w.OrganizerUser, w.SeriesId, Week(0), MeetingScopes.All, CancellationToken.None);
            Assert.True(all.IsSuccess, all.Error);
        }
        Assert.Empty(await CalendarAsync(w, w.Attendee));
    }

    [Fact]
    public async Task Calendar_VirtualIdEqualsTheTaskIdItLaterBecomes()
    {
        var w = await SeedAsync();
        var virtualId = (await CalendarAsync(w, w.Attendee)).Single(i => i.StartDate == Week(3)).Id;

        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(Now));
        var series = (await s.Meetings.GetSeriesAsync(w.H.TenantId, w.SeriesId))!;
        var ensured = await s.UnitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await s.Writer.LockSeriesAsync(w.H.TenantId, w.SeriesId, ct);
            return await s.Materializer.EnsureAsync(w.H.TenantId, series, Week(3), w.OrganizerUser, ct);
        }, CancellationToken.None);

        Assert.Equal(virtualId, ensured.Value);
    }

    [Fact]
    public async Task Outsider_SeesNoMeeting()
    {
        var w = await SeedAsync();
        Guid outsider;
        await using (var db = w.H.NewContext())
        {
            outsider = w.H.AddEmployee(db).Id;
            await db.SaveChangesAsync();
        }

        Assert.Empty(await CalendarAsync(w, outsider));
        Assert.Equal(5, (await CalendarAsync(w, w.Organizer)).Count(i => i.IsOrganizer || i.MeetingSeriesId == w.SeriesId));
    }

    [Fact]
    public async Task Conflicts_InvisibleMeetingIsBusyOnly_VisibleOneHasItsTitle()
    {
        var w = await SeedAsync();
        Guid outsiderUser;
        await using (var db = w.H.NewContext())
        {
            outsiderUser = w.H.AddEmployee(db).UserId;
            await db.SaveChangesAsync();
        }

        async Task<CalendarConflictsResponse> CheckAsync(Guid callerUser, DateTimeOffset start)
        {
            await using var db = w.H.NewContext();
            var s = new MeetingTestServices(db, new MeetingTestClock(Now));
            var handler = new CheckCalendarConflictsQueryHandler(new CalendarNotificationDb.StubCurrentUser(w.H.TenantId, callerUser),
                new EfCalendarEventRepository(db), new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db),
                new ONEVO.Infrastructure.Services.Calendar.IcalNetRecurrenceExpander(), Projection(db, s));
            var result = await handler.Handle(new CheckCalendarConflictsQuery([w.Attendee], start, start.AddMinutes(60)), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
            return result.Value!;
        }

        var hidden = Assert.Single((await CheckAsync(outsiderUser, Week(0).AddMinutes(-15))).Conflicts);
        var shown = Assert.Single((await CheckAsync(w.OrganizerUser, Week(0).AddMinutes(-15))).Conflicts);
        var virtualShown = Assert.Single((await CheckAsync(w.OrganizerUser, Week(4).AddMinutes(-15))).Conflicts);

        Assert.Equal((Guid.Empty, "Busy", true, Week(0), End(0)),
            (hidden.ConflictingEventId, hidden.ConflictingEventTitle, hidden.IsBusyOnly, hidden.OverlapStart, hidden.OverlapEnd));
        Assert.Equal((w.Tasks[0], "Weekly sync", false), (shown.ConflictingEventId, shown.ConflictingEventTitle, shown.IsBusyOnly));
        Assert.Equal((MeetingOccurrenceIds.For(w.SeriesId, Week(4)), false), (virtualShown.ConflictingEventId, virtualShown.IsBusyOnly));
    }
    private sealed class FixedZone : ONEVO.Application.Features.Calendar.Services.ICalendarTimezoneResolver
    {
        public Task<string> ResolveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) => Task.FromResult("UTC");
    }

    /// <summary>A caller with no leave read permission: they see only their own leave.</summary>
    private sealed class NoLeaveScope : ONEVO.Application.Features.Leave.Calendar.Services.ILeaveVisibilityScopeProvider
    {
        public Task<ONEVO.Application.Features.Leave.Calendar.Services.LeaveVisibilityScopeResolution> ResolveForCurrentUserAsync(CancellationToken ct = default) =>
            Task.FromResult(new ONEVO.Application.Features.Leave.Calendar.Services.LeaveVisibilityScopeResolution(
                null, ONEVO.Application.Features.Leave.Calendar.Services.LeaveVisibilityScopeFailure.NoLeaveReadPermission));
    }

    private static async Task<CalendarConflictsResponse> CheckWithAvailabilityAsync(
        World w, Guid callerUser, DateTimeOffset start, Guid? excludeSeries = null)
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(Now));
        var availability = new ParticipantAvailabilityChecker(
            new ONEVO.Infrastructure.Persistence.Repositories.Leave.Request.EfLeaveRequestReadRepository(db),
            new ONEVO.Application.Features.Leave.Calendar.Services.NoOpLeaveCalendarHolidayProvider(),
            new ONEVO.Infrastructure.Persistence.Repositories.OrgStructure.EfLegalEntityRepository(db),
            new FixedZone(), new NoLeaveScope(),
            new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfPositionAssignmentRepository(db,
                new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeHierarchyClosureRepository(db, new MeetingTestClock(Now))));
        var handler = new CheckCalendarConflictsQueryHandler(new CalendarNotificationDb.StubCurrentUser(w.H.TenantId, callerUser),
            new EfCalendarEventRepository(db), new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db),
            new ONEVO.Infrastructure.Services.Calendar.IcalNetRecurrenceExpander(), Projection(db, s), availability);
        var result = await handler.Handle(new CheckCalendarConflictsQuery([w.Attendee], start, start.AddMinutes(60), IsAllDay: true,
            TimeZone: "UTC", ExcludeMeetingSeriesId: excludeSeries), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task Conflicts_SkipTheSeriesBeingEdited()
    {
        var w = await SeedAsync();

        var conflicts = (await CheckWithAvailabilityAsync(w, w.OrganizerUser, Week(0).AddMinutes(-15), w.SeriesId)).Conflicts;

        Assert.DoesNotContain(conflicts, c => c.Kind == CalendarConflictKinds.Event);
    }

    [Fact]
    public async Task Conflicts_ApprovedLeave_IsOnLeaveForSelf_BusyForOthers_AndNeverCarriesLeaveDetails()
    {
        var w = await SeedAsync();
        var day = DateOnly.FromDateTime(Week(1).UtcDateTime);
        await using (var db = w.H.NewContext())
        {
            var type = w.H.AddLeaveType(db);
            w.H.AddApprovedLeave(db, w.Attendee, type, day, day).Reason = "Family trip";
            await db.SaveChangesAsync();
        }
        var attendeeUser = await UserOfAsync(w, w.Attendee);
        var probe = Week(1).AddHours(2);

        var own = Assert.Single((await CheckWithAvailabilityAsync(w, attendeeUser, probe)).Conflicts, c => c.Kind == CalendarConflictKinds.Leave);
        var other = Assert.Single((await CheckWithAvailabilityAsync(w, w.OrganizerUser, probe)).Conflicts, c => c.Kind == CalendarConflictKinds.Leave);

        Assert.Equal(("On leave", Guid.Empty), (own.ConflictingEventTitle, own.ConflictingEventId));
        Assert.Equal("Busy", other.ConflictingEventTitle);
        var json = System.Text.Json.JsonSerializer.Serialize(new[] { own, other });
        Assert.DoesNotContain("Family trip", json);
        Assert.DoesNotContain("Annual", json);
    }
    [Fact]
    public async Task Conflicts_AcceptLocalOffsetTimes_AgainstPostgres()
    {
        // The meeting form sends "...+05:30"; Npgsql rejects non-UTC DateTimeOffsets for timestamptz.
        var w = await SeedAsync();

        var conflicts = (await CheckWithAvailabilityAsync(w, w.OrganizerUser, Week(0).AddMinutes(-15).ToOffset(TimeSpan.FromHours(5.5)))).Conflicts;

        Assert.Contains(conflicts, c => c.Kind == CalendarConflictKinds.Event);
    }
}
