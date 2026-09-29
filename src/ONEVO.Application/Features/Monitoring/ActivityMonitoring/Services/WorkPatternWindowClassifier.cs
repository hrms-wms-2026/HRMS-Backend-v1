using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

public sealed record WorkPatternTotals(
    int FocusMinutes, int OtherActiveMinutes, int IdleMinutes, int MeetingBarMinutes,
    int ProductiveFocusMinutes, int ProductiveOtherActiveMinutes);

/// <summary>
/// Single source of truth for turning a day's raw ActivitySnapshot/MeetingSignal rows into
/// Focus/OtherActive/Idle/Meeting/Productive minutes. Used identically by the live "today"
/// query path (GetMyWorkPatternQueryHandler) and the nightly aggregation job
/// (ActivityDailySummaryAggregator) so the two paths can never compute different numbers for
/// the same underlying data - see WorkPatternLiveVsNightlyConsistencyTests.
///
/// Model summary (full derivation in docs/superpowers/plans/2026-09-25-work-pattern-productivity-model.md):
/// - A snapshot window that overlaps a meeting contributes its WHOLE duration (active+idle
///   both) to MeetingBar and is excluded from everything else below - meeting-ness doesn't
///   depend on whether you were typing.
/// - Duration allocation always uses ActiveSeconds/IdleSeconds specifically, never the raw
///   window width, since a single snapshot can be partially active and partially idle.
/// - Focus is a 30+ minute contiguous same-foreground-process streak among non-meeting windows.
/// - "Focus" does not imply "Productive" - a long streak in a Personal-classified app is real
///   Focus (a real behavioral/concentration signal) but doesn't count toward ProductiveFocus.
/// - "Unknown" app category is unclassified data, not a negative judgment - it simply doesn't
///   contribute to Productive, the same as it wouldn't contribute either way if we had no data
///   at all for that process.
/// </summary>
public static class WorkPatternWindowClassifier
{
    public const int FocusThresholdMinutes = 30;
    private static readonly TimeSpan MeetingSampleWindow = TimeSpan.FromMinutes(2);

    public static WorkPatternTotals Classify(
        IReadOnlyList<ActivitySnapshot> snapshots,
        IReadOnlyList<MeetingSignal> meetingSignals)
    {
        var meetingWindows = meetingSignals
            .Where(m => m.IsMeetingAppRunning)
            .Select(m => (Start: m.CapturedAt - MeetingSampleWindow, End: m.CapturedAt))
            .ToList();

        var ordered = snapshots
            .Where(s => s.ActiveSeconds + s.IdleSeconds > 0)
            .OrderBy(s => s.CapturedAt)
            .ToList();

        long meetingBarSeconds = 0, idleSeconds = 0, focusSeconds = 0, otherActiveSeconds = 0;
        long productiveFocusSeconds = 0, productiveOtherActiveSeconds = 0;

        // Windows NOT overlapping a meeting, in order - the population the focus-streak walk runs over.
        var nonMeetingWindows = new List<ActivitySnapshot>();

        foreach (var snap in ordered)
        {
            var windowDuration = TimeSpan.FromSeconds(snap.ActiveSeconds + snap.IdleSeconds);
            var windowStart = snap.CapturedAt - windowDuration;
            var windowEnd = snap.CapturedAt;

            var overlapsMeeting = meetingWindows.Any(m => windowStart < m.End && m.Start < windowEnd);
            if (overlapsMeeting)
            {
                meetingBarSeconds += snap.ActiveSeconds + snap.IdleSeconds;
                continue;
            }

            idleSeconds += snap.IdleSeconds;
            nonMeetingWindows.Add(snap);
        }

        // 30+ minute contiguous same-foreground-process streak, walked over non-meeting windows only.
        string? streakProcess = null;
        long streakActiveSeconds = 0;
        var streakWindows = new List<ActivitySnapshot>();

        void FlushStreak()
        {
            if (streakWindows.Count == 0)
                return;

            var minutes = streakActiveSeconds / 60;
            if (minutes >= FocusThresholdMinutes)
            {
                focusSeconds += streakActiveSeconds;
                foreach (var w in streakWindows)
                {
                    if (AppCategoryClassifier.Classify(w.ForegroundProcessName) == AppCategory.Productive)
                        productiveFocusSeconds += w.ActiveSeconds;
                }
            }
            else
            {
                otherActiveSeconds += streakActiveSeconds;
                foreach (var w in streakWindows)
                {
                    if (AppCategoryClassifier.Classify(w.ForegroundProcessName) == AppCategory.Productive)
                        productiveOtherActiveSeconds += w.ActiveSeconds;
                }
            }
            streakActiveSeconds = 0;
            streakWindows.Clear();
            streakProcess = null;
        }

        foreach (var snap in nonMeetingWindows)
        {
            if (snap.ActiveSeconds <= 0)
            {
                FlushStreak();
                continue;
            }

            var process = snap.ForegroundProcessName ?? string.Empty;
            if (streakProcess is null)
            {
                streakProcess = process;
            }
            else if (!string.Equals(streakProcess, process, StringComparison.OrdinalIgnoreCase))
            {
                FlushStreak();
                streakProcess = process;
            }

            streakActiveSeconds += snap.ActiveSeconds;
            streakWindows.Add(snap);
        }
        FlushStreak();

        return new WorkPatternTotals(
            FocusMinutes: (int)(focusSeconds / 60),
            OtherActiveMinutes: (int)(otherActiveSeconds / 60),
            IdleMinutes: (int)(idleSeconds / 60),
            MeetingBarMinutes: (int)(meetingBarSeconds / 60),
            ProductiveFocusMinutes: (int)(productiveFocusSeconds / 60),
            ProductiveOtherActiveMinutes: (int)(productiveOtherActiveSeconds / 60));
    }
}
