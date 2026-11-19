using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Infrastructure.Services.Calendar;
using ONEVO.Tests.Integration.Calendar;
using Xunit;
using static ONEVO.Tests.Integration.WorkManagement.Meetings.MeetingAttendanceWorld;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Plan C Task 12 (spec 5.10): the organizer's own external calendar gets a copy of each materialized occurrence -
/// created once, updated when the occurrence changes, deleted when it is cancelled - linked by meeting task id; the copy
/// coming back through the pull never creates an event; an attendee's connection never pushes someone else's meeting.</summary>
public sealed class MeetingExternalPushIntegrationTests
{
    /// <summary>Microsoft Graph as the sync sees it; the pull returns whatever <see cref="Remote"/> holds.</summary>
    private sealed class GraphStub : IMicrosoftGraphCalendarClient
    {
        private int _next;
        public List<GraphEventDto> Created { get; } = [];
        public List<(string Id, GraphEventDto Event)> Updated { get; } = [];
        public List<string> Deleted { get; } = [];
        public List<GraphEventDto> Remote { get; } = [];
        public bool PullReturnsRemote { get; set; }

        public Task<GraphCalendarPage> ListEventsAsync(string accessToken, string? deltaLink, DateTimeOffset windowStart, DateTimeOffset windowEnd,
            CancellationToken ct)
            => Task.FromResult(new GraphCalendarPage(PullReturnsRemote ? Remote.ToList() : [], "delta-1"));

        public Task<GraphEventDto> CreateEventAsync(string accessToken, GraphEventDto @event, CancellationToken ct)
        {
            var created = @event with { Id = $"remote-{++_next}", Etag = "e1" };
            Created.Add(@event);
            Remote.Add(created);
            return Task.FromResult(created);
        }

        public HashSet<string> MissingRemotely { get; } = [];

        public Task<GraphEventDto> UpdateEventAsync(string accessToken, string eventId, GraphEventDto @event, CancellationToken ct)
        {
            if (MissingRemotely.Contains(eventId))
                throw new HttpRequestException("Not found", null, System.Net.HttpStatusCode.NotFound);
            Updated.Add((eventId, @event));
            return Task.FromResult(@event with { Id = eventId, Etag = "e2" });
        }

        public Task DeleteEventAsync(string accessToken, string eventId, CancellationToken ct)
        {
            if (MissingRemotely.Contains(eventId))
                throw new HttpRequestException("Gone", null, System.Net.HttpStatusCode.Gone);
            Deleted.Add(eventId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoGoogle : IGoogleCalendarClient
    {
        public Task<GoogleCalendarPage> ListEventsAsync(string accessToken, string calendarId, string? syncToken, DateTimeOffset windowStart,
            DateTimeOffset windowEnd, CancellationToken ct) => throw new NotSupportedException();
        public Task<GoogleCalendarEventDto> InsertEventAsync(string accessToken, string calendarId, GoogleCalendarEventDto @event, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<GoogleCalendarEventDto> PatchEventAsync(string accessToken, string calendarId, string eventId, GoogleCalendarEventDto @event,
            CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Tokens : ICalendarConnectionTokenProvider
    {
        public Task<string?> GetFreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct)
            => Task.FromResult<string?>("token");
        public Task<string?> ForceRefreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct)
            => Task.FromResult<string?>("token");
    }

    private static async Task SyncAsync(World w, Guid connectionId, GraphStub graph)
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(Now));
        var sync = new CalendarSyncService(new EfExternalCalendarConnectionRepository(db), new EfExternalCalendarEventLinkRepository(db),
            new EfCalendarEventRepository(db), new Tokens(), new NoGoogle(), graph, CalendarNotificationDb.Encryption, s.UnitOfWork,
            NullLogger<CalendarSyncService>.Instance, null, new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db), s.Meetings);
        await sync.SyncConnectionAsync(w.H.TenantId, connectionId, CancellationToken.None);
    }

    private static async Task<Guid> OrganizerConnectionAsync(World w)
    {
        await using var db = w.H.NewContext();
        return (await db.ExternalCalendarConnections.SingleAsync(c => c.UserId == w.OrganizerUser)).Id;
    }

