using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec 5.3 attendees: series add/remove (removal revokes access at once), per-occurrence add/remove, RSVP.
/// Declining keeps access (D18).</summary>
public sealed class MeetingAttendeesIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = First.AddDays(-1);

    private sealed record World(MyTeamDb H, MeetingSeries Series, Guid Occ1, Guid Occ2, Guid Organizer, Guid Member, Guid Newcomer);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var member = h.AddEmployee(db);
        var newcomer = h.AddEmployee(db);
        var series = h.AddSeries(db, at, First, "FREQ=WEEKLY");
        h.AddSeriesParticipant(db, series, at.OrganizerEmployeeId, CalendarEventParticipantStatuses.Accepted);
        h.AddSeriesParticipant(db, series, member.Id);
        var (o1, _) = h.AddOccurrence(db, series, at, First);
        var (o2, _) = h.AddOccurrence(db, series, at, First.AddDays(7));
        await db.SaveChangesAsync();
        return new World(h, series, o1.Id, o2.Id, at.OrganizerEmployeeId, member.Id, newcomer.Id);
    }

    private static MeetingTestServices Services(ONEVO.Infrastructure.Persistence.ApplicationDbContext db) => new(db, new MeetingTestClock(Now));

    private static async Task<string?> RoleAtAsync(World w, Guid caller, DateTimeOffset start)
    {
        await using var db = w.H.NewContext();
        return (await Services(db).Access.ResolveForOccurrenceAsync(w.H.TenantId, caller, w.Series.Id, start, CancellationToken.None))?.Role;
    }

    [Fact]
    public async Task SeriesRemove_RevokesAccessAtOnce_AndSeriesAddBringsThemBack()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
            Assert.True((await Services(db).Attendees.RemoveSeriesEmployeeAsync(w.H.TenantId, w.Organizer, w.Series.Id, w.Member,
                CancellationToken.None)).IsSuccess);
        Assert.Null(await RoleAtAsync(w, w.Member, First));

        await using (var db = w.H.NewContext())
        {
            var added = await Services(db).Attendees.AddSeriesAttendeesAsync(w.H.TenantId, w.Organizer, w.Series.Id,
                new MeetingAttendeesRequest([w.Member], ["Client@Example.com"]), CancellationToken.None);
            Assert.Equal(2, added.Value!.Added);
        }

        Assert.Equal(MeetingRoles.Attendee, await RoleAtAsync(w, w.Member, First));
        await using var check = w.H.NewContext();
        Assert.Single(await check.CalendarEventParticipants.IgnoreQueryFilters()
            .Where(p => p.MeetingSeriesId == w.Series.Id && p.EmployeeId == w.Member).ToListAsync());   // revived, not duplicated
    }

    [Fact]
    public async Task AnOccurrenceAdd_SeesOnlyThatOccurrence_AndAnOccurrenceRemoveHidesOne()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var services = Services(db);
            Assert.True((await services.Attendees.AddOccurrenceAttendeesAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, First,
                new MeetingAttendeesRequest([w.Newcomer], []), CancellationToken.None)).IsSuccess);
        }
        await using (var db = w.H.NewContext())
            Assert.True((await Services(db).Attendees.RemoveOccurrenceEmployeeAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id,
                First.AddDays(7), w.Member, CancellationToken.None)).IsSuccess);

        Assert.Equal(MeetingRoles.Attendee, await RoleAtAsync(w, w.Newcomer, First));
        Assert.Null(await RoleAtAsync(w, w.Newcomer, First.AddDays(7)));
        Assert.Equal(MeetingRoles.Attendee, await RoleAtAsync(w, w.Member, First));
        Assert.Null(await RoleAtAsync(w, w.Member, First.AddDays(7)));
    }

    [Fact]
    public async Task ASeriesAdd_ClearsTheirFutureRemoveOverride()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
            Assert.True((await Services(db).Attendees.RemoveOccurrenceEmployeeAsync(w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id,
                First.AddDays(7), w.Member, CancellationToken.None)).IsSuccess);
        await using (var db = w.H.NewContext())
        {
            var services = Services(db);
            Assert.True((await services.Attendees.RemoveSeriesEmployeeAsync(w.H.TenantId, w.Organizer, w.Series.Id, w.Member, CancellationToken.None)).IsSuccess);
        }
        await using (var db = w.H.NewContext())
            Assert.True((await Services(db).Attendees.AddSeriesAttendeesAsync(w.H.TenantId, w.Organizer, w.Series.Id,
                new MeetingAttendeesRequest([w.Member], []), CancellationToken.None)).IsSuccess);

        Assert.Equal(MeetingRoles.Attendee, await RoleAtAsync(w, w.Member, First.AddDays(7)));
    }

    [Fact]
    public async Task Declining_KeepsAccess_AndWritesRsvpHistory()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
            Assert.True((await Services(db).Attendees.RespondAsync(w.H.TenantId, w.Member, w.Series.Id,
                new RespondToMeetingRequest(CalendarEventParticipantStatuses.Rejected, "On leave", null), CancellationToken.None)).IsSuccess);

        Assert.Equal(MeetingRoles.Attendee, await RoleAtAsync(w, w.Member, First));
        await using var check = w.H.NewContext();
        var history = await check.CalendarEventParticipantRsvpHistory.SingleAsync(r => r.EmployeeId == w.Member);
        Assert.Equal((w.Series.Id, CalendarEventParticipantStatuses.Pending, CalendarEventParticipantStatuses.Rejected, "On leave"),
            (history.EventId, history.FromStatus, history.ToStatus, history.Reason));
    }

    [Fact]
    public async Task ARemovedPerson_Gets404_AndTheOrganizerCannotBeRemoved()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
            await Services(db).Attendees.RemoveSeriesEmployeeAsync(w.H.TenantId, w.Organizer, w.Series.Id, w.Member, CancellationToken.None);

        await using var check = w.H.NewContext();
        var services = Services(check);
        var respond = await services.Attendees.RespondAsync(w.H.TenantId, w.Member, w.Series.Id,
            new RespondToMeetingRequest(CalendarEventParticipantStatuses.Accepted, null, null), CancellationToken.None);
        var removeOrganizer = await services.Attendees.RemoveSeriesEmployeeAsync(w.H.TenantId, w.Organizer, w.Series.Id, w.Organizer,
            CancellationToken.None);

        Assert.Equal(404, respond.StatusCode);
        Assert.Equal((400, MeetingErrorCodes.OrganizerCannotBeRemoved), (removeOrganizer.StatusCode, removeOrganizer.ErrorCode));
    }
}
