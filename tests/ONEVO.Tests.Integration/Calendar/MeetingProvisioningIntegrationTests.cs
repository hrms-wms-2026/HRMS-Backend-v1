using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CreateEventMeeting;
using ONEVO.Application.Features.Calendar.Commands.RemoveEventMeeting;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using Xunit;
using static ONEVO.Tests.Integration.Calendar.CalendarNotificationDb;

namespace ONEVO.Tests.Integration.Calendar;

public sealed class MeetingProvisioningIntegrationTests
{
    /// <summary>Teams double: counts creates, can block until released, and keeps one meeting per externalId (like createOrGet).</summary>
    private sealed class FakeTeams : ITeamsMeetingClient, ICalendarConnectionTokenProvider
    {
        private readonly object _gate = new();
        public int CreateCalls;
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Block;
        public Dictionary<string, TeamsMeetingDto> ByExternalId { get; } = [];
        public List<string> Cancelled { get; } = [];

        public async Task<TeamsMeetingDto> CreateOrGetMeetingAsync(string a, string externalId, string s, DateTimeOffset b, DateTimeOffset e, CancellationToken ct)
        {
            Interlocked.Increment(ref CreateCalls);
            if (Block) await Release.Task;
            lock (_gate)
            {
                if (!ByExternalId.TryGetValue(externalId, out var dto))
                    ByExternalId[externalId] = dto = new TeamsMeetingDto($"graph-{ByExternalId.Count + 1}", $"https://teams/j/{ByExternalId.Count + 1}", null, null);
                return dto;
            }
        }

        public Task CancelMeetingAsync(string a, string id, CancellationToken ct) { lock (_gate) Cancelled.Add(id); return Task.CompletedTask; }
        Task<TeamsAttendanceResult> ITeamsMeetingClient.GetAttendanceAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
        public Task<string?> GetFreshAccessTokenAsync(ExternalCalendarConnection c, string p, CancellationToken ct) => Task.FromResult<string?>("token");
        public Task<string?> ForceRefreshAccessTokenAsync(ExternalCalendarConnection c, string p, CancellationToken ct) => Task.FromResult<string?>("token");
    }

    private sealed class UnusedZoom : IZoomMeetingClient
    {
        public Task<ZoomMeetingDto> CreateMeetingAsync(string a, string s, DateTimeOffset b, DateTimeOffset e, string t, CancellationToken ct) => throw new InvalidOperationException("not used");
        public Task<ZoomMeetingDto?> FindMeetingByCorrelationTokenAsync(string a, string t, DateTimeOffset s, CancellationToken ct) => throw new InvalidOperationException("not used");
        public Task CancelMeetingAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
        public Task<IReadOnlyList<ZoomAttendanceRecordDto>> GetAttendanceAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
    }

