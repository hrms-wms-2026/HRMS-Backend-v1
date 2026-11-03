using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Services.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec 5.1 / 5.9 reads: one visibility rule; "not found" and "not allowed" are the same 404.</summary>
public sealed class MeetingReadIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = First.AddDays(-1);

    private sealed record World(MyTeamDb H, MeetingSeries Series, Guid Occ1, Guid Occ2, Guid Organizer, Guid Attendee, Guid Outsider);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var organizer = h.AddEmployee(db, lastName: "Organizer");
        var attendee = h.AddEmployee(db, lastName: "Attendee");
        var outsider = h.AddEmployee(db);
        var series = h.AddSeries(db, at with { OrganizerEmployeeId = organizer.Id }, First, "FREQ=WEEKLY");
        h.AddSeriesParticipant(db, series, organizer.Id, CalendarEventParticipantStatuses.Accepted);
        h.AddSeriesParticipant(db, series, attendee.Id);
        var (occ1, _) = h.AddOccurrence(db, series, at, First);
        var (occ2, _) = h.AddOccurrence(db, series, at, First.AddDays(7));
        db.MeetingOccurrenceSuppressions.Add(new MeetingOccurrenceSuppression
        {
            SeriesId = series.Id, OriginalStart = First.AddDays(28), TenantId = h.TenantId, MovedToTaskId = occ2.Id,
            CreatedByEmployeeId = organizer.Id,
        });
        await db.SaveChangesAsync();
        return new World(h, series, occ1.Id, occ2.Id, organizer.Id, attendee.Id, outsider.Id);
    }

    private static MeetingReadService Reads(ApplicationDbContext db) => new(new EfMeetingRepository(db),
        new MeetingAccessResolver(new EfMeetingRepository(db)), new EfProjectRepository(db), new EfObjectiveRepository(db),
        new EfEmployeeRepository(db), new IcalNetMeetingRecurrence(), new FixedClock(Now));

    [Fact]
    public async Task OrganizerAndAttendee_ReadTheOccurrence()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var asOrganizer = await Reads(db).GetOccurrenceAsync(w.H.TenantId, w.Organizer, w.Occ1, CancellationToken.None);
        var asAttendee = await Reads(db).GetOccurrenceAsync(w.H.TenantId, w.Attendee, w.Occ1, CancellationToken.None);

        Assert.Equal((MeetingRoles.Organizer, true, MeetingStatuses.Scheduled), (asOrganizer.Value!.MyRole, asOrganizer.Value.CanEdit, asOrganizer.Value.Status));
        Assert.Equal((MeetingRoles.Attendee, false, CalendarEventParticipantStatuses.Pending), (asAttendee.Value!.MyRole, asAttendee.Value.CanEdit, asAttendee.Value.MyResponse));
        Assert.Equal(w.Series.Title, asAttendee.Value.Title);
        Assert.Contains(asAttendee.Value.Attendees, a => a.EmployeeId == w.Attendee && a.Name!.EndsWith("Attendee"));
        Assert.EndsWith("Organizer", asAttendee.Value.Organizer.Name);
    }

    [Fact]
    public async Task AnOutsider_GetsTheSame404AsAnUnknownId()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var hidden = await Reads(db).GetOccurrenceAsync(w.H.TenantId, w.Outsider, w.Occ1, CancellationToken.None);
        var unknown = await Reads(db).GetOccurrenceAsync(w.H.TenantId, w.Outsider, Guid.NewGuid(), CancellationToken.None);
        var hiddenSeries = await Reads(db).GetSeriesAsync(w.H.TenantId, w.Outsider, w.Series.Id, CancellationToken.None);

        Assert.Equal((404, MeetingErrorCodes.NotFound), (hidden.StatusCode, hidden.ErrorCode));
        Assert.Equal((unknown.StatusCode, unknown.ErrorCode, unknown.Error), (hidden.StatusCode, hidden.ErrorCode, hidden.Error));
        Assert.Equal((hidden.StatusCode, hidden.Error), (hiddenSeries.StatusCode, hiddenSeries.Error));
    }

    [Fact]
    public async Task AVirtualOccurrence_HasItsFutureTaskId_AndASuppressedSlotIs404()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var third = await Reads(db).GetOccurrenceAtAsync(w.H.TenantId, w.Attendee, w.Series.Id, First.AddDays(14), CancellationToken.None);
        var suppressed = await Reads(db).GetOccurrenceAtAsync(w.H.TenantId, w.Attendee, w.Series.Id, First.AddDays(28), CancellationToken.None);
        var offRule = await Reads(db).GetOccurrenceAtAsync(w.H.TenantId, w.Attendee, w.Series.Id, First.AddDays(15), CancellationToken.None);

        Assert.Equal(MeetingOccurrenceIds.For(w.Series.Id, First.AddDays(14)), third.Value!.TaskId);
        Assert.Equal(First.AddDays(14).AddMinutes(w.Series.DurationMinutes), third.Value.EndAt);
        Assert.Equal(404, suppressed.StatusCode);
        Assert.Equal(404, offRule.StatusCode);
    }

    [Fact]
    public async Task TheList_ShowsOnlyVisibleOccurrencesInRange()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var attendeeView = await Reads(db).ListAsync(w.H.TenantId, w.Attendee, First.AddHours(-1), First.AddDays(1), null, CancellationToken.None);
        var outsiderView = await Reads(db).ListAsync(w.H.TenantId, w.Outsider, First.AddDays(-30), First.AddDays(30), null, CancellationToken.None);

        var item = Assert.Single(attendeeView.Value!);
        Assert.Equal((w.Occ1, MeetingRoles.Attendee, CalendarEventParticipantStatuses.Pending), (item.TaskId, item.MyRole, item.MyResponse));
        Assert.Empty(outsiderView.Value!);
    }

    [Fact]
    public async Task TheSeries_ListsItsUpcomingOccurrences()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();

        var series = await Reads(db).GetSeriesAsync(w.H.TenantId, w.Organizer, w.Series.Id, CancellationToken.None);

        Assert.Equal([w.Occ1, w.Occ2], series.Value!.UpcomingOccurrences.Select(o => o.TaskId).ToList());
        Assert.Equal(2, series.Value.Attendees.Count);
    }

    private sealed class FixedClock(DateTimeOffset now) : ONEVO.Application.Common.ServiceInterfaces.IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
        public DateOnly Today => DateOnly.FromDateTime(now.UtcDateTime);
    }
}
