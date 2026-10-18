-- Meetings-as-tasks Phase 2, Release A preflight (spec 4.1, 8).
--
-- Run BEFORE applying migration AddMeetingWorkItemsFoundation, as the migrator (or any role that can read the
-- calendar and task tables):
--
--     psql "$MIGRATION_CONNECTION" -f ops/meetings/phase2-preflight.sql
--
-- Read-only: the whole script runs in a READ ONLY transaction that is rolled back. Each line counts the existing
-- rows that would violate a constraint or trigger the migration adds; every count must be 0. A non-zero count means
-- the migration's VALIDATE CONSTRAINT step (or the first later write to that row) would fail - fix the data first.

BEGIN READ ONLY;

-- Row level security: see every tenant's rows (set for this transaction only).
SELECT set_config('app.tenant_context_mode', 'admin', true) AS admin_mode \gset

SELECT check_name,
       violations,
       CASE WHEN violations = 0 THEN 'OK' ELSE 'FIX BEFORE MIGRATING' END AS status
FROM (
    -- ck_calendar_event_participants_one_owner: every existing participant must have its calendar event.
    SELECT 1 AS ord, 'calendar_event_participants without an event' AS check_name,
           count(*) AS violations
    FROM calendar_event_participants WHERE event_id IS NULL
    UNION ALL
    -- ck_calendar_event_guests_one_owner
    SELECT 2, 'calendar_event_guests without an event',
           count(*) FROM calendar_event_guests WHERE event_id IS NULL
    UNION ALL
    -- ck_calendar_event_meetings_owner: existing links become owner_kind = 'calendar_event'.
    SELECT 3, 'calendar_event_meetings without an event',
           count(*) FROM calendar_event_meetings WHERE calendar_event_id IS NULL
    UNION ALL
    -- ck_external_calendar_event_links_one_owner
    SELECT 4, 'external_calendar_event_links without an event',
           count(*) FROM external_calendar_event_links WHERE calendar_event_id IS NULL
    UNION ALL
    -- trg_meeting_task_has_details: a 'meeting' task with no meeting_details row would fail on its next write.
    SELECT 5, 'tasks already marked task_kind = meeting',
           count(*) FROM tasks WHERE task_kind = 'meeting'
    UNION ALL
    -- ux_tasks_tenant_id_id / ux_objectives_tenant_project_id / ux_calendar_event_meetings_tenant_id_id are implied
    -- by the primary keys; these confirm no id repeats (a damaged PK index would let one through).
    SELECT 6, 'duplicate task ids',
           count(*) FROM (SELECT id FROM tasks GROUP BY id HAVING count(*) > 1) d
    UNION ALL
    SELECT 7, 'duplicate objective ids',
           count(*) FROM (SELECT id FROM objectives GROUP BY id HAVING count(*) > 1) d
    UNION ALL
    SELECT 8, 'duplicate calendar_event_meetings ids',
           count(*) FROM (SELECT id FROM calendar_event_meetings GROUP BY id HAVING count(*) > 1) d
) checks
ORDER BY ord;

-- Already applied? (Then this preflight is moot - the constraints are live.)
SELECT CASE WHEN EXISTS (
           SELECT 1 FROM "__EFMigrationsHistory" WHERE migration_id LIKE '%_AddMeetingWorkItemsFoundation')
       THEN 'AddMeetingWorkItemsFoundation: already applied'
       ELSE 'AddMeetingWorkItemsFoundation: not applied yet' END AS migration_state;

ROLLBACK;