    private static async Task<ExternalCalendarConnection> AddTeamsConnectionAsync(CalendarNotificationDb h)
    {
        await using var db = h.Context();
        var connection = new ExternalCalendarConnection
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.Organizer.UserId, Provider = CalendarExternalSources.OutlookCalendar,
            ExternalAccountEmail = "ada@example.com", RefreshTokenEncrypted = [1], Status = ExternalCalendarConnectionStatuses.Active,
            ScopesJson = """["OnlineMeetings.ReadWrite"]"""
        };
        db.ExternalCalendarConnections.Add(connection);
        await db.SaveChangesAsync();
        return connection;
    }

    private static (CreateEventMeetingCommandHandler Create, RemoveEventMeetingCommandHandler Remove) Handlers(
        CalendarNotificationDb h, CalendarHandlers s, FakeTeams teams)
    {
        var clock = new SystemDateTimeProvider();
        var meetings = new EfCalendarEventMeetingRepository(s.Db, clock);
        var connections = new EfExternalCalendarConnectionRepository(s.Db);
        var provisioner = new MeetingProvisioner(teams, teams, new UnusedZoom(), NullLogger<MeetingProvisioner>.Instance);
        var links = new EventMeetingLinkService(s.Events, meetings, connections, provisioner, s.Sender, s.UnitOfWork, clock);
        return (
            new CreateEventMeetingCommandHandler(h.OrganizerUser, s.Events, connections, meetings, links, s.Employees, clock, s.UnitOfWork),
            new RemoveEventMeetingCommandHandler(h.OrganizerUser, s.Events, meetings, connections, teams, teams, new UnusedZoom(), links, s.Employees, s.UnitOfWork));
    }

    [Fact]
    public async Task TwoConcurrentFirstCreates_OneProviderCall_TheOtherGetsInProgress()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        await AddTeamsConnectionAsync(h);
        var evt = await h.AddEventAsync([], []);
        var teams = new FakeTeams { Block = true };

        async Task<ONEVO.Application.Common.Models.Result<CreateEventMeetingResult>> CreateAsync()
        {
            await using var s = h.Scope();
            return await Handlers(h, s, teams).Create.Handle(new CreateEventMeetingCommand(evt.Id, CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);
        }

        var first = CreateAsync();
        var second = CreateAsync();
        await Task.Delay(500); // both have tried to claim
        teams.Release.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, teams.CreateCalls);
        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(results, r => r.Error == "meeting_link_in_progress");
    }

    [Fact]
    public async Task CreateRemoveCreateRemoveCreate_EveryLinkAndLinkRemovedEmailOncePerGeneration_RetriesSendNothing()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        await AddTeamsConnectionAsync(h);
        var participant = await h.AddEmployeeAsync("Grace", "Hopper");
        var evt = await h.AddEventAsync([participant], ["vendor@example.com"]);
        var teams = new FakeTeams();

        async Task CreateAsync()
        {
            await using var s = h.Scope();
            var result = await Handlers(h, s, teams).Create.Handle(new CreateEventMeetingCommand(evt.Id, CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        async Task RemoveAsync()
        {
            await using var s = h.Scope();
            Assert.True((await Handlers(h, s, teams).Remove.Handle(new RemoveEventMeetingCommand(evt.Id), CancellationToken.None)).IsSuccess);
        }

        await CreateAsync();
        await CreateAsync(); // retried request: the existing link, nothing new sent
        await RemoveAsync();
        await CreateAsync();
        await RemoveAsync();
        await CreateAsync();

        var rows = await h.OutboxAsync();
        var linkEmails = rows.Where(r => r.DeduplicationKey?.StartsWith("link:") == true).ToList();
        var removedEmails = Emails(rows, CalendarEmailKinds.LinkRemoved).ToList();
        Assert.Equal(6, linkEmails.Count);    // 3 generations × (participant + guest)
        Assert.Equal(4, removedEmails.Count); // 2 removals × (participant + guest)
        Assert.Equal(3, linkEmails.Select(r => r.DeduplicationKey!.Split(':')[2]).Distinct().Count());
        Assert.Equal(3, teams.ByExternalId.Count); // a new remote meeting per generation
        Assert.Equal(2, teams.Cancelled.Count);
    }

    [Fact]
    public async Task DeleteWithAnActiveMeeting_QueuesOneCancelMessage_AfterCommit()
    {
        var h = await CalendarNotificationDb.CreateAsync();
        await AddTeamsConnectionAsync(h);
        var evt = await h.AddEventAsync([], []);
        var teams = new FakeTeams();
        await using (var s = h.Scope())
            Assert.True((await Handlers(h, s, teams).Create.Handle(new CreateEventMeetingCommand(evt.Id, CalendarEventMeetingProviders.MicrosoftTeams), CancellationToken.None)).IsSuccess);

        await using (var s = h.Scope())
        {
            var clock = new SystemDateTimeProvider();
            var result = await new ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent.DeleteCalendarEventCommandHandler(
                    h.OrganizerUser, s.Events, s.Employees, s.Sender, new EfCalendarEventMeetingRepository(s.Db, clock),
                    new ONEVO.Infrastructure.Services.SharedPlatform.Outbox.OutboxWriter(s.Db, Encryption, clock), s.UnitOfWork)
                .Handle(new ONEVO.Application.Features.Calendar.Commands.DeleteCalendarEvent.DeleteCalendarEventCommand(evt.Id), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var cancel = Assert.Single((await h.OutboxAsync()).Where(r => r.Type == OutboxMessageTypes.CalendarMeetingCancel));
        Assert.Equal("graph-1", cancel.Payload.RootElement.GetProperty("ExternalMeetingId").GetString());
        Assert.Empty(teams.Cancelled); // not called inline
    }
}
