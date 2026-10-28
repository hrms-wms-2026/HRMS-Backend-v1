using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Spec 5.8: a provider session belongs to the occurrence whose window [start - 60m, end] contains its start
/// (nearest start wins, the earlier on a tie); a person's time is the union of their intervals.</summary>
public sealed class MeetingAttendanceMatcherTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Before = TimeSpan.FromMinutes(60);
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static (Guid, DateTimeOffset, DateTimeOffset) Occ(Guid id, DateTimeOffset start, int minutes = 30) => (id, start, start.AddMinutes(minutes));

    [Fact]
    public void Assign_WindowIncludesSixtyMinutesBefore()
    {
        var result = MeetingAttendanceMatcher.Assign(
            [("early", Monday.AddMinutes(-60)), ("too-early", Monday.AddMinutes(-61)), ("at-end", Monday.AddMinutes(30)), ("after", Monday.AddMinutes(31))],
            [Occ(A, Monday)], Before);

        Assert.Equal(A, result["early"]);
        Assert.Equal(A, result["at-end"]);
        Assert.False(result.ContainsKey("too-early"));
        Assert.False(result.ContainsKey("after"));
    }

    [Fact]
    public void Assign_NearestStartWins_EarlierOnTie()
    {
        // A at 09:00-10:00, B at 10:00-10:30: a session at 09:50 is in both windows; B's start is nearer.
        var occurrences = new[] { Occ(A, Monday, 60), Occ(B, Monday.AddHours(1)) };

        var nearest = MeetingAttendanceMatcher.Assign([("s", Monday.AddMinutes(50))], occurrences, Before);
        var tie = MeetingAttendanceMatcher.Assign([("t", Monday.AddMinutes(30))], occurrences, Before);

        Assert.Equal(B, nearest["s"]);
        Assert.Equal(A, tie["t"]);   // 30 min from both starts: the earlier occurrence
    }

    [Fact]
    public void Assign_TwoSessionsInOneOccurrence()
    {
        var result = MeetingAttendanceMatcher.Assign([("first", Monday.AddMinutes(1)), ("restart", Monday.AddMinutes(12))],
            [Occ(A, Monday), Occ(B, Monday.AddDays(7))], Before);

        Assert.Equal((A, A), (result["first"], result["restart"]));
    }

    [Fact]
    public void Assign_NoMatchStaysUnassigned()
    {
        var result = MeetingAttendanceMatcher.Assign([("adhoc", Monday.AddDays(3))], [Occ(A, Monday), Occ(B, Monday.AddDays(7))], Before);

        Assert.Empty(result);
    }

    [Fact]
    public void Union_MergesOverlapsAndRejoins()
    {
        var total = MeetingAttendanceMatcher.Union(
        [
            (Monday, Monday.AddMinutes(10)),
            (Monday.AddMinutes(5), Monday.AddMinutes(15)),    // overlaps the first: 09:00-09:15
            (Monday.AddMinutes(20), Monday.AddMinutes(25)),   // a rejoin after a gap
            (Monday.AddMinutes(25), Monday.AddMinutes(27)),   // touches the previous one
        ], Monday.AddMinutes(30));

        Assert.Equal(TimeSpan.FromMinutes(22), total);
    }

    [Fact]
    public void Union_OpenIntervalEndsAtOccurrenceEnd()
    {
        var total = MeetingAttendanceMatcher.Union([(Monday.AddMinutes(10), (DateTimeOffset?)null)], Monday.AddMinutes(30));
        var afterTheEnd = MeetingAttendanceMatcher.Union([(Monday.AddMinutes(40), (DateTimeOffset?)null)], Monday.AddMinutes(30));

        Assert.Equal(TimeSpan.FromMinutes(20), total);
        Assert.Equal(TimeSpan.Zero, afterTheEnd);
    }
}
