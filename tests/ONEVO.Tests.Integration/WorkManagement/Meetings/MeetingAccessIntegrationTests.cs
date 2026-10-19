using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec 5.1 / D6: organizer and attendees only. The project lead, module owner, tenant Owner, admin "*" and HR
/// are outsiders - the resolver has no role check at all.</summary>
public sealed class MeetingAccessIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private sealed record World(MyTeamDb H, Guid SeriesId, Guid Occ1, Guid Occ2, Guid Organizer, Guid Attendee,
        Guid RemovedFromOcc2, Guid AddedToOcc1, IReadOnlyList<Guid> Outsiders);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);                       // its organizer is the project lead and root module owner
        var organizer = h.AddEmployee(db);
        var attendee = h.AddEmployee(db);
        var removed = h.AddEmployee(db);
        var added = h.AddEmployee(db);
        var owner = h.AddEmployee(db);
        var hr = h.AddEmployee(db);
        var peer = h.AddEmployee(db);
        h.GrantPermission(db, owner.UserId, "*");
        h.GrantPermission(db, hr.UserId, "employees:write");
        h.AddMember(db, at.ProjectId, at.ObjectiveId, organizer.Id);
        h.AddMember(db, at.ProjectId, at.ObjectiveId, peer.Id);

        var series = h.AddSeries(db, at with { OrganizerEmployeeId = organizer.Id }, First, "FREQ=WEEKLY");
        h.AddSeriesParticipant(db, series, organizer.Id, CalendarEventParticipantStatuses.Accepted);
        h.AddSeriesParticipant(db, series, attendee.Id);
        h.AddSeriesParticipant(db, series, removed.Id);
        var (occ1, _) = h.AddOccurrence(db, series, at, First);
        var (occ2, _) = h.AddOccurrence(db, series, at, First.AddDays(7));
        h.AddEmployeeOverride(db, occ1.Id, added.Id, MeetingAttendeeChanges.Add);
        h.AddEmployeeOverride(db, occ2.Id, removed.Id, MeetingAttendeeChanges.Remove);
        await db.SaveChangesAsync();

        return new World(h, series.Id, occ1.Id, occ2.Id, organizer.Id, attendee.Id, removed.Id, added.Id,
            [at.OrganizerEmployeeId, owner.Id, hr.Id, peer.Id]);
    }

    [Fact]
    public async Task Resolver_GivesEachCallerTheirRole_AndOutsidersNothing()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewContext();
        var resolver = new MeetingAccessResolver(new EfMeetingRepository(db));

        async Task<string?> At(Guid caller, DateTimeOffset start)
            => (await resolver.ResolveForOccurrenceAsync(w.H.TenantId, caller, w.SeriesId, start, CancellationToken.None))?.Role;
        async Task<string?> Series(Guid caller)
            => (await resolver.ResolveForSeriesAsync(w.H.TenantId, caller, w.SeriesId, CancellationToken.None))?.Role;

        Assert.Equal("organizer", await At(w.Organizer, First));
        Assert.Equal("attendee", await At(w.Attendee, First));
        Assert.Equal("attendee", await At(w.RemovedFromOcc2, First));
        Assert.Equal("attendee", await At(w.AddedToOcc1, First));
        Assert.Null(await At(w.RemovedFromOcc2, First.AddDays(7)));
        Assert.Null(await At(w.AddedToOcc1, First.AddDays(7)));
        Assert.Equal("attendee", await At(w.Attendee, First.AddDays(14)));   // a virtual (not yet materialized) occurrence
        Assert.Equal("organizer", await Series(w.Organizer));
        Assert.Equal("attendee", await Series(w.Attendee));
        Assert.Null(await Series(w.AddedToOcc1));                             // an occurrence add sees that occurrence only
        foreach (var outsider in w.Outsiders)
        {
            Assert.Null(await At(outsider, First));
            Assert.Null(await Series(outsider));
        }
    }

    [Fact]
    public async Task Visibility_ListsExactlyTheOccurrencesEachCallerMaySee()
    {
        var w = await SeedAsync();
        await using var db = w.H.NewTenantContext();

        async Task<HashSet<Guid>> Visible(Guid caller)
            => (await MeetingVisibility.ForCaller(db.MeetingDetails, db, caller).Select(d => d.TaskId).ToListAsync()).ToHashSet();

        Assert.Equal([w.Occ1, w.Occ2], await Visible(w.Organizer));
        Assert.Equal([w.Occ1, w.Occ2], await Visible(w.Attendee));
        Assert.Equal([w.Occ1], await Visible(w.RemovedFromOcc2));
        Assert.Equal([w.Occ1], await Visible(w.AddedToOcc1));
        foreach (var outsider in w.Outsiders)
            Assert.Empty(await Visible(outsider));
    }
}
