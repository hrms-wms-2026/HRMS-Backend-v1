# Database design review, 2026-11-15

Question: is the table design sound, are there tables that should be one table, and what should be added where?

Answer: the base is solid (every table has a primary key, row-level security, snake_case names, almost every foreign key is indexed). There are six places where one concept is spread over several tables, and four gaps that apply across the whole schema: missing foreign keys, no check constraints on status columns, random GUID clustered keys, and few concurrency tokens. The fix tasks are 35 to 42 in `docs/Improvements`.

## How it was checked

Read-only catalogue queries (`sys.tables`, `sys.columns`, `sys.indexes`, `sys.foreign_keys`) against the local SQL Server development database, using the application login. Row counts come from `sys.partitions`. No data was changed. The database holds demo data only (70,853 rows in total), so sizes show which tables grow fastest, not how big they will get.

Index usage statistics could not be read: the application login has no permission for them, which is correct. Task 40 runs that part with an admin login.

## Size of the schema

| Item | Count |
|---|---|
| Tables | 219 |
| Columns | 2,774 |
| Indexes | 817 |
| Foreign keys | 273 |
| Check constraints | 109 |
| Triggers | 4 |
| Tables with `tenant_id` | 174 |
| Tables with no rows in the dev database | 110 |

## A. One concept split over several tables

### A1. Three snapshot tables for one capture

`activity_snapshots`, `app_usage_snapshots` and `device_state_snapshots` all have the same key columns (`tenant_id`, `employee_id`, `agent_device_id`, `captured_at`) and exactly 13,604 rows each. They are the three largest tables. One capture from the tray app is written as three rows in three tables, each with its own GUID primary key and two more indexes.

- `activity_snapshots`: keyboard and mouse counts, active and idle seconds, intensity, foreground process.
- `app_usage_snapshots`: process name, window title hash.
- `device_state_snapshots`: idle seconds, is idle, latitude, longitude, accuracy.

`idle_seconds` is stored twice. The foreground process name is stored twice. Reading one moment for one employee needs a three-way join.

Verdict: merge into one table. Task 35.

### A2. Three tables with the same monitoring switches

`monitoring_feature_toggles` (per legal entity), `monitoring_policy_overrides` (per `scope_type` and `scope_id`) and `employee_monitoring_overrides` (per employee) share 13 to 15 of their 14 to 17 columns: the same list of switches such as `activity_monitoring`, `screenshot_capture`, `device_tracking`, `idle_threshold_minutes`.

`monitoring_policy_overrides` already has a generic scope. The other two are that same table with the scope fixed. Adding one new monitoring feature means three migrations and three places in the resolver.

Verdict: merge into one settings table with a scope. Task 36.

### A3. Review workflow repeated in 13 tables

These tables each carry their own `status` plus `reviewed_at`, `decided_at` or `resolved_at`, and usually `reviewed_by_id` and a comment: `leave_requests`, `leave_request_approvers`, `attendance_corrections`, `attendance_alerts`, `device_change_requests`, `location_change_requests`, `work_area_change_requests`, `offboarding_task_bypass_requests`, `seat_increase_requests`, `project_member_invitations`, `tray_device_authorizations`, `wm_approval_requests`, `exceptions`.

There is no shared row that says "this person has something waiting". That is the root of the slow inbox measured on 2026-11-15: to build the inbox the application must ask every one of these tables.

Verdict: keep the detail tables, add one shared header table. Task 37.

### A4. History spread over nine tables

`task_comment_logs`, `task_edit_logs`, `task_percentage_logs`, `task_status_change_logs`, `sprint_activity_logs`, `calendar_event_activity_logs`, `inbox_decision_journal`, `leave_balance_audits`, `billing_audit_logs`, plus the generic `audit_logs`.

The four task log tables all have `task_id`, `employee_id`, a time and a before and after value. A task history feed has to read and merge four tables. `audit_logs` has the right generic shape (action, resource type, resource id, old and new values) and has no rows in the dev database.

Verdict: one activity log for task, sprint and calendar history. Keep `leave_balance_audits` and `billing_audit_logs` separate because they are financial records. Task 38.

### A5. Four notification stores

`notifications` (815 rows) and `notification_deliveries` (815 rows) hold the same title, body and related entity for the same event. `monitoring_notifications` and `wm_notification_log` are two more stores with their own shape.

Verdict: review as part of task 38. The likely end state is one delivery table where the in-app bell is one channel.

### A6. One-to-one child tables

