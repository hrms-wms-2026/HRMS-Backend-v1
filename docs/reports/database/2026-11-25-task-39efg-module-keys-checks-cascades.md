# Tasks 39e, 39f and 39g: module foreign keys, status checks, cascade review (2026-11-25)

Task files: `docs/Improvements/person-1-security-and-auth/task-39e Other module foreign keys.md`, `task-39f Remaining status check constraints.md`, `task-39g Cascade delete review.md`. Follows task 39d (`2026-11-25-task-39d-tenant-foreign-keys.md`). One branch, `claude/improve-task-39efg`, three migrations generated one after another on the updated snapshot.

| Migration | What it does | Hand-written part |
|---|---|---|
| `20261125053716_AddOtherModuleForeignKeys39e` | 107 foreign keys (all NO ACTION) and the 92 indexes they need | `RepairOtherModuleReferencesSql` (repair, runs first) |
| `20261125053747_AddStatusCheckConstraints39f` | 60 check constraints | `CheckStatusValuesSql` (read-only check, runs first) |
| `20261125053817_ReviewCascadeDeletes39g` | 28 keys from CASCADE to NO ACTION (drop and add) | none: no data changes |

None of them was applied to a shared database by the agent.

## 39e: foreign keys in the other modules

| Item | Before | After |
|---|---|---|
| `task-39e` lines in `missing-foreign-keys.txt` | 124 | 0 |
| Allow-list lines | 141 | 34 |

- **Keys**: `ReferenceForeignKeys` gets one method per module: `ApplyMonitoring` (45), `ApplyAuth` (20), `ApplyCalendar` (16), `ApplyConversations` (12), `ApplySharedPlatform` (9), `ApplyInbox` (2), `ApplyInfrastructureModule` (2), `ApplyDashboard` (1).
- **Targets**: the desktop agent's `agent_device_id` and `device_registration_id` are `tray_device_registrations` ids (the ingest handlers write `_device.DeviceRegistrationId`). The actor columns hold the signed-in tenant user (`users`); the platform admin columns (`notification_template_versions.*_by_user_id`) point at `platform_users`. The calendar module's `event_id` columns point at `personal_calendar_events`.
- **Delete check**: no parent is hard-deleted by the application. Users, employees, device registrations and file records are never deleted (`FileStorageService.DeleteAsync` sets `deleted_at`); personal calendar events, participants, conversation messages and inbox journal rows are `BaseEntity` rows, which the soft-delete interceptor keeps. `DisconnectCalendarConnection` deletes the connection's event links before the connection, in the same save, so that key holds.
- **Monitoring purge**: `MonitoringRetentionPurgeRunner` deletes `activity_raw_buffer` rows and `monitoring_evidence_assets` rows. Neither table is a parent of any key, so the purge order is unaffected. The new keys add an index on each high-volume `agent_device_id` column that had none.
- **Kept without a key (17)**, each with its reason in the allow-list:
  - `calendar_event_meetings.external_calendar_connection_id`: the disconnect hard-deletes the connection and keeps the meeting row.
  - `conversation_messages.reply_to_message_id`: `DeleteMessageIfExistsAsync` hard-deletes a system card, and a self reference cannot be SET NULL on SQL Server.
  - `notification_templates.active_version_id`: a template and its first version are added in one save (`NotificationTemplateSeeder`, `CreateNotificationTemplate`); with `notification_template_versions.template_id` a key makes a cycle that EF cannot order. The first gate run found it (6 unit tests failed with "circular dependency"), so the key was dropped and the three migrations regenerated in order.
  - `payment_gateway_credentials.rotated_by_id` and `deactivated_by_id`: see below; `CredentialOwnershipCompletionArchitectureTests` limits later migrations on these tables.
  - `mixed` (no type column, more than one parent): `calendar_event_participant_rsvp_history.event_id`, `calendar_event_reminders_sent.event_id`, `calendar_meeting_incidents.event_id`.
  - `polymorphic`: `feature_access_grants.grantee_id`, `inbox_decision_journal.item_id`.
  - `external` (no table): `audit_logs.correlation_id`, `monitoring_evidence_assets.legal_hold_id` and `retention_policy_id`, `notification_deliveries.event_id`.
  - `unused` (no code writes it): `module_catalog_price_history.changed_by_id`, `tenant_provisioning_states.last_updated_by_id`, `tenant_setup_selections.completed_by_id`.
