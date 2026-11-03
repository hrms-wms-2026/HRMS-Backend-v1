using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Tests.Integration.Calendar;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Plan C Task 4 (spec 5.7, P1-5): a meeting created with a platform claims a series-owned link and queues its
/// provisioning; the link email doubles as the invite; a failed link sends the invites without one, once; a split inherits
/// the link with no provider call and no email; every occurrence carries the series' link.</summary>
public sealed class MeetingLinkProvisioningIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);

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

    private static MeetingProvisioningOutcome Created() => new(MeetingProvisioningOutcomeKind.Created,
        new ProvisionedMeeting("ext-1", "https://teams.example/join/1", "https://teams.example/org/1", null), null);

    private sealed record World(MyTeamDb H, MeetingPlacement At, Guid Organizer, Guid OrganizerUser, Guid Attendee);

    private static async Task<World> SeedAsync(bool connected = true)
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var organizer = h.AddEmployee(db);
        var attendee = h.AddEmployee(db);
        if (connected)
            db.ExternalCalendarConnections.Add(new ExternalCalendarConnection
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = organizer.UserId, Provider = CalendarExternalSources.OutlookCalendar,
                ExternalAccountEmail = "lead@contoso.example", ScopesJson = "[\"OnlineMeetings.ReadWrite\"]",
                Status = ExternalCalendarConnectionStatuses.Active, CreatedAt = Now,
            });
        await db.SaveChangesAsync();
        return new World(h, at with { OrganizerEmployeeId = organizer.Id }, organizer.Id, organizer.UserId, attendee.Id);
    }

    private static async Task<MeetingCreatedResponse> CreateAsync(World w, string? platform = CalendarEventMeetingProviders.MicrosoftTeams,
        string? rule = "FREQ=WEEKLY")
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(Now));
        var created = await s.UnitOfWork.ExecuteInTransactionAsync(ct => s.Factory.CreateDirectAsync(w.H.TenantId, w.Organizer, w.OrganizerUser,
            new ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingPlacement(w.At.ProjectId, w.At.ObjectiveId, false, true),
            new CreateMeetingRequest(w.At.ProjectId, w.At.ObjectiveId, "Design review", null, null, false, Now.AddDays(1), 30, false, "UTC",
                rule, [w.Attendee], ["client@example.com"], platform),
            MeetingCreationPaths.Direct, null, ct));
        Assert.True(created.IsSuccess, created.Error);
        return created.Value!;
    }

    private static async Task<ONEVO.Application.Common.Models.Result<MeetingLinkResponse>> ProvisionAsync(World w, Guid linkId, Guid seriesId,
        ProvisionerStub provisioner, DateTimeOffset? at = null)
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(at ?? Now));
        return await s.LinkService(provisioner).ProvisionAsync(w.H.TenantId, linkId, seriesId, recovery: false, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<(string Type, string? Key)>> OutboxAsync(MyTeamDb h)
    {
        await using var db = h.NewContext();
        return (await db.Set<OutboxMessage>().AsNoTracking().Where(m => m.TenantId == h.TenantId).OrderBy(m => m.CreatedAt).ToListAsync())
            .Select(m => (m.Type, m.DeduplicationKey)).ToList();
    }

    private static IReadOnlyList<string?> Keys(IEnumerable<(string Type, string? Key)> rows, string prefix)
        => rows.Where(r => r.Key?.StartsWith(prefix) == true).Select(r => r.Key).Order().ToList();

    [Fact]
    public async Task CreateWithTeams_ClaimsALink_HoldsTheInvites_AndQueuesOneProvisionMessage()
    {
        var w = await SeedAsync();

        var created = await CreateAsync(w);

        Assert.Equal(CalendarEventMeetingStatuses.Provisioning, created.Link!.Status);
        var outbox = await OutboxAsync(w.H);
        Assert.Empty(Keys(outbox, "invite:"));
        Assert.Equal([$"meeting-link-provision:{created.Link.LinkId}:1"], Keys(outbox, "meeting-link-provision:"));
        await using var check = w.H.NewContext();
        Assert.All(await check.MeetingDetails.Where(d => d.SeriesId == created.SeriesId).ToListAsync(),
            d => Assert.Equal(created.Link.LinkId, d.CalendarEventMeetingId));
    }

    [Fact]
    public async Task ProvisionCreated_ActivatesTheLink_AndEmailsItAsTheInvite()
    {
        var w = await SeedAsync();
        var created = await CreateAsync(w);
        var provisioner = new ProvisionerStub(Created());

        var result = await ProvisionAsync(w, created.Link!.LinkId!.Value, created.SeriesId!.Value, provisioner);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(("https://teams.example/join/1", CalendarEventMeetingStatuses.Active), (result.Value!.JoinUrl, result.Value.Status));
        Assert.Equal((created.Link.LinkId.Value, 1), (provisioner.Calls.Single().EventId, provisioner.Calls.Single().Generation));
        await using var check = w.H.NewContext();
        var rows = await check.CalendarEventParticipants.Where(p => p.MeetingSeriesId == created.SeriesId && p.EmployeeId == w.Attendee).Select(p => p.Id).SingleAsync();
        var guest = await check.CalendarEventGuests.SingleAsync(g => g.MeetingSeriesId == created.SeriesId);
        var outbox = await OutboxAsync(w.H);
        Assert.Equal(new[] { CalendarEmailKeys.Link(created.Link.LinkId.Value, 1, rows), CalendarEmailKeys.Link(created.Link.LinkId.Value, 1, guest.Id) }.Order(),
            Keys(outbox, "link:"));
        Assert.Empty(Keys(outbox, "invite:"));
        Assert.False((await check.CalendarEventMeetings.SingleAsync()).InvitesPending);
    }

    [Fact]
    public async Task ProvisionFailed_SendsTheInvitesWithoutALinkOnce_AndNotifiesTheOrganizer()
    {
        var w = await SeedAsync();
        var created = await CreateAsync(w);
        var provisioner = new ProvisionerStub(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Permanent, null, "rejected"));

        var first = await ProvisionAsync(w, created.Link!.LinkId!.Value, created.SeriesId!.Value, provisioner);
        await ProvisionAsync(w, created.Link.LinkId.Value, created.SeriesId.Value, provisioner);

        Assert.Equal((CalendarEventMeetingStatuses.Failed, "rejected"), (first.Value!.Status, first.Value.FailureReason));
        Assert.Single(provisioner.Calls);
        var outbox = await OutboxAsync(w.H);
        Assert.Equal(2, Keys(outbox, "invite:").Count);
        Assert.Empty(Keys(outbox, "link:"));
        await using var check = w.H.NewContext();
        var notices = await check.Set<OutboxMessage>().AsNoTracking().Where(m => m.Type == OutboxMessageTypes.WorkNotification).ToListAsync();
        Assert.Contains(notices, m => CalendarNotificationDb.Encryption.Decrypt(m.EncryptedPayload).Contains(CalendarMeetingNoticeCodes.LinkFailed));
    }

    [Fact]
    public async Task ProvisionAmbiguous_IsProvisioningUnknown_WithTheFallbackInvites()
    {
        var w = await SeedAsync();
        var created = await CreateAsync(w);

        var result = await ProvisionAsync(w, created.Link!.LinkId!.Value, created.SeriesId!.Value,
            new ProvisionerStub(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Ambiguous, null, "timeout_or_server_error")));

        Assert.Equal(CalendarEventMeetingStatuses.ProvisioningUnknown, result.Value!.Status);
        Assert.Equal(2, Keys(await OutboxAsync(w.H), "invite:").Count);
    }

    [Fact]
    public async Task OrganizerNotConnected_CreatesWithoutALink_AndInvitesAtOnce()
    {
        var w = await SeedAsync(connected: false);

        var created = await CreateAsync(w);

        Assert.Equal((null, CalendarMeetingLinkStates.NotConnected), (created.Link!.LinkId, created.Link.Status));
        Assert.Equal(2, Keys(await OutboxAsync(w.H), "invite:").Count);
        await using var check = w.H.NewContext();
        Assert.False(await check.CalendarEventMeetings.AnyAsync());
    }

    [Fact]
    public async Task ASeriesCancelledDuringTheCall_GetsNoLink_AndTheRemoteMeetingIsCancelled()
    {
        var w = await SeedAsync();
        var created = await CreateAsync(w);
        await using (var db = w.H.NewContext())
        {
            (await db.MeetingSeries.SingleAsync()).CancelledAt = Now;
            await db.SaveChangesAsync();
        }

        var result = await ProvisionAsync(w, created.Link!.LinkId!.Value, created.SeriesId!.Value, new ProvisionerStub(Created()));

        Assert.Equal(CalendarEventMeetingStatuses.Cancelled, result.Value!.Status);
        var outbox = await OutboxAsync(w.H);
        Assert.Equal([$"meeting-cancel:{created.Link.LinkId}:1"], Keys(outbox, "meeting-cancel:"));
        Assert.Empty(Keys(outbox, "link:"));
    }

    [Fact]
    public async Task ASplit_InheritsTheLink_WithNoProviderCallAndNoLinkEmail()
    {
        var w = await SeedAsync();
        var created = await CreateAsync(w);
        await ProvisionAsync(w, created.Link!.LinkId!.Value, created.SeriesId!.Value, new ProvisionerStub(Created()));

        await using (var db = w.H.NewContext())
        {
            var edit = await new MeetingTestServices(db, new MeetingTestClock(Now)).Edits.EditAsync(w.H.TenantId, w.Organizer, w.OrganizerUser,
                created.SeriesId.Value, Now.AddDays(1), new EditMeetingRequest(MeetingScopes.Following, 0, "Design review v2", null, null, null, null,
                    null, null, null, null), CancellationToken.None);
            Assert.True(edit.IsSuccess, edit.Error);
        }

        await using var check = w.H.NewContext();
        var target = await check.MeetingSeries.SingleAsync(s => s.SplitFromSeriesId == created.SeriesId);
        Assert.Equal(MeetingSeriesLinkOrigins.InheritedOnSplit,
            (await check.MeetingSeriesLinks.SingleAsync(l => l.SeriesId == target.Id && l.ReleasedAt == null)).Origin);
        Assert.All(await check.MeetingDetails.Where(d => d.SeriesId == target.Id).ToListAsync(), d => Assert.Equal(created.Link.LinkId, d.CalendarEventMeetingId));
        Assert.Single(await check.CalendarEventMeetings.ToListAsync());
        Assert.Equal(2, Keys(await OutboxAsync(w.H), "link:").Count);   // only the original link email
    }

    private static async Task HandleAsync(World w, Guid linkId, Guid seriesId, int generation, ProvisionerStub provisioner, DateTimeOffset at)
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(at));
        var handler = new ONEVO.Application.Features.WorkManagement.Meetings.OutboxHandlers.MeetingLinkProvisionOutboxHandler(
            s.LinkService(provisioner), s.Links, s.Clock);
        await handler.HandleAsync(JsonSerializer.Serialize(new MeetingLinkProvisionPayload(w.H.TenantId, linkId, seriesId, generation)), CancellationToken.None);
    }

    [Fact]
    public async Task TheHandler_IgnoresAMessageForAnotherGeneration()
    {
        var w = await SeedAsync();
        var created = await CreateAsync(w);
        var provisioner = new ProvisionerStub(Created());

        await HandleAsync(w, created.Link!.LinkId!.Value, created.SeriesId!.Value, generation: 2, provisioner, Now);

        Assert.Empty(provisioner.Calls);
    }

    [Fact]
    public async Task TheHandler_RunsInRecoveryMode_WhenTheClaimIsOld()
    {
        var w = await SeedAsync();
        var created = await CreateAsync(w);
        var fresh = new ProvisionerStub(Created());
        var late = new ProvisionerStub(Created());

        await HandleAsync(w, created.Link!.LinkId!.Value, created.SeriesId!.Value, 1, fresh, Now.AddSeconds(5));
        Assert.False(fresh.Calls.Single().Recovery);
        await using (var db = w.H.NewContext())
        {
            var link = await db.CalendarEventMeetings.SingleAsync();
            link.Status = CalendarEventMeetingStatuses.Provisioning;
            link.ProvisioningStartedAt = Now;
            await db.SaveChangesAsync();
        }
        await HandleAsync(w, created.Link.LinkId.Value, created.SeriesId.Value, 1, late, Now.AddMinutes(5));

        Assert.True(late.Calls.Single().Recovery);
    }
}
