# Task 37: Shared approval request header table

| | |
|---|---|
| Priority | 37 of 42 (lower number = build first). Added 2026-11-15 |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day. Split with a suffix (a, b) if the migration work does not fit |
| Area | DB, Architecture, Performance |
| Depends on | 00a, 00c |

## What we found

Review: `docs/reports/database/2026-11-15-database-design-review.md`, section A3. Timings: `docs/reports/performance/2026-11-15-api-response-timings.md`.

Thirteen tables each carry their own review workflow columns (`status` plus `reviewed_at`, `decided_at` or `resolved_at`, usually with a reviewer id and a comment): `leave_requests`, `leave_request_approvers`, `attendance_corrections`, `attendance_alerts`, `device_change_requests`, `location_change_requests`, `work_area_change_requests`, `offboarding_task_bypass_requests`, `seat_increase_requests`, `project_member_invitations`, `tray_device_authorizations`, `wm_approval_requests`, `exceptions`.

No single table says "this person has something waiting for them". To build the inbox the application asks every module in turn and merges in memory. That is why all four inbox endpoints take over 3 seconds, and why inbox counts cannot be a simple `COUNT`.

## What to add and change

1. Add one header table, `inbox_items`:
   - `id`, `tenant_id`
   - `source` and `kind` (which module and which type), `subject_id` (the row in the module's own table)
   - `category` (request, alert, invitation)
   - `subject_employee_id` (who it is about), `requested_by_employee_id`, `assigned_to_employee_id` (who must act now)
   - `status` (pending, approved, rejected, cancelled, resolved), `title`, `summary`
   - `created_at`, `updated_at`, `decided_at`, `decided_by_employee_id`
2. Unique index on `(tenant_id, source, kind, subject_id)`.
3. Indexes for the two inbox questions:
   - received: `(tenant_id, assigned_to_employee_id, status, updated_at DESC)` with `title`, `source`, `kind`, `subject_id` included;
   - sent: `(tenant_id, requested_by_employee_id, status, updated_at DESC)`.
4. Check constraints on `status` and `category`.
5. For multi-step approvals (leave approvers, the work management approver chain) allow several rows per subject, one per person who must act, or add a child table `inbox_item_assignees`. Decide with the leave and work management owners.
6. The module tables stay as the source of truth for their details. Each module writes or updates its header row in the same transaction as its own change. Start with the two modules that have the most rows (find them with the count from task 00c).
7. Backfill existing pending rows with one script per module.
8. Add a nightly consistency check that compares header rows with the module tables and logs any difference.
9. Register the table for RLS.

## Done when

- The inbox list and counts for the converted modules are single indexed queries on `inbox_items`.
- A request created, decided or reverted in a converted module updates its header row in the same transaction (test).
- The consistency check reports zero differences on the dev database.
