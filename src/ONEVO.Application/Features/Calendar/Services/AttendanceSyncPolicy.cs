using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>What one attendance fetch told us.</summary>
public enum AttendanceFetchOutcome
{
    /// <summary>Exception, non-2xx, missing connection or re-auth needed.</summary>
    Failed,
    /// <summary>Teams: no report yet. Zoom: 200 with 0 participants (can't tell "not ready" from "nobody joined").</summary>
    NotReady,
    /// <summary>Teams: a report exists (even with 0 records). Zoom: 200 with at least one participant.</summary>
    Final
}

/// <summary>Spec §3.2.1 classification, pure so both sync jobs (and their tests) share it.</summary>
public static class AttendanceSyncPolicy
{
    /// <summary>Reports appear some time after a meeting ends; don't ask before this.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(30);

    /// <summary>After this long past the event's end, stop retrying and record why.</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(24);

    /// <summary>The meeting's next attendance_sync_status, or null to change nothing and retry next run.</summary>
    public static string? NextStatus(AttendanceFetchOutcome outcome, DateTimeOffset eventEnd, DateTimeOffset now)
    {
        var givingUp = now >= eventEnd + GiveUpAfter;
        return outcome switch
        {
            AttendanceFetchOutcome.Final => CalendarAttendanceSyncStatuses.Synced,
            AttendanceFetchOutcome.NotReady => givingUp ? CalendarAttendanceSyncStatuses.Unavailable : null,
            _ => givingUp ? CalendarAttendanceSyncStatuses.Failed : null
        };
    }
}