    private static async Task<IReadOnlyList<ExternalCalendarEventLink>> LinksAsync(World w)
    {
        await using var db = w.H.NewContext();
        return await db.ExternalCalendarEventLinks.AsNoTracking().ToListAsync();
    }

    private static async Task EditThisAsync(World w, int k, EditMeetingRequest request)
    {
        await using var db = w.H.NewContext();
        var edit = await new MeetingTestServices(db, new MeetingTestClock(Now)).Edits.EditAsync(w.H.TenantId, w.Organizer, w.OrganizerUser,
            w.SeriesId, Week(k), request, CancellationToken.None);
        Assert.True(edit.IsSuccess, edit.Error);
    }

    [Fact]
    public async Task OrganizerOccurrence_IsPushedOnce_AndPatchedOnEdit()
    {
        var w = await SeedAsync();
        var connection = await OrganizerConnectionAsync(w);
        var graph = new GraphStub();

        await SyncAsync(w, connection, graph);
        await SyncAsync(w, connection, graph);

        Assert.Equal(3, graph.Created.Count);
        Assert.Empty(graph.Updated);
        Assert.Empty(graph.Created.Where(e => e.AttendeeEmails is { Count: > 0 }));   // ONEVO sends the invites, not Outlook
        var links = await LinksAsync(w);
        Assert.Equal(w.Tasks.Order(), links.Select(l => l.MeetingTaskId!.Value).Order());
        Assert.All(links, l => Assert.Null(l.CalendarEventId));

        await EditThisAsync(w, 0, new EditMeetingRequest(MeetingScopes.This, 0, "Weekly sync - moved room", null, "Room 2", null, null,
            null, null, null, null));
        await SyncAsync(w, connection, graph);

        var (id, updated) = Assert.Single(graph.Updated);
        Assert.Equal(links.Single(l => l.MeetingTaskId == w.Tasks[0]).ExternalEventId, id);
        Assert.Equal(("Weekly sync - moved room", "Room 2", Week(0), End(0)), (updated.Title, updated.Location, updated.Start, updated.End));
    }

    [Fact]
    public async Task CancelledOccurrence_DeletesTheRemoteCopy()
    {
        var w = await SeedAsync();
        var connection = await OrganizerConnectionAsync(w);
        var graph = new GraphStub();
        await SyncAsync(w, connection, graph);
        var remote = (await LinksAsync(w)).Single(l => l.MeetingTaskId == w.Tasks[0]).ExternalEventId;
        await using (var db = w.H.NewContext())
        {
            var cancelled = await new MeetingTestServices(db, new MeetingTestClock(Now)).Cancels.CancelAsync(
                w.H.TenantId, w.Organizer, w.OrganizerUser, w.SeriesId, Week(0), MeetingScopes.This, CancellationToken.None);
            Assert.True(cancelled.IsSuccess, cancelled.Error);
        }

        await SyncAsync(w, connection, graph);

        Assert.Equal([remote], graph.Deleted);
        Assert.DoesNotContain(await LinksAsync(w), l => l.MeetingTaskId == w.Tasks[0]);
        Assert.Equal(3, graph.Created.Count);
    }

    [Fact]
    public async Task PulledBackCopy_NeverDuplicates()
    {
        var w = await SeedAsync();
        var connection = await OrganizerConnectionAsync(w);
        var graph = new GraphStub();
        await SyncAsync(w, connection, graph);

        graph.PullReturnsRemote = true;
        await SyncAsync(w, connection, graph);

        await using var db = w.H.NewContext();
        Assert.Empty(await db.CalendarEvents.ToListAsync());
        Assert.Equal(3, graph.Created.Count);
        Assert.Equal(3, (await LinksAsync(w)).Count);
    }

    [Fact]
    public async Task AttendeesConnection_DoesNotPushSomeoneElsesMeeting()
    {
        var w = await SeedAsync();
        Guid attendeeConnection;
        await using (var db = w.H.NewContext())
        {
            var attendeeUser = (await db.Employees.SingleAsync(e => e.Id == w.Attendee)).UserId;
            var c = new ExternalCalendarConnection
            {
                Id = Guid.NewGuid(), TenantId = w.H.TenantId, UserId = attendeeUser, Provider = CalendarExternalSources.OutlookCalendar,
                ExternalAccountEmail = w.AttendeeEmail, ScopesJson = "[\"Calendars.ReadWrite\"]",
                Status = ExternalCalendarConnectionStatuses.Active, CreatedAt = Now,
            };
            db.ExternalCalendarConnections.Add(c);
            await db.SaveChangesAsync();
            attendeeConnection = c.Id;
        }
        var graph = new GraphStub();

        await SyncAsync(w, attendeeConnection, graph);

        Assert.Empty(graph.Created);
        Assert.Empty(await LinksAsync(w));
    }

