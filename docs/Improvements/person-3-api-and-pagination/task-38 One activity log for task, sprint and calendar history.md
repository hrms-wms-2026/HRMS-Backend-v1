# Task 38: One activity log for task, sprint and calendar history

| | |
|---|---|
| Priority | 38 of 42 (lower number = build first). Added 2026-11-15 |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day. Split with a suffix (a, b) if the migration work does not fit |
| Area | DB, Code quality |
| Depends on | nothing |

## What we found

Review: `docs/reports/database/2026-11-15-database-design-review.md`, sections A4 and A5.

History is stored in a separate table per kind of change:

| Table | Columns | Rows in dev |
|---|---|---|
| `task_status_change_logs` | task, employee, from status, to status, time | 794 |
| `task_percentage_logs` | task, employee, previous and new percent, source, session, reason, time | 1,032 |
| `task_edit_logs` | task, employee, source, old and new JSON, reason, time | 152 |
| `task_comment_logs` | task, comment, employee, action, time | 0 |
| `sprint_activity_logs` | sprint, employee, action, from and to status, details JSON, time | 75 |
| `calendar_event_activity_logs` | event, action, performer, details JSON, time | 0 |

The task history feed must read four tables and merge them by time. Each new kind of change needs a new table, entity, repository and RLS entry. A generic `audit_logs` table with the right shape already exists and has no rows in the dev database.

Notifications have the same pattern: `notifications`, `notification_deliveries`, `monitoring_notifications` and `wm_notification_log`. The first two hold the same title, body and related entity for the same 815 events.

## What to add and change

1. Add `activity_log`: `id`, `tenant_id`, `entity_type` (task, sprint, calendar_event), `entity_id`, `parent_entity_id` (the project or objective, for feeds), `action` (status_changed, percent_changed, edited, comment_added and so on), `actor_employee_id`, `source`, `from_value`, `to_value` (short text for the common case), `details_json` (for the rest), `reason`, `occurred_at`.
2. Cluster on `(tenant_id, occurred_at, id)`. Add `(tenant_id, entity_type, entity_id, occurred_at DESC)` for "history of this item" and `(tenant_id, parent_entity_id, occurred_at DESC)` for project feeds.
3. Check constraint on `entity_type`. Keep `action` as free text with a constants class in code.
4. Keep typed columns where reports filter on them: for status changes store `from_status_id` and `to_status_id` as real columns, not only in JSON, because the board analytics read them.
5. Move the six tables one at a time: copy rows, switch writers, switch readers, drop the old table one release later.
6. Do not move `leave_balance_audits`, `billing_audit_logs` or `inbox_decision_journal`. They are financial or decision records with their own rules.
7. Decide what `audit_logs` is for. If it is the security audit trail (who changed which permission or setting), keep it separate from the activity log and make sure it is written. If nothing writes it, say so and remove it.
8. Notifications: write a short design note proposing one delivery table where the in-app bell is a channel, and what happens to `monitoring_notifications` and `wm_notification_log`. Do not build it in this task.

## Done when

- Task history is one query on one table.
- Adding a new kind of history is a new `action` value, not a new table.
- Row counts match before and after each copy.
- The notification design note is in `docs/design-notes/`.
