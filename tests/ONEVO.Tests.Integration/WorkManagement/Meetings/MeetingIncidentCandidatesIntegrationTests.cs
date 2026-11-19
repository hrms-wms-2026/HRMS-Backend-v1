using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>
/// The nightly C2 incident pass picks the employees to evaluate. A series participant has no calendar event row
/// (event_id is null), so the legacy candidate query never finds them: this query supplies everyone who attends a
/// task-meeting occurrence in the window - organizer, series participants and per-occurrence adds - so the
/// NoResponse, DoubleBook and OnLeave rules run for task meetings too (spec 3, C2 engine).
/// </summary>
public sealed class MeetingIncidentCandidatesIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EveryoneWhoAttendsAnOccurrenceInTheWindow_IsACandidate_NobodyElse()
    {
        var h = await MyTeamDb.CreateAsync();
        Guid organizer, participant, added, outsideWindowOnly, unrelated;
        await using (var db = h.NewContext())
        {
            var at = h.AddPlacement(db);
            organizer = at.OrganizerEmployeeId;
            participant = h.AddEmployee(db).Id;
            added = h.AddEmployee(db).Id;
            outsideWindowOnly = h.AddEmployee(db).Id;
            unrelated = h.AddEmployee(db).Id;

            var inWindow = h.AddSeries(db, at, First);
            h.AddSeriesParticipant(db, inWindow, participant, CalendarEventParticipantStatuses.Pending);
            var (occurrence, _) = h.AddOccurrence(db, inWindow, at, First);
            h.AddEmployeeOverride(db, occurrence.Id, added, MeetingAttendeeChanges.Add);

            // A series whose only occurrence is weeks after the window.
            var later = h.AddSeries(db, at with { OrganizerEmployeeId = outsideWindowOnly }, First.AddDays(40));
            h.AddOccurrence(db, later, at with { OrganizerEmployeeId = outsideWindowOnly }, First.AddDays(40));
            await db.SaveChangesAsync();
        }

        await using var read = h.NewContext();
        var candidates = await new EfMeetingRepository(read).ListAttendeeEmployeeIdsInRangeAsync(
            h.TenantId, First.AddDays(-1), First.AddDays(7));

        Assert.Equal(new[] { organizer, participant, added }.OrderBy(x => x), candidates.OrderBy(x => x));
        Assert.DoesNotContain(outsideWindowOnly, candidates);
        Assert.DoesNotContain(unrelated, candidates);
    }

    [Fact]
    public async Task AnotherTenantsMeetings_AreNotCandidates()
    {
        var h = await MyTeamDb.CreateAsync();
        var other = await h.AddTenantAsync();
        await using (var db = other.NewContext())
        {
            var at = other.AddPlacement(db);
            var series = other.AddSeries(db, at, First);
            other.AddOccurrence(db, series, at, First);
            await db.SaveChangesAsync();
        }

        await using var read = h.NewContext();
        Assert.Empty(await new EfMeetingRepository(read).ListAttendeeEmployeeIdsInRangeAsync(
            h.TenantId, First.AddDays(-1), First.AddDays(7)));
    }
}