    [Fact]
    public async Task PrivateOccurrence_PushesBusy()
    {
        var w = await SeedAsync();
        await EditThisAsync(w, 0, new EditMeetingRequest(MeetingScopes.This, 0, null, "Secret agenda", "Room 9", true, null, null, null, null, null));
        var graph = new GraphStub();

        await SyncAsync(w, await OrganizerConnectionAsync(w), graph);

        var busy = Assert.Single(graph.Created, e => e.Start == Week(0));
        Assert.Equal(("Busy", (string?)null, (string?)null, true), (busy.Title, busy.Description, busy.Location, busy.IsPrivate));
        Assert.Equal("Weekly sync", Assert.Single(graph.Created, e => e.Start == Week(1)).Title);
    }

    [Fact]
    public async Task APullOnlyConnection_PushesNothing()
    {
        var w = await SeedAsync();
        var connection = await OrganizerConnectionAsync(w);
        await using (var db = w.H.NewContext())
        {
            await db.ExternalCalendarConnections.Where(c => c.Id == connection)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.SyncDirection, CalendarSyncDirections.PullOnly));
        }
        var graph = new GraphStub();

        await SyncAsync(w, connection, graph);

        Assert.Empty(graph.Created);
    }

    [Fact]
    public async Task ACopyTheOrganizerDeletedRemotely_IsRecreated_AndTheSyncKeepsWorking()
    {
        var w = await SeedAsync();
        var connection = await OrganizerConnectionAsync(w);
        var graph = new GraphStub();
        await SyncAsync(w, connection, graph);
        var links = await LinksAsync(w);
        var deleted = links.Single(l => l.MeetingTaskId == w.Tasks[0]).ExternalEventId;
        var edited = links.Single(l => l.MeetingTaskId == w.Tasks[1]).ExternalEventId;
        var cancelled = links.Single(l => l.MeetingTaskId == w.Tasks[2]).ExternalEventId;
        // Deleted in Outlook: the pull reports week 0's copy cancelled; weeks 1 and 2 are gone too, unseen by the pull.
        graph.Remote.RemoveAll(e => e.Id != deleted);
        graph.Remote[0] = graph.Remote[0] with { IsCancelled = true };
        graph.PullReturnsRemote = true;
        graph.MissingRemotely.UnionWith([edited, cancelled]);
        await EditThisAsync(w, 1, new EditMeetingRequest(MeetingScopes.This, 0, "Week 1 retitled", null, null, null, null, null, null, null, null));
        await using (var db = w.H.NewContext())
        {
            var c = await new MeetingTestServices(db, new MeetingTestClock(Now)).Cancels.CancelAsync(
                w.H.TenantId, w.Organizer, w.OrganizerUser, w.SeriesId, Week(2), MeetingScopes.This, CancellationToken.None);
            Assert.True(c.IsSuccess, c.Error);
        }

        await SyncAsync(w, connection, graph);

        // Week 0 and week 1 are pushed again as new copies; week 2's copy is already gone, so its link just goes.
        Assert.Equal(5, graph.Created.Count);
        Assert.Contains(graph.Created, e => e.Title == "Week 1 retitled");
        var after = await LinksAsync(w);
        Assert.Equal(new[] { w.Tasks[0], w.Tasks[1] }.Order(), after.Select(l => l.MeetingTaskId!.Value).Order());
        Assert.DoesNotContain(after, l => l.ExternalEventId == deleted || l.ExternalEventId == edited);
        await using var check = w.H.NewContext();
        var state = await check.ExternalCalendarConnections.AsNoTracking().SingleAsync(c => c.Id == connection);
        Assert.Equal((0, (string?)null), (state.FailureCount, state.LastError));
    }
}
