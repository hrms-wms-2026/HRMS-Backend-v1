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

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>The Plan C Task 7/8 attendance world: a weekly Teams meeting (organizer, one attendee, optional guests) with an active
/// link and its first three occurrences materialized, plus a provider session stub.</summary>
public static class MeetingAttendanceWorld
{
    public static readonly DateTimeOffset Now = new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset S0 = Now.AddDays(1);   // weekly at 06:00 UTC for 30 minutes

    public static DateTimeOffset Week(int k) => S0.AddDays(7 * k);
    public static DateTimeOffset End(int k) => Week(k).AddMinutes(30);

    /// <summary>A Teams session client whose answer the test sets; counts its calls.</summary>
    public sealed class SessionsStub : IMeetingAttendanceSessionClient
    {
        public Func<DateTimeOffset, DateTimeOffset, IReadOnlyList<ProviderAttendanceSession>> Answer { get; set; } = (_, _) => [];
        public List<(string MeetingId, DateTimeOffset From, DateTimeOffset To)> Calls { get; } = [];
        public string Provider => CalendarEventMeetingProviders.MicrosoftTeams;

        public Task<IReadOnlyList<ProviderAttendanceSession>> ListSessionsAsync(string accessToken, string externalMeetingId, DateTimeOffset from,
            DateTimeOffset to, CancellationToken ct)
        {
            Calls.Add((externalMeetingId, from, to));
            return Task.FromResult(Answer(from, to).Where(s => s.Start >= from && s.Start <= to).ToList() as IReadOnlyList<ProviderAttendanceSession>);
        }
    }

    public sealed class ProvisionerStub : IMeetingProvisioner
    {
        public Task<MeetingProvisioningOutcome> ProvisionAsync(MeetingProvisioningRequest request, CancellationToken ct)
            => Task.FromResult(new MeetingProvisioningOutcome(MeetingProvisioningOutcomeKind.Created,
                new ProvisionedMeeting("ext-1", "https://teams.example/join/1", "https://teams.example/org/1", null), null));

        public Task<bool> ResolveForRemovalAsync(MeetingProvisioningRequest request, CancellationToken ct) => Task.FromResult(true);
    }

    public sealed record World(MyTeamDb H, Guid Organizer, Guid OrganizerUser, Guid Attendee, string AttendeeEmail, Guid SeriesId, Guid LinkId,
        IReadOnlyList<Guid> Tasks);

    /// <summary>A weekly Teams meeting with an active link and its first three occurrences materialized.</summary>
    public static async Task<World> SeedAsync(params string[] guests)
    {
        var h = await MyTeamDb.CreateAsync();
        Guid organizerId, organizerUser, attendee;
        string attendeeEmail;
        MeetingPlacement at;
        await using (var db = h.NewContext())
        {
            at = h.AddPlacement(db);
            var organizer = h.AddEmployee(db);
            (organizerId, organizerUser) = (organizer.Id, organizer.UserId);
            var person = h.AddEmployee(db);
            (attendee, attendeeEmail) = (person.Id, person.Email!);
            db.ExternalCalendarConnections.Add(new ExternalCalendarConnection
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = organizer.UserId, Provider = CalendarExternalSources.OutlookCalendar,
                ExternalAccountEmail = "lead@contoso.example", ScopesJson = "[\"OnlineMeetings.ReadWrite\"]",
                Status = ExternalCalendarConnectionStatuses.Active, CreatedAt = Now,
            });
            await db.SaveChangesAsync();
        }

        Guid seriesId, linkId;
        await using (var db = h.NewContext())
        {
            var s = new MeetingTestServices(db, new MeetingTestClock(Now));
            var created = await s.UnitOfWork.ExecuteInTransactionAsync(ct => s.Factory.CreateDirectAsync(h.TenantId, organizerId, organizerUser,
                new ONEVO.Application.Features.WorkManagement.Meetings.Services.MeetingPlacement(at.ProjectId, at.ObjectiveId, false, true),
                new CreateMeetingRequest(at.ProjectId, at.ObjectiveId, "Weekly sync", null, null, false, S0, 30, false, "UTC",
                    "FREQ=WEEKLY", [attendee], guests, CalendarEventMeetingProviders.MicrosoftTeams),
                MeetingCreationPaths.Direct, null, ct));
            Assert.True(created.IsSuccess, created.Error);
            (seriesId, linkId) = (created.Value!.SeriesId!.Value, created.Value.Link!.LinkId!.Value);
        }
        await using (var db = h.NewContext())
        {
            var s = new MeetingTestServices(db, new MeetingTestClock(Now));
            await s.LinkService(new ProvisionerStub()).ProvisionAsync(h.TenantId, linkId, seriesId, recovery: false, CancellationToken.None);
            var series = (await s.Meetings.GetSeriesAsync(h.TenantId, seriesId))!;
            foreach (var k in new[] { 1, 2 })
                await s.UnitOfWork.ExecuteInTransactionAsync(async ct =>
                {
                    await s.Writer.LockSeriesAsync(h.TenantId, seriesId, ct);
                    return await s.Materializer.EnsureAsync(h.TenantId, series, Week(k), organizerUser, ct);
                }, CancellationToken.None);
        }
        await using var check = h.NewContext();
        var tasks = new List<Guid>();
        foreach (var k in new[] { 0, 1, 2 })
            tasks.Add((await check.MeetingDetails.SingleAsync(d => d.SeriesId == seriesId && d.OriginalStart == Week(k))).TaskId);
        return new World(h, organizerId, organizerUser, attendee, attendeeEmail, seriesId, linkId, tasks);
    }

    public static ProviderAttendanceSession Session(string id, DateTimeOffset start, params ProviderAttendanceRecord[] records)
        => new(id, start, start.AddMinutes(30), records);

    public static ProviderAttendanceRecord Rec(string key, string? email, DateTimeOffset joined, int minutes)
        => new(key, "Someone", email, joined, joined.AddMinutes(minutes));

    public static async Task<MeetingAttendanceSyncOutcome> SyncAsync(World w, SessionsStub stub, DateTimeOffset at)
    {
        await using var db = w.H.NewContext();
        var s = new MeetingTestServices(db, new MeetingTestClock(at));
        return await s.AttendanceSync(stub).SyncLinkAsync(w.H.TenantId, w.LinkId, at, CancellationToken.None);
    }

    public static async Task<MeetingDetails> DetailsAsync(World w, int k)
    {
        await using var db = w.H.NewContext();
        return await db.MeetingDetails.AsNoTracking().SingleAsync(d => d.TaskId == w.Tasks[k]);
    }
}