- With 39b, 39c and 39d done, 34 id columns have no key, each with a reason (task 39's target was fewer than 100).

### Repair SQL (39e)

`RepairOtherModuleReferencesSql` runs first in `Up`, in the `admin` tenant context mode (SQL Server RLS applies to `sa`), with the same rules as 39c:

1. `Guid.Empty` becomes `NULL` on the nullable columns.
2. A value found in another identity table (`users`, `platform_users`, `employees`) but not in the key's target becomes `NULL` on a nullable column. The counts are printed.
   - 2b. The Dapi demo seeder wrote one device id per employee into `activity_snapshots`, `app_usage_snapshots` and `device_state_snapshots` without creating its `tray_device_registrations` row. The bad-data check found 12281 of 12281 rows unknown in each, all in tenant `6b0874ab-71db-401f-859f-bdd50c1317fb`. For that demo tenant only, the repair adds the missing registration with the same id, for the employee's user (or the demo owner), named "Demo laptop", with first and last seen taken from the snapshots. The count is printed. `DapiHistorySeeder` now creates the same rows itself, so a fresh seed is clean.
3. Any other value that is not a row of its parent stops the migration with 50039, naming each `table.column=rows`. Nothing is deleted.

The `payment_gateway` tables are not touched by these migrations: `CredentialOwnershipCompletionArchitectureTests` allows a later migration on them only the 39c audit key. So `payment_gateway_credentials.rotated_by_id` and `deactivated_by_id` stay on the allow-list, and the two `payment_gateway` cascades stay as they are (configs are never removed, so they never fire). Changing them needs that test's owner to agree; the first gate run found this (the generated operations failed the test), and the test was not widened.

## 39f: status and type check constraints

| Item | Before | After |
|---|---|---|
| `todo` lines in `missing-check-constraints.txt` | 98 | 35 |
| `free` lines | 6 | 9 |
| Rules in `StatusCheckConstraints` | 21 | 81 |

- **Order of the task**: all review workflow tables of task 37 (`access_grant_requests`, `attendance_corrections`, `location_change_requests`, `work_area_change_requests`, `device_change_requests`, `offboarding_task_bypass_requests`, `seat_increase_requests`, `wm_approval_requests`, `project_member_invitations`), then the type columns next to polymorphic ids (`wm_approval_requests.target_type`, `wm_monitor_alerts.target_type`, `entity_assets.owner_type`), then 43 more columns whose values come from a constants class.
- **New constants classes** (the values were string literals): `PermissionChangeKinds` (used by `PermissionCatalogSynchronizer`), `AccessGrantApprovalStatuses` (stored capitalised: `Pending`, `Approved`, `Rejected`), `AgentCommandTypes`, `AgentCommandStatuses` (including `expired`, written by `ExpireStaleCommandsAsync`), `MonitoringEvidenceTypes`, `MonitoringEvidenceTriggers`. The monitoring writers now use them.
- **Raw SQL writers** were checked: the `ExecuteSql` writes to `calendar_event_meetings`, `position_assignments`, `file_upload_reservations`, `tasks` and `meeting_details` use the constants.
- **Moved to `free`**: `notifications.related_entity_type` and `notification_deliveries.related_entity_type` (an open navigation key such as `inbox:{source}:{kind}`), `audit_logs.resource_type` (an entity name per audit writer).
- **Still `todo` (35)**: the values are string literals or client input with no constants class yet (billing statuses, legal document types, address and bank account types, checklist template types, `invitation_tokens.status`, `presence_sessions.status`, `outbox_messages.type` and others). Their reason now says so. Each needs a constants class and a grep of its writers before it can get a constraint; this is the 39f remainder.

### Check SQL (39f)

`CheckStatusValuesSql` runs first in `Up`. It counts, per constrained column, the rows whose value is outside the new list (NULL passes a CHECK constraint, so it is left out), and stops with 50039 naming each `table.column=rows`. It changes no data: an old value is reported, not rewritten.

## 39g: cascade deletes

45 keys used `ON DELETE CASCADE`. `CascadeDeleteRules` (applied after the configurations) keeps 17 and turns the other 28 into NO ACTION. The decision for each key:

| Child | Parent | Decision | Why |
|---|---|---|---|
| clock_in_late_deduction_rules | clock_in_policies | keep | line items; `UpdateClockInPolicy` clears `LateDeductionRules` |
| conversation_message_attachments, hides, mentions, reactions, revisions (5) | conversation_messages | keep | parts of a message; `DeleteMessageIfExistsAsync` hard-deletes a system card |
| dashboard_widget_layouts | dashboard_widgets | keep | layout rows; `DashboardLayoutSupport` clears `Layouts` |
| dashboard_widgets | user_dashboards | keep | `DashboardLayoutSupport` removes a widget from `Widgets` |
| leave_policy_blackout_periods | leave_policy_versions | keep | a future version is hard-deleted |
| role_permissions, user_permission_overrides | permissions | keep | `PermissionCatalogSynchronizer` removes a retired permission and relies on the cascade |
| role_permissions, user_roles | roles | keep | `ArchiveRole` removes the role through EF (soft-deleted); EF cascades its tracked parts |
| task_assignments, planned_task_blocks | tasks | keep | tasks are removed through EF (soft-deleted); EF cascades the tracked rows |
| agent_commands | tray_device_registrations | NO ACTION | registrations are never deleted; commands are history |
| bulk_onboarding_batch_rows | bulk_onboarding_batches | NO ACTION | batches are never removed |
| calendar_event_objectives, calendar_event_tasks | calendar_events | NO ACTION | project calendar events are archived, never deleted |
| conversation_favorites, conversation_messages, conversation_mutes, conversation_participants | conversations | NO ACTION | conversations are never removed |
| dashboard_template_widgets, dashboard_template_widget_layouts | dashboard_templates, dashboard_template_widgets | NO ACTION | templates and their widgets are never removed |
| leave_policy_assignments, leave_policy_versions | leave_policies | NO ACTION | policies are never removed |
| leave_request_approvers, leave_request_day_allocations, leave_request_documents | leave_requests | NO ACTION | requests are never deleted |
| module_catalog_price_history, module_features, module_permission_ownership | module_catalog | NO ACTION | catalog modules are never removed |
| payment_gateway_country_routes, payment_gateway_credentials | payment_gateway_configs | keep (not reviewed) | `CredentialOwnershipCompletionArchitectureTests` limits later migrations on these tables to the 39c audit key; configs are never removed, so the cascade never fires |
| platform_oauth_app_credentials | platform_oauth_apps | NO ACTION | apps are never removed |
| platform_role_permissions | platform_permissions, platform_roles | NO ACTION | neither parent is removed |
| platform_user_credentials, platform_user_invites, platform_user_roles, platform_user_sessions | platform_users | NO ACTION | a person: never removed with their history |
| platform_user_roles | platform_roles | NO ACTION | roles are never removed |
| support_ticket_attachments, support_ticket_comments | support_tickets | NO ACTION | tickets are never deleted |

- NO ACTION removes cascade paths only, so no key becomes illegal (SQL Server's multiple cascade path rule).
- The migration changes delete behaviour only, never data.
- `ForeignKeyRulesTests.EveryCascadeIsOnTheAllowList` holds the kept keys with their reasons and fails on a cascade that is not listed, on a listed key that no longer cascades, and when it differs from `CascadeDeleteRules.Kept`. `NoForeignKeyToAPersonCascades` now covers `platform_users` too.

## Bad data in the local database

The agent cannot connect to the database (the connection needs the `sa` password from `.env`, which the auto-mode classifier refused). One read-only check covers all three tasks: `C:\Users\User\Desktop\orphans39efg.sql`.

```powershell
& "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE" -S localhost,1433 -d CrownusDb -U sa -C -i C:\Users\User\Desktop\orphans39efg.sql -o C:\Users\User\Desktop\orphans39efg-result.txt
```

- Part 1 (39e): per new key, the rows, `NULL`, `Guid.Empty`, values in the target, values in `users`, `platform_users` and `employees` instead, unknown values, and up to 3 samples.
- Part 2 (39f): the distinct values and row counts of every column that was `todo` (all 98), so the constrained ones can be checked and the remaining ones get their value lists.
- Part 3 (39g): the foreign keys that cascade in the database, with child row counts, and the latest applied migration.

It runs in the `admin` tenant context mode, resets it at the end, and changes nothing (its only `INSERT`s fill table variables). Result: the user ran it on the local database on 2026-11-25 (file `C:\Users\User\Desktop\orphans39efg-result.txt`).

- Part 1: every key shows 0 unknown, 0 `Guid.Empty` and 0 wrong-kind ids, except `activity_snapshots`, `app_usage_snapshots` and `device_state_snapshots.agent_device_id`. Those have 12281 of 12281 rows unknown each, all `NOT NULL`, none in `tray_device_registrations`. The cause was the Dapi demo seeder (step 2b above). `NULL` was not possible and no row is deleted: the repair adds the missing demo registrations, and the seeder now creates them.
- Part 2: all 38 values found in the 60 newly constrained columns are on their lists. SQL Server lets `NULL` through a check, and the 39f pre-check counts it the same way, so the 30 `NULL` rows of `sprint_activity_logs.from_status` are fine. `tenant_subscriptions.maintenance_status` (2 `NULL` rows) and the other 14 columns listed are still `todo`, without a check.
- Part 3: informational, nothing unexpected.

## Tests

`OtherModuleRepairTests` (integration, `SqlServerMigration`): the 39e repair passes on clean data, adds the missing registration for an unknown device of the Dapi demo tenant (the snapshot is kept), still stops naming `activity_snapshots.agent_device_id=1` for any other tenant without adding one, sets `Guid.Empty` and an employees id in `users.suspended_by_id` to `NULL`, and stops with 50039 naming `users.suspended_by_id=1` on an unknown id without changing it; the 39f check passes on clean data and stops naming `clock_in_policies.scope_type=1` without changing the row.

## Gate

Fast gate only (Execution's instruction: the full integration run happens on feature/testing).

- `verify-20261125-123611`: build PASS, unit 8371 passed, architecture 1022 passed.
- SqlServerMigration tests alone (Testcontainers SQL Server, all migrations applied to a fresh database): 305 of 305 passed, `OtherModuleRepairTests` 6 of 6 included.
- The read-only desktop check was run once on a migrated test database to confirm it runs; that temporary test was not committed.
- After the Dapi device fix (commit aa535975f): `verify-20261125-154107` build PASS, unit 8371 passed, architecture 1022 passed. SqlServerMigration alone: 307 of 307 passed, including the 2 new device repair tests. The first gate run that day (`verify-20261125-153523`) had one timing failure, `WidgetDataOrchestratorTests.Slow_provider_times_out_into_error`, while the main checkout's full gate ran at the same time. That test is outside this change, and it passed 3 of 3 times on its own and in the rerun.

Earlier runs, both fixed without weakening a test:

1. `verify-20261125-105514`: 6 unit tests failed (`NotificationTemplateSeederTests`, `InboxNotificationNavigationTests`) with an EF "circular dependency": the new key `notification_templates.active_version_id` closed a cycle with `notification_template_versions.template_id`. The key was dropped (allow-list line with the reason).
2. `verify-20261125-110210`: `CredentialOwnershipCompletionArchitectureTests` failed on the generated `payment_gateway` operations. Those tables were left out of 39e and 39g, and the test was not widened.

After each, the three migrations were removed, the snapshot restored from the commit, and the migrations generated again in order (39e, 39f, 39g).

## Full gate on feature/testing (dfd3c4ac7): 26 integration failures

All 26 came from the new keys. Each was either a fixture writing a made-up parent id, or an app bug that the key exposed.

| Group | Failing key or check | Tests | Cause | Fix |
|---|---|---|---|---|
| 1 | `fk_user_dashboards_users_user_id` | 6 `UserDashboardRepositoryTests` | Fixture: made-up user id | `TestUsers.NewAsync` (new helper) saves the user first |
| 2 | `fk_monitoring_policy_overrides_users_set_by_id` | 4 `MonitoringPolicyOverrideIntegrationTests`, 4 `MonitoringLegalEntityScopingIntegrationTests` | Fixture: made-up `set_by_id` | `TestUsers.New` |
| 3 | `fk_app_usage_snapshots_tray_device_registrations_agent_device_id` | 4 `MeetingComplianceTaskMeetingIntegrationTests`, 1 `MeetingLeakIntegrationTests` | Fixture: made-up device id | `TestDevices.ForEmployeeAsync` (new helper) registers a device of the employee's user |
| 4 | `fk_user_roles_users_user_id` | 2 `OfficeProjectReconcilerIntegrationTests` | Fixture: an owner without an employee had no `users` row either | `OfficeProjectDb.AssignRoleAsync` adds the user when it is missing |
| 5 | `fk_sessions_users_user_id` | 1 `SessionRevocationTests` | Fixture: made-up user ids | `TestUsers.New` |
| 6 | `ck_calendar_event_meetings_owner` (a 2026-11-07 check, not task 39) | 1 `MeetingProvisioningIntegrationTests` | **App**: the new key `calendar_event_meetings.calendar_event_id` was `NoAction`. `DeleteCalendarEvent` removes the event (soft delete, so the row stays), and EF then set the tracked meeting's key to `NULL`, which broke the owner check | Reference keys are now `ClientNoAction` (below) |
| 7 | `fk_device_change_requests_employees_employee_id` (500, not 409) | 1 `TrayActivationIntegrationTests` | **App**: `TrayEnrollmentService` wrote the user's id into `device_change_requests.employee_id`, while the approval workflow reads it back as an employee id, so the approver could never find the requester. The bug was older than task 39; the key exposed it | The service stores the user's default employee; a user without an employee is still refused, but no request is raised. The test looks the request up by employee id. The 39e repair step 2c maps existing user ids to their employee |
| 8 | `fk_notifications_users_recipient_user_id` | 2 `OutboxProcessorBehaviourIntegrationTests` | Fixture: the test handler staged a notification for a made-up recipient, so the staged save failed | The payload carries a saved recipient user |

### ClientNoAction for the reference keys

Every key from `ReferenceForeignKeys` (tasks 39b-39e, through `Fk<>` and the `created_by_id` loop) was `DeleteBehavior.NoAction`. On a tracked child, EF then sets a nullable key to `NULL`, and refuses a required one, whenever the parent is removed. The soft-delete interceptor keeps the parent row, so this silently cut a valid link (group 6). Before task 39 these columns had no key, so EF did nothing.

`ClientNoAction` keeps `ON DELETE NO ACTION` in the database, so a real hard delete of a parent with children still fails there, and EF leaves tracked children alone, as it did before task 39. The `tenant_id` keys stay `NoAction`: tenants are never removed, and unit tests pin that. The explicit `Restrict` keys and the 39g cascade review are unchanged.

The change is model only. `20261125114212_ReferenceKeysClientNoAction39e` was generated on top of 39g: it has no operations (empty `Up` and `Down`), and its snapshot moves 278 keys from `NoAction` to `ClientNoAction`. No earlier migration was edited.

New tests:
- `OtherModuleRepairTests.Repair_maps_a_user_id_in_device_change_requests_to_its_employee` (integration).
- `TrayEnrollmentServiceTests.IssueAsync_WhenDifferentFingerprintAndUserHasNoEmployee_RefusesWithoutRaisingARequest` (unit).
- The device change unit test now checks `EmployeeId`.
