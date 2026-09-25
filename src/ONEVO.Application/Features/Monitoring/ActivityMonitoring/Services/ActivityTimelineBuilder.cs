using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

/// <summary>
/// Classifies a day's activity snapshots into Focus/Idle timeline segments with real
/// start/end boundaries, for self-service display. Uses the same 30-minute threshold as
/// WorkPatternWindowClassifier (which computes the authoritative Focus/Meeting/Productive
/// totals) but is a separate implementation because this one needs interval boundaries for
/// the Today's Activity timeline visualization, not just totals.
/// </summary>
public static class ActivityTimelineBuilder
{
    public const string FocusType = "focus";
    public const string IdleType = "idle";
    public const string MeetingType = "meeting";

    /// <summary>Minimum contiguous active minutes to count as focus.</summary>
    public const int FocusThresholdMinutes = WorkPatternWindowClassifier.FocusThresholdMinutes;

    public static IReadOnlyList<ActivityTimelineSegmentDto> BuildSegments(
        IReadOnlyList<ActivitySnapshot> snapshots,
        IReadOnlyList<MeetingSignal>? meetingSignals = null)
    {
        var meetingWindows = (meetingSignals ?? [])
            .Where(m => m.IsMeetingAppRunning)
            .Select(m => (Start: m.CapturedAt - TimeSpan.FromMinutes(2), End: m.CapturedAt))
            .ToList();

        var ordered = snapshots
            .Where(s => s.ActiveSeconds + s.IdleSeconds > 0)
            .OrderBy(s => s.CapturedAt)
            .ToList();

        var segments = new List<ActivityTimelineSegmentDto>();
        DateTimeOffset? streakStart = null;
        var streakEnd = default(DateTimeOffset);
        var streakActive = false;
        string streakProcess = string.Empty;

        void FlushStreak()
        {
            if (streakStart is null)
                return;

            var minutes = (streakEnd - streakStart.Value).TotalMinutes;
            var type = streakActive && minutes >= FocusThresholdMinutes ? FocusType : IdleType;
            segments.Add(new ActivityTimelineSegmentDto(streakStart.Value, streakEnd, type));
            streakStart = null;
        }

        foreach (var snapshot in ordered)
        {
            var duration = TimeSpan.FromSeconds(snapshot.ActiveSeconds + snapshot.IdleSeconds);
            var start = snapshot.CapturedAt - duration;
            var end = snapshot.CapturedAt;

            if (meetingWindows.Any(m => start < m.End && m.Start < end))
            {
                FlushStreak();
                segments.Add(new ActivityTimelineSegmentDto(start, end, MeetingType));
                continue;
            }

            var isActive = snapshot.ActiveSeconds > 0;
            var process = snapshot.ForegroundProcessName ?? string.Empty;

            var continuesStreak = streakStart is not null
                && isActive == streakActive
                && (!isActive || string.Equals(process, streakProcess, StringComparison.OrdinalIgnoreCase));

            if (continuesStreak)
            {
                streakEnd = end;
                continue;
            }

            FlushStreak();
            streakStart = start;
            streakEnd = end;
            streakActive = isActive;
            streakProcess = process;
        }

        FlushStreak();
        return segments;
    }
}
