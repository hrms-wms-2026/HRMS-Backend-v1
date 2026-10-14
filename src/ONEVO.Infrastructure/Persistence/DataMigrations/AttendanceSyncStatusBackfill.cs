namespace ONEVO.Infrastructure.Persistence.DataMigrations;

/// <summary>
/// Data step of migration AddMeetingAttendanceSyncStatus. FROZEN: the migration and its
/// integration test run this exact text. Never edit it; add a new migration instead.
///
/// Before this column, the jobs stamped last_attendance_synced_at even when the provider returned
/// nothing, so zero-row "synced" meetings were never retried. Meetings with rows are marked synced;
/// zero-row ones are re-queued, and the classifier then stores a late report, or (past the 24 h
/// cut-off) records attendance_unavailable / sync_failed.
/// </summary>
public static class AttendanceSyncStatusBackfill
{
    public const string Sql = """
        SET LOCAL app.tenant_context_mode = 'admin';

        UPDATE calendar_event_meetings m
        SET attendance_sync_status = 'synced'
        WHERE m.last_attendance_synced_at IS NOT NULL
          AND m.attendance_sync_status IS NULL
          AND EXISTS (SELECT 1 FROM calendar_event_meeting_attendances a WHERE a.calendar_event_meeting_id = m.id);

        UPDATE calendar_event_meetings m
        SET last_attendance_synced_at = NULL
        WHERE m.last_attendance_synced_at IS NOT NULL
          AND m.attendance_sync_status IS NULL;
        """;
}