| Child | Parent | Rows | Verdict |
|---|---|---|---|
| `task_assignments` (unique on `task_id`) | `tasks` | 435 of 754 | Merge into `tasks` as assignee columns, or drop the unique index if several assignees are planned. It also stores both `user_id` and `employee_id` |
| `meeting_details` | `tasks` | 319 of 754 | Keep. A meeting is a task subtype with 19 own columns |
| `meeting_action_items` | `tasks` | 0 | Keep |
| `tenant_storage_stats` | `tenants` | 0 | Keep. It is a counter updated often |
| `platform_oauth_apps`, `platform_service_keys` | `platform_providers` | small | Keep |

## B. Things that are one table and should be split

- **Id lists stored as text.** `clock_in_policies.department_ids`, `.employee_ids`, `.position_ids`, `user_dashboards.applied_role_ids`, `tenant_subscriptions.selected_modules`. They are `nvarchar(max)`. They cannot have a foreign key or an index, a deleted department stays in the list, and "which policies apply to this employee" has to read every policy. Task 41.
- **`tenant_subscriptions` has 45 columns, 29 nullable.** It mixes plan, billing cycle, seats, trial and gateway data. Review in task 41.

## C. Gaps across the whole schema

| # | Finding | Numbers |
|---|---|---|
| C1 | Id columns without a foreign key | 542 GUID columns end in `_id` (not counting `id` and `tenant_id`). 229 have a foreign key, 313 do not. Worst: `invitation_tokens` 9, `wm_approval_requests` 8, `attendance_alerts` 6, `bulk_onboarding_batch_rows` 6, `employees` 5 |
| C2 | `tenant_id` without a foreign key to `tenants` | 174 tables have `tenant_id`. 24 have the foreign key |
| C3 | Status columns with no check constraint | 127 text columns named like status, type, kind or state. None is covered by a check constraint |
| C4 | Random GUID as the clustered key | 205 tables cluster on a `uniqueidentifier` primary key. Code calls `Guid.NewGuid()` 480 times. New rows land on random pages, which causes page splits and fragmentation on the insert-heavy tables |
| C5 | Tenant tables with no index starting with `tenant_id` | 20 tables. With rows today: `conversation_messages` (2,450), `outbox_messages`, `task_comment_reactions`, `role_permissions`, `dashboard_widget_layouts`, `user_roles`, `dashboard_widgets` |
| C6 | Redundant indexes | 27 indexes are a prefix of another index on the same table. Example: `positions` has `(tenant_id)`, `(tenant_id, legal_entity_id)` and `(tenant_id, legal_entity_id, department_id)` |
| C7 | Few concurrency tokens | 9 tables have a `rowversion`. 125 have `updated_at`. Two people saving the same row overwrite each other silently on most tables |
| C8 | `nvarchar(max)` used where a length is known | 151 columns. 56 hold JSON, 52 hold long text, 43 hold other things such as names (`attendance_records.expected_work_mode_name`), error messages, scopes and a subject template |
| C9 | Mixed identity columns | `task_assignments`, `invitation_tokens` and `access_grant_requests` store both `user_id` and `employee_id`. `employee_check_ins` uses `user_id` where its sibling tables use `employee_id` |
| C10 | Cascade deletes | 51 foreign keys delete child rows automatically. Each one should be a decision, not a default |
| C11 | Empty tables | 110 of 219 tables have no rows in the dev database. Most belong to features with no demo data. Some may be unused. Task 42 sorts them |

## D. What is already good

- Every table has a primary key and a clustered index. There are no heaps.
- All but one foreign key column has an index that starts with it.
- Row-level security covers the tenant tables and architecture tests enforce it.
- Naming is consistent snake_case with `ix_`, `pk_`, `ux_` prefixes.
- Rules such as one task short id per tenant and one calendar connection per user and provider are enforced by unique indexes.
- Coordinates and scores use `float`; money and hours use `decimal`. That is the right way round.
- Token and challenge tables (`refresh_tokens`, `password_reset_tokens`, `invitation_tokens`, `tray_device_refresh_tokens`, the four challenge tables) are separate. Keep them separate: they have different lifetimes and security rules.
- Location tables (`employee_check_ins`, `daily_work_location_confirmations`, `employee_work_locations`) look alike but mean different things. Keep them separate.

## E. Order of work

| Order | Task | Why first |
|---|---|---|
| 1 | 37 Shared approval header | Removes the cause of the 3-second inbox |
| 2 | 35 Merge snapshot tables | Cuts the writes of the fastest-growing data to one third |
| 3 | 40 Index fixes | Cheap, helps every tenant query |
| 4 | 39 Foreign keys and check constraints | Stops bad data before UAT creates it |
| 5 | 36, 38, 41, 42 | Clean-up that makes later changes cheaper |

## Not checked

- Query plans and index usage statistics (needs an admin login; task 40 and task 18).
- Column-level encryption of sensitive fields such as bank details and tokens.
- Whether each empty table is used by code.
- Data volumes at real scale. All sizes here are from demo data.
