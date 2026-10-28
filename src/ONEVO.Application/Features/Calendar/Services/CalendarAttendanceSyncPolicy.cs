namespace ONEVO.Application.Features.Calendar.Services;

public enum CalendarAttendanceSyncOutcome
{
    /// <summary>The report had rows: store them and stamp the meeting synced.</summary>
    Synced,
    /// <summary>No rows yet (Teams/Zoom publish reports late): leave the meeting due and try again next run.</summary>
    RetryLater,
    /// <summary>Still no rows <see cref="CalendarAttendanceSyncPolicy.AttendanceReportCutoffHours"/> after the end: stop and mark it unavailable.</summary>
    Unavailable,
    /// <summary>The fetch itself failed and the cutoff has passed: stop and mark the sync failed ("unknown").</summary>
    Failed,
}

/// <summary>An event's scheduled start and end - the window its provider attendance session must start in.</summary>
public sealed record CalendarEventWindow(DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// What the Teams/Zoom attendance sync jobs do with one report (plan C0 P0-2). An empty report is never treated as
/// "synced" - providers publish attendance late - it is retried until the cutoff after the meeting's end.
/// </summary>
public static class CalendarAttendanceSyncPolicy
{
    public const int AttendanceReportCutoffHours = 24;

    public static CalendarAttendanceSyncOutcome Decide(int recordCount, DateTimeOffset? eventEnd, DateTimeOffset now)
    {
        if (recordCount > 0)
            return CalendarAttendanceSyncOutcome.Synced;
        return eventEnd is { } end && now >= end.AddHours(AttendanceReportCutoffHours)
            ? CalendarAttendanceSyncOutcome.Unavailable
            : CalendarAttendanceSyncOutcome.RetryLater;
    }

    /// <summary>The fetch failed (provider error, no connection, sign-in needed): retried until the cutoff,
    /// then recorded as failed so the meeting stops being polled forever.</summary>
    public static CalendarAttendanceSyncOutcome DecideAfterFailure(DateTimeOffset eventEnd, DateTimeOffset now)
        => now >= eventEnd.AddHours(AttendanceReportCutoffHours)
            ? CalendarAttendanceSyncOutcome.Failed
            : CalendarAttendanceSyncOutcome.RetryLater;
}
