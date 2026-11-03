using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec 5.7 series-owned links: one claim per series under the series lock, references inherited on split and
/// counted, released one series at a time, and the materialized half of the "needed" rule.</summary>
public sealed class MeetingLinkRepositoryIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    private sealed record World(MyTeamDb H, MeetingPlacement At, MeetingSeries Series);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var series = h.AddSeries(db, at, Now.AddDays(1), "FREQ=WEEKLY");
        await db.SaveChangesAsync();
        return new World(h, at, series);
    }

    private static async Task<Guid?> ClaimAsync(MyTeamDb h, Guid seriesId, TimeSpan? holdLockFor = null)
    {
        await using var db = h.NewContext();
        return await new ONEVO.Infrastructure.Persistence.UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
        {
            await new EfMeetingOccurrenceWriter(db).LockSeriesAsync(h.TenantId, seriesId, ct);
            var id = await new EfMeetingLinkRepository(db).TryClaimForSeriesAsync(h.TenantId, seriesId, Guid.NewGuid(),
                CalendarEventMeetingProviders.MicrosoftTeams, invitesPending: true, Now, ct);
            await db.SaveChangesAsync(ct);
            if (holdLockFor is { } hold)
                await Task.Delay(hold, ct);
            return id;
        });
    }

    private static async Task<MeetingSeries> AddSplitAsync(World w)
    {
        await using var db = w.H.NewContext();
        var target = w.H.AddSeries(db, w.At, Now.AddDays(8), "FREQ=WEEKLY");
        target.SplitFromSeriesId = w.Series.Id;
        await db.SaveChangesAsync();
        return target;
    }

    [Fact]
    public async Task TwoConcurrentClaimsForOneSeries_ExactlyOneWins()
    {
        var w = await SeedAsync();

        var results = await Task.WhenAll(ClaimAsync(w.H, w.Series.Id, TimeSpan.FromMilliseconds(300)), ClaimAsync(w.H, w.Series.Id));

        Assert.Single(results, r => r is not null);
        await using var check = w.H.NewContext();
        var link = await check.CalendarEventMeetings.SingleAsync();
        Assert.Equal((CalendarMeetingOwnerKinds.MeetingSeries, (Guid?)null, CalendarEventMeetingStatuses.Provisioning, 1, true),
            (link.OwnerKind, link.CalendarEventId, link.Status, link.LinkGeneration, link.InvitesPending));
        var reference = await check.MeetingSeriesLinks.SingleAsync();
        Assert.Equal((w.Series.Id, link.Id, MeetingSeriesLinkOrigins.Created), (reference.SeriesId, reference.CalendarEventMeetingId, reference.Origin));
    }

    [Fact]
    public async Task AnInheritedReference_SharesTheLink_AndReleasingOneLeavesTheOther()
    {
        var w = await SeedAsync();
        var linkId = (await ClaimAsync(w.H, w.Series.Id))!.Value;
        var split = await AddSplitAsync(w);

        await using (var db = w.H.NewContext())
        {
            Assert.Equal(linkId, await new EfMeetingLinkRepository(db).AddInheritedReferenceAsync(w.H.TenantId, w.Series.Id, split.Id, Now));
            await db.SaveChangesAsync();
        }
        await using (var db = w.H.NewContext())
        {
            var links = new EfMeetingLinkRepository(db);
            Assert.Equal(new[] { w.Series.Id, split.Id }.Order(), (await links.ListActiveReferencingSeriesAsync(w.H.TenantId, linkId)).Order());
            Assert.Equal(linkId, (await links.GetActiveForSeriesAsync(w.H.TenantId, split.Id))!.Id);
            Assert.Equal(linkId, await links.ReleaseAsync(w.H.TenantId, w.Series.Id, Now));
        }

        await using var check = w.H.NewContext();
        var repo = new EfMeetingLinkRepository(check);
        Assert.Equal([split.Id], await repo.ListActiveReferencingSeriesAsync(w.H.TenantId, linkId));
        Assert.Null(await repo.GetActiveForSeriesAsync(w.H.TenantId, w.Series.Id));
        Assert.Null(await repo.ReleaseAsync(w.H.TenantId, w.Series.Id, Now));
        Assert.Equal(MeetingSeriesLinkOrigins.InheritedOnSplit,
            (await check.MeetingSeriesLinks.SingleAsync(l => l.SeriesId == split.Id)).Origin);
    }

    [Fact]
    public async Task InheritingFromASeriesWithoutALink_StagesNothing()
    {
        var w = await SeedAsync();
        var split = await AddSplitAsync(w);

        await using var db = w.H.NewContext();
        Assert.Null(await new EfMeetingLinkRepository(db).AddInheritedReferenceAsync(w.H.TenantId, w.Series.Id, split.Id, Now));
        await db.SaveChangesAsync();
        Assert.False(await db.MeetingSeriesLinks.AnyAsync());
    }

    [Fact]
    public async Task SetOccurrenceLink_TouchesOnlyOccurrencesFromTheGivenStart()
    {
        var w = await SeedAsync();
        var linkId = (await ClaimAsync(w.H, w.Series.Id))!.Value;
        Guid early, late;
        await using (var db = w.H.NewContext())
        {
            early = w.H.AddOccurrence(db, w.Series, w.At, Now.AddDays(1)).Task.Id;
            late = w.H.AddOccurrence(db, w.Series, w.At, Now.AddDays(8)).Task.Id;
            await db.SaveChangesAsync();
        }

        await using (var db = w.H.NewContext())
            await new EfMeetingLinkRepository(db).SetOccurrenceLinkAsync(w.H.TenantId, w.Series.Id, linkId, Now.AddDays(2), TimeSpan.FromMinutes(30));

        await using var check = w.H.NewContext();
        var details = await check.MeetingDetails.ToDictionaryAsync(d => d.TaskId);
        Assert.Equal(((Guid?)null, (Guid?)linkId), (details[early].CalendarEventMeetingId, details[late].CalendarEventMeetingId));
    }

    public static TheoryData<string, bool> NeedCases => new()
    {
        { "upcoming", true },
        { "upcoming_cancelled", false },
        { "ended_unfinalized", true },
        { "ended_finalized_in_window", true },
        { "ended_finalized_window_closed", false },
    };

    [Theory]
    [MemberData(nameof(NeedCases))]
    public async Task AnyOccurrenceNeedsLink_FollowsTheNeededRule(string shape, bool expected)
    {
        var w = await SeedAsync();
        var linkId = (await ClaimAsync(w.H, w.Series.Id))!.Value;
        await using (var db = w.H.NewContext())
        {
            var start = shape.StartsWith("upcoming") ? Now.AddDays(1) : shape.EndsWith("window_closed") ? Now.AddDays(-9) : Now.AddDays(-2);
            var (_, d) = w.H.AddOccurrence(db, w.Series, w.At, start);
            d.CalendarEventMeetingId = linkId;
            if (shape == "upcoming_cancelled")
                d.CancelledAt = Now.AddDays(-1);
            if (shape.StartsWith("ended_finalized"))
                d.AttendanceFinalizedAt = d.EndAt.AddHours(24);
            await db.SaveChangesAsync();
        }

        await using var check = w.H.NewContext();
        Assert.Equal(expected, await new EfMeetingLinkRepository(check).AnyOccurrenceNeedsLinkAsync(w.H.TenantId, linkId, Now, Window));
    }
}
