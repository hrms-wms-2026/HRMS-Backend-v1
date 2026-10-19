using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Plan C Task 5 (spec 5.7): the link endpoints, and the release rule for a link that several series share after a
/// split - a series cancel or removal releases only its own reference; the provider meeting is cancelled once, when the last
/// reference goes and nothing still needs the link; regenerating affects only that series.</summary>
public sealed class MeetingLinkLifecycleIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset First = Now.AddDays(1);
    private static readonly DateTimeOffset Second = Now.AddDays(8);

    private sealed class ProvisionerStub(MeetingProvisioningOutcome outcome) : IMeetingProvisioner
    {
        public List<MeetingProvisioningRequest> Calls { get; } = [];

        public Task<MeetingProvisioningOutcome> ProvisionAsync(MeetingProvisioningRequest request, CancellationToken ct)
        {
            Calls.Add(request);
            return Task.FromResult(outcome);
        }

        public Task<bool> ResolveForRemovalAsync(MeetingProvisioningRequest request, CancellationToken ct) => Task.FromResult(true);
    }

    private static MeetingProvisioningOutcome Created(string id = "1") => new(MeetingProvisioningOutcomeKind.Created,
        new ProvisionedMeeting($"ext-{id}", $"https://teams.example/join/{id}", $"https://teams.example/org/{id}", null), null);

    private sealed record World(MyTeamDb H, MeetingPlacement At, Guid Organizer, Guid OrganizerUser, Guid Attendee, Guid Outsider,
        Guid SeriesId, Guid LinkId);

    /// <summary>A weekly Teams meeting from <see cref="First"/> whose link is active.</summary>
    private static async Task<World> SeedAsync(string? rule = "FREQ=WEEKLY")
    {
        var h = await MyTeamDb.CreateAsync();
        Guid organizerId, organizerUser, attendee, outsider;
        MeetingPlacement at;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);
            var organizer = h.AddEmployee(db);
            (organizerId, organizerUser) = (organizer.Id, organizer.UserId);
            attendee = h.AddEmployee(db).Id;
            outsider = h.AddEmployee(db).Id;
            db.ExternalCalendarConnections.Add(new ExternalCalendarConnection
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = organizer.UserId, Provider = CalendarExternalSources.OutlookCalendar,
                ExternalAccountEmail = "lead@contoso.example", ScopesJson = "[\"OnlineMeetings.ReadWrite\"]",
                Status = ExternalCalendarConnectionStatuses.Active, CreatedAt = Now,
            });
            await db.SaveChangesAsync();
        }

        MeetingCreatedResponse created;
        await using (var db = h.NewContext())
        {
            var s = new MeetingTestServices(db, new MeetingTestClock(Now));
            var result = await s.UnitOfWork.ExecuteInTransactionAsync(ct => s.Factory.CreateDirectAsync(h.TenantId, organizerId, organizerUser,
                new ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingPlacement(at.ProjectId, at.ObjectiveId, false, true),
                new CreateMeetingRequest(at.ProjectId, at.ObjectiveId, "Design review", null, null, false, First, 30, false, "UTC",
                    rule, [attendee], ["client@example.com"], CalendarEventMeetingProviders.MicrosoftTeams),
                MeetingCreationPaths.Direct, null, ct));
            Assert.True(result.IsSuccess, result.Error);
            created = result.Value!;
        }
        await using (var db = h.NewContext())
        {
            var provisioned = await new MeetingTestServices(db, new MeetingTestClock(Now)).LinkService(new ProvisionerStub(Created()))
                .ProvisionAsync(h.TenantId, created.Link!.LinkId!.Value, created.SeriesId!.Value, recovery: false, CancellationToken.None);
            Assert.Equal(CalendarEventMeetingStatuses.Active, provisioned.Value!.Status);
        }
        return new World(h, at, organizerId, organizerUser, attendee, outsider, created.SeriesId!.Value, created.Link.LinkId.Value);
    }

    /// <summary>Splits the series at <see cref="Second"/>: the new series inherits the link. Returns its id.</summary>
    private static async Task<Guid> SplitAsync(World w)
    {
        await using (var db = w.H.NewContext())
        {
            var edit = await new MeetingTestServices(db, new MeetingTestClock(Now)).Edits.EditAsync(w.H.TenantId, w.Organizer, w.OrganizerUser,
                w.SeriesId, Second, new EditMeetingRequest(MeetingScopes.Following, 0, "Design review v2", null, null, null, null,
                    null, null, null, null), CancellationToken.None);
            Assert.True(edit.IsSuccess, edit.Error);
        }
        await using var check = w.H.NewContext();
        return (await check.MeetingSeries.SingleAsync(s => s.SplitFromSeriesId == w.SeriesId)).Id;
    }

    private static async Task CancelAllAsync(World w, Guid seriesId, DateTimeOffset start, DateTimeOffset? at = null)
    {
        await using var db = w.H.NewContext();
        var result = await new MeetingTestServices(db, new MeetingTestClock(at ?? Now)).Cancels.CancelAsync(
            w.H.TenantId, w.Organizer, w.OrganizerUser, seriesId, start, MeetingScopes.All, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
    }

    private static async Task<T> WithLinksAsync<T>(World w, Func<SeriesMeetingLinkService, Task<T>> act, IMeetingProvisioner? provisioner = null,
        MeetingsOptions? options = null)
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(Now));
        return await act(s.LinkService(provisioner ?? new ProvisionerStub(Created("2")), options));
    }

    private static async Task<IReadOnlyList<string?>> KeysAsync(MyTeamDb h, string prefix)
    {
        await using var db = h.NewContext();
        return (await db.Set<OutboxMessage>().AsNoTracking().Where(m => m.TenantId == h.TenantId).ToListAsync())
            .Where(m => m.DeduplicationKey?.StartsWith(prefix) == true).Select(m => m.DeduplicationKey).Order().ToList();
    }

    private static async Task<string> LinkStatusAsync(World w, Guid linkId)
    {
        await using var db = w.H.NewContext();
        return (await db.CalendarEventMeetings.SingleAsync(m => m.Id == linkId)).Status;
    }

    [Fact]
    public async Task CancelOneOfTwoSeriesSharingALink_QueuesNoProviderCancel()
    {
        var w = await SeedAsync();
        await SplitAsync(w);

        await CancelAllAsync(w, w.SeriesId, First);

        Assert.Empty(await KeysAsync(w.H, "meeting-cancel:"));
        Assert.Equal(CalendarEventMeetingStatuses.Active, await LinkStatusAsync(w, w.LinkId));
        await using var check = w.H.NewContext();
        Assert.NotNull((await check.MeetingSeriesLinks.SingleAsync(l => l.SeriesId == w.SeriesId)).ReleasedAt);
    }

    [Fact]
    public async Task ReleasingBoth_AndNothingNeeded_QueuesExactlyOneProviderCancel()
    {
        var w = await SeedAsync();
        var split = await SplitAsync(w);

        await CancelAllAsync(w, w.SeriesId, First);
        await CancelAllAsync(w, split, Second);

        Assert.Equal([$"meeting-cancel:{w.LinkId}:1"], await KeysAsync(w.H, "meeting-cancel:"));
        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, await LinkStatusAsync(w, w.LinkId));
    }

    [Fact]
    public async Task ReleasingTheLast_WhileAttendanceIsUnfinalized_ParksTheCancel()
    {
        var w = await SeedAsync();

        // The first occurrence has happened; its attendance is not finalized yet.
        await CancelAllAsync(w, w.SeriesId, Second, at: First.AddHours(2));

        Assert.Empty(await KeysAsync(w.H, "meeting-cancel:"));
        Assert.Equal(CalendarEventMeetingStatuses.Active, await LinkStatusAsync(w, w.LinkId));
        await using var check = w.H.NewContext();
        Assert.NotNull((await check.MeetingSeriesLinks.SingleAsync(l => l.SeriesId == w.SeriesId)).ReleasedAt);
    }

    [Fact]
    public async Task RegenerateAffectsOnlyThatSeries()
    {
        var w = await SeedAsync();
        var split = await SplitAsync(w);

        var refused = await WithLinksAsync(w, l => l.RegenerateAsync(w.H.TenantId, w.Organizer, w.OrganizerUser, split,
            new MeetingLinkRequest(CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None));
        var regenerated = await WithLinksAsync(w, l => l.RegenerateAsync(w.H.TenantId, w.Organizer, w.OrganizerUser, split,
            new MeetingLinkRequest(CalendarEventMeetingProviders.MicrosoftTeams, Regenerate: true), CancellationToken.None));

        Assert.Equal((409, MeetingErrorCodes.LinkExists), (refused.StatusCode, refused.ErrorCode));
        Assert.True(regenerated.IsSuccess, regenerated.Error);
        var newLink = regenerated.Value!.LinkId!.Value;
        Assert.NotEqual(w.LinkId, newLink);
        Assert.Equal(CalendarEventMeetingStatuses.Provisioning, regenerated.Value.Status);
        Assert.Equal(CalendarEventMeetingStatuses.Active, await LinkStatusAsync(w, w.LinkId));
        Assert.Empty(await KeysAsync(w.H, "meeting-cancel:"));
        Assert.Equal([$"meeting-link-provision:{newLink}:1"], await KeysAsync(w.H, $"meeting-link-provision:{newLink}"));
        await using var check = w.H.NewContext();
        Assert.Equal(w.LinkId, (await check.MeetingSeriesLinks.SingleAsync(l => l.SeriesId == w.SeriesId && l.ReleasedAt == null)).CalendarEventMeetingId);
        Assert.Equal(newLink, (await check.MeetingSeriesLinks.SingleAsync(l => l.SeriesId == split && l.ReleasedAt == null)).CalendarEventMeetingId);
        Assert.All(await check.MeetingDetails.Where(d => d.SeriesId == split).ToListAsync(), d => Assert.Equal(newLink, d.CalendarEventMeetingId));
        Assert.All(await check.MeetingDetails.Where(d => d.SeriesId == w.SeriesId).ToListAsync(), d => Assert.Equal(w.LinkId, d.CalendarEventMeetingId));
    }

    [Fact]
    public async Task AttendeeGetsJoinUrlOnly_OrganizerGetsStatus_Outsider404()
    {
        var w = await SeedAsync();

        var organizer = await WithLinksAsync(w, l => l.GetAsync(w.H.TenantId, w.Organizer, w.SeriesId, CancellationToken.None));
        var attendee = await WithLinksAsync(w, l => l.GetAsync(w.H.TenantId, w.Attendee, w.SeriesId, CancellationToken.None));
        var outsider = await WithLinksAsync(w, l => l.GetAsync(w.H.TenantId, w.Outsider, w.SeriesId, CancellationToken.None));

        Assert.Equal(("https://teams.example/join/1", CalendarEventMeetingStatuses.Active, "https://teams.example/org/1"),
            (organizer.Value!.JoinUrl, organizer.Value.Link!.Status, organizer.Value.Link.OrganizerJoinUrl));
        Assert.Equal(("https://teams.example/join/1", (MeetingLinkResponse?)null), (attendee.Value!.JoinUrl, attendee.Value.Link));
        Assert.Equal((404, MeetingErrorCodes.NotFound), (outsider.StatusCode, outsider.ErrorCode));
    }

    [Fact]
    public async Task RemoveSendsLinkRemovedToThisSeriesOnly()
    {
        var w = await SeedAsync();
        var split = await SplitAsync(w);

        var removed = await WithLinksAsync(w, l => l.RemoveAsync(w.H.TenantId, w.Organizer, split, CancellationToken.None));

        Assert.True(removed.IsSuccess, removed.Error);
        Assert.True(removed.Value!.ReleasePending);
        await using var check = w.H.NewContext();
        var participant = await check.CalendarEventParticipants.SingleAsync(p => p.MeetingSeriesId == split && p.EmployeeId == w.Attendee);
        var guest = await check.CalendarEventGuests.SingleAsync(g => g.MeetingSeriesId == split);
        Assert.Equal(new[] { CalendarEmailKeys.LinkRemoved(w.LinkId, 1, participant.Id), CalendarEmailKeys.LinkRemoved(w.LinkId, 1, guest.Id) }.Order(),
            await KeysAsync(w.H, "link-removed:"));
        Assert.Equal(CalendarEventMeetingStatuses.Active, await LinkStatusAsync(w, w.LinkId));
        Assert.Empty(await KeysAsync(w.H, "meeting-cancel:"));
        Assert.All(await check.MeetingDetails.Where(d => d.SeriesId == split).ToListAsync(), d => Assert.Null(d.CalendarEventMeetingId));
    }

    [Fact]
    public async Task RemoveTheOnlyReference_QueuesTheProviderCancel()
    {
        var w = await SeedAsync();

        var removed = await WithLinksAsync(w, l => l.RemoveAsync(w.H.TenantId, w.Organizer, w.SeriesId, CancellationToken.None));

        Assert.True(removed.IsSuccess, removed.Error);
        Assert.Equal((CalendarEventMeetingStatuses.Cancelled, false), (removed.Value!.Status, removed.Value.ReleasePending));
        Assert.Equal([$"meeting-cancel:{w.LinkId}:1"], await KeysAsync(w.H, "meeting-cancel:"));
    }

    [Fact]
    public async Task RetryFromFailed_ProvisionsTheSameGeneration_AndCleanupConfirmCancelsAnUnresolvedLink()
    {
        var w = await SeedAsync();
        await using (var db = w.H.NewContext())
        {
            var link = await db.CalendarEventMeetings.SingleAsync();
            link.Status = CalendarEventMeetingStatuses.Failed;
            await db.SaveChangesAsync();
        }
        var provisioner = new ProvisionerStub(Created("3"));

        var retried = await WithLinksAsync(w, l => l.RetryAsync(w.H.TenantId, w.Organizer, w.SeriesId, CancellationToken.None), provisioner);
        var notUnresolved = await WithLinksAsync(w, l => l.ConfirmCleanupAsync(w.H.TenantId, w.Organizer, w.SeriesId, CancellationToken.None));
        await using (var db = w.H.NewContext())
        {
            (await db.CalendarEventMeetings.SingleAsync()).Status = CalendarEventMeetingStatuses.Unresolved;
            await db.SaveChangesAsync();
        }
        var confirmed = await WithLinksAsync(w, l => l.ConfirmCleanupAsync(w.H.TenantId, w.Organizer, w.SeriesId, CancellationToken.None));

        Assert.Equal((CalendarEventMeetingStatuses.Active, "https://teams.example/join/3"), (retried.Value!.Status, retried.Value.JoinUrl));
        Assert.Equal((w.LinkId, 1), (provisioner.Calls.Single().EventId, provisioner.Calls.Single().Generation));
        Assert.Equal((409, MeetingErrorCodes.LinkNotUnresolved), (notUnresolved.StatusCode, notUnresolved.ErrorCode));
        Assert.True(confirmed.IsSuccess, confirmed.Error);
        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, await LinkStatusAsync(w, w.LinkId));
    }

    [Fact]
    public async Task AttendeeCannotChangeTheLink()
    {
        var w = await SeedAsync();

        var removed = await WithLinksAsync(w, l => l.RemoveAsync(w.H.TenantId, w.Attendee, w.SeriesId, CancellationToken.None));

        Assert.Equal((403, MeetingErrorCodes.OrganizerRequired), (removed.StatusCode, removed.ErrorCode));
        Assert.Equal(CalendarEventMeetingStatuses.Active, await LinkStatusAsync(w, w.LinkId));
    }

    [Fact]
    public async Task ReadOnlyBlocksEveryLinkWrite()
    {
        var w = await SeedAsync();
        var readOnly = new MeetingsOptions { Create = true, ReadOnly = true };

        var results = new List<(int? Status, string? Code)>
        {
            Of(await WithLinksAsync(w, l => l.RegenerateAsync(w.H.TenantId, w.Organizer, w.OrganizerUser, w.SeriesId,
                new MeetingLinkRequest(CalendarEventMeetingProviders.MicrosoftTeams, Regenerate: true), CancellationToken.None), options: readOnly)),
            Of(await WithLinksAsync(w, l => l.RetryAsync(w.H.TenantId, w.Organizer, w.SeriesId, CancellationToken.None), options: readOnly)),
            Of(await WithLinksAsync(w, l => l.RemoveAsync(w.H.TenantId, w.Organizer, w.SeriesId, CancellationToken.None), options: readOnly)),
            Of(await WithLinksAsync(w, l => l.ConfirmCleanupAsync(w.H.TenantId, w.Organizer, w.SeriesId, CancellationToken.None), options: readOnly)),
        };

        Assert.All(results, r => Assert.Equal((409, MeetingErrorCodes.MeetingsReadOnly), (r.Status, r.Code)));
        Assert.Equal(CalendarEventMeetingStatuses.Active, await LinkStatusAsync(w, w.LinkId));
        var reads = await WithLinksAsync(w, l => l.GetAsync(w.H.TenantId, w.Organizer, w.SeriesId, CancellationToken.None), options: readOnly);
        Assert.True(reads.IsSuccess);
    }

    private static (int? Status, string? Code) Of<T>(Result<T> result) => (result.StatusCode, result.ErrorCode);

    [Fact]
    public async Task CancellingAOneOffMeetingWithScopeThis_CancelsTheMeetingAndItsProviderMeeting()
    {
        var w = await SeedAsync(rule: null);

        await using (var db = w.H.NewContext())
        {
            var cancelled = await new MeetingTestServices(db, new MeetingTestClock(Now)).Cancels.CancelAsync(
                w.H.TenantId, w.Organizer, w.OrganizerUser, w.SeriesId, First, MeetingScopes.This, CancellationToken.None);
            Assert.True(cancelled.IsSuccess, cancelled.Error);
        }

        await using var check = w.H.NewContext();
        Assert.NotNull((await check.MeetingSeries.SingleAsync(s => s.Id == w.SeriesId)).CancelledAt);
        Assert.Equal([$"meeting-cancel:{w.LinkId}:1"], await KeysAsync(w.H, "meeting-cancel:"));
        // The attendees get a cancel of the whole meeting (series key), not of an occurrence of a series that never recurred.
        Assert.NotEmpty(await KeysAsync(w.H, $"cancel:{w.SeriesId}:series:"));
    }
}
