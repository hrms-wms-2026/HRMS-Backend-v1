using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Spec 5.3 cancel: one occurrence, this and following (plus UNTIL), or the whole series; idempotent; a past
/// occurrence cannot be cancelled.</summary>
public sealed class MeetingCancelIntegrationTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = First.AddDays(6);

    private sealed record World(MyTeamDb H, MeetingSeries Series, Guid Week0, Guid Week1, Guid Week2, Guid Organizer);

    private static async Task<World> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        var series = h.AddSeries(db, at, First, "FREQ=WEEKLY");
        h.AddSeriesParticipant(db, series, at.OrganizerEmployeeId, CalendarEventParticipantStatuses.Accepted);
        var (w0, _) = h.AddOccurrence(db, series, at, First);
        var (w1, _) = h.AddOccurrence(db, series, at, First.AddDays(7));
        var (w2, _) = h.AddOccurrence(db, series, at, First.AddDays(14));
        await db.SaveChangesAsync();
        return new World(h, series, w0.Id, w1.Id, w2.Id, at.OrganizerEmployeeId);
    }

    private static async Task<ONEVO.Application.Common.Models.Result<bool>> CancelAsync(World w, DateTimeOffset at, string scope)
    {
        await using var db = w.H.NewContext();
        return await new MeetingTestServices(db, new MeetingTestClock(Now)).Cancels.CancelAsync(
            w.H.TenantId, w.Organizer, Guid.NewGuid(), w.Series.Id, at, scope, CancellationToken.None);
    }

    private static async Task<Dictionary<Guid, MeetingDetails>> DetailsAsync(World w)
    {
        await using var db = w.H.NewContext();
        return await db.MeetingDetails.ToDictionaryAsync(d => d.TaskId);
    }

    [Fact]
    public async Task CancelThis_CancelsOnlyThatOccurrence_AndIsIdempotent()
    {
        var w = await SeedAsync();

        Assert.True((await CancelAsync(w, First.AddDays(7), MeetingScopes.This)).IsSuccess);
        Assert.True((await CancelAsync(w, First.AddDays(7), MeetingScopes.This)).IsSuccess);

        var details = await DetailsAsync(w);
        Assert.NotNull(details[w.Week1].CancelledAt);
        Assert.Null(details[w.Week2].CancelledAt);
    }

    [Fact]
    public async Task CancelFollowing_CancelsFromThePoint_AndEndsTheRule()
    {
        var w = await SeedAsync();

        Assert.True((await CancelAsync(w, First.AddDays(14), MeetingScopes.Following)).IsSuccess);

        var details = await DetailsAsync(w);
        Assert.Null(details[w.Week1].CancelledAt);
        Assert.NotNull(details[w.Week2].CancelledAt);
        await using var db = w.H.NewContext();
        Assert.Contains("UNTIL=20261116T085959Z", (await db.MeetingSeries.SingleAsync()).RecurrenceRule);
    }

    [Fact]
    public async Task CancelAll_CancelsTheSeriesAndEveryFutureOccurrence_NotThePast()
    {
        var w = await SeedAsync();

        Assert.True((await CancelAsync(w, First.AddDays(7), MeetingScopes.All)).IsSuccess);

        var details = await DetailsAsync(w);
        Assert.Null(details[w.Week0].CancelledAt);
        Assert.NotNull(details[w.Week1].CancelledAt);
        Assert.NotNull(details[w.Week2].CancelledAt);
        await using var db = w.H.NewContext();
        Assert.NotNull((await db.MeetingSeries.SingleAsync()).CancelledAt);
    }

    [Fact]
    public async Task APastOccurrence_CannotBeCancelled()
    {
        var w = await SeedAsync();

        var result = await CancelAsync(w, First, MeetingScopes.This);

        Assert.Equal((409, MeetingErrorCodes.OccurrenceStarted), (result.StatusCode, result.ErrorCode));
    }
}
