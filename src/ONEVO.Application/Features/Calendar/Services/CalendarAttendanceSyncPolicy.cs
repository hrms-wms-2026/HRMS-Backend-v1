namespace ONEVO.Application.Features.Calendar.Services;

public enum CalendarAttendanceSyncOutcome
{
    /// <summary>The report had rows: store them and stamp the meeting synced.</summary>
    Synced,
    /// <summary>No rows yet (Teams/Zoom publish reports late): leave the meeting due and try again next run.</summary>
    RetryLater,
    /// <summary>Still no rows <see cref="CalendarAttendanceSyncPolicy.AttendanceReportCutoffHours"/> after the end: stop and mark it unavailable.</summary>
    Unavailable,
}

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
}
