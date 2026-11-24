# Task 39c: foreign keys for the created_by_id audit columns (2026-11-24)

Task file: `docs/Improvements/person-1-security-and-auth/task-39c Audit column foreign keys.md`. Follows task 39b (`2026-11-23-task-39b-core-foreign-keys.md`).

## What changed

| Item | Before | After |
|---|---|---|
| `audit` lines in `missing-foreign-keys.txt` | 76 | 1, with a reason |
| Allow-list lines in `missing-foreign-keys.txt` | 384 | 309 |
| `created_by_id` foreign keys | 0 | 75 |

- **Migration**: `20261123120447_AddAuditColumnForeignKeys39c`, generated with `dotnet ef migrations add`. It makes 70 `created_by_id` columns nullable, adds 75 indexes and 75 foreign keys, all `NO ACTION`. Four SQL constants in it are hand-written (see "Hand-written SQL").
- **Keys**: `ReferenceForeignKeys.ApplyAuditColumns` in `src/ONEVO.Infrastructure/Persistence/Integrity/ReferenceForeignKeys.cs`. Every root `BaseEntity` table gets its key to `users` from one loop, so a new `BaseEntity` table gets the key without a new line.
- **One migration, not batches of about 30**: Execution asked for one migration (`AddAuditColumnForeignKeys39c`). The keys are all on nullable columns and the repair runs first, so one transaction is enough.

### Which parent each column points at

Each writer was checked: does `created_by_id` hold a `users.id`, an employee id, or a `platform_users.id`?

| Parent | Tables | Why |
|---|---|---|
| `users` (69) | Every `BaseEntity` table with its own table, plus `clock_in_policies` and `meeting_series` | `AuditableEntityInterceptor` stamps `ICurrentUser.UserId`. `MeetingSeriesFactory`, the splitter and the mover write the organizer's or actor's user id. |
| `platform_users` (5) | `tenant_subscriptions`, `global_app_catalog`, `payment_gateway_configs`, `payment_gateway_country_routes`, `tray_app_releases` | Written only by platform admin endpoints, with the platform admin's id. Same pattern as the existing `configuration_templates` and `integration_catalog` keys. |
| `employees` (1) | `calendar_events` (Work Management `CalendarEvent`, the project calendar) | Its writers store the creating employee's id. |

The 69 `users` tables: `attendance_alert_deductions`, `attendance_alerts`, `bulk_onboarding_batch_rows`, `bulk_onboarding_batches`, `calendar_event_participant_rsvp_history`, `calendar_event_participants`, `calendar_event_reminders_sent`, `calendar_meeting_incidents`, `clock_in_policies`, `conversation_favorites`, `conversation_message_attachments`, `conversation_message_hides`, `conversation_message_mentions`, `conversation_message_reactions`, `conversation_message_revisions`, `conversation_messages`, `conversation_mutes`, `conversation_participants`, `conversations`, `dashboard_template_widget_layouts`, `dashboard_template_widgets`, `dashboard_templates`, `dashboard_widget_layouts`, `dashboard_widgets`, `employee_addresses`, `employee_bank_details`, `employee_dependents`, `employee_emergency_contacts`, `employees`, `entity_assets`, `holiday_calendar_settings`, `inbox_decision_journal`, `labels`, `meeting_series`, `objectives`, `onboarding_drafts`, `personal_calendar_events`, `planned_task_blocks`, `position_assignments`, `positions`, `project_categories`, `project_member_invitations`, `project_members`, `projects`, `release_calendar`, `reporting_manager_changes`, `roles`, `sprint_activity_logs`, `sprints`, `task_categories`, `task_clocking_sessions`, `task_comment_logs`, `task_comment_reactions`, `task_comments`, `task_drafts`, `task_edit_logs`, `task_percentage_logs`, `task_status_change_logs`, `task_statuses`, `tasks`, `tenant_one_time_charges`, `user_dashboards`, `users`, `versions`, `wm_approval_requests`, `wm_module_hour_summaries`, `wm_monitor_alerts`, `wm_notification_log`, `wm_task_rate_settings`.

### The one column left, with a reason

| Column | Why |
|---|---|
| `invitation_tokens.created_by_id` | Mixed. `TenantOwnerInvitationService` writes the platform admin's `platform_users` id for the tenant owner's invitation; every other invitation writes a `users` id. No single parent fits. A key needs the column split in two, or a separate platform column. |

## Code changes

- `BaseEntity.CreatedById` is `Guid?`. So are `ClockInPolicy.CreatedById`, `MeetingSeries.CreatedById` and the Work Management `CalendarEvent.CreatedById`, which are not `BaseEntity` types but had the same `Guid.Empty` problem.
- `AuditableEntityInterceptor` writes `NULL` when no tenant user is signed in: anonymous, an empty user id, or a platform admin. `CurrentUserService` reads the `NameIdentifier` claim without checking the scheme, so for a platform admin it returns a `platform_users` id, which the key to `users` would reject. The interceptor now compares it with `ICurrentPlatformUserContext.UserId` (trusted only for `AdminScheme`) and writes `NULL` then. 5 unit tests in `AuditableEntityInterceptorTests`.
- Raw SQL inserts (`EfPositionAssignmentRepository`, twice, and `EfMeetingOccurrenceWriter`) write `NULL` instead of `Guid.Empty`.
- About 20 read sites that compare or pass `CreatedById` use `CreatedById ?? Guid.Empty`, which keeps their current behaviour (a `NULL` creator matches nobody).
- `DevSmokeTestTenantSeeder` writes `NULL` for a `tenant_subscriptions` row (it has no platform admin).
- `CredentialOwnershipCompletionArchitectureTests` still allows `payment_gateway` only in `_InitialSqlServer.cs`, plus, in other migrations, only the exact lines that name the new `platform_users` key and its index on `payment_gateway_*`.

## Hand-written SQL in the migration

| Constant | When | What |
|---|---|---|
| `ModuleHourSummaryProcedureSql` | Up, before the first key | `CREATE OR ALTER` of `wm_refresh_module_hour_summary` (from `AddAuthLookupAndTriggers`). It inserted the literal `Guid.Empty` as `created_by_id`; it now inserts `NULL`. Nothing else changes. The trigger `trg_tasks_module_hour_summary` calls it on every task write, so without this every task write would fail on the new key. |
| `RepairAuditCreatorsSql` | Up, after the columns become nullable, before the first key | In `admin` tenant context mode (SQL Server RLS applies to every login): `Guid.Empty` → `NULL` on 73 columns. Then counts, per key, every value that is not a row of its parent. If any is found it stops with error 50039 and the counts (`table=rows`). It deletes nothing and guesses nothing; the migration's transaction rolls back. |
| `ModuleHourSummaryProcedureDownSql` | Down | The procedure as it was. |
| `RestoreGuidEmptyDownSql` | Down, before the columns become `NOT NULL` again | `NULL` → `Guid.Empty` on the 70 altered columns. |

**Follow-up (after the merge as 7b4073f7a).** On the user's local `CrownusDb`, recreated at 39b and seeded by the pre-39c API, the migration stopped with 50039: `tenant_subscriptions=2`. It rolled back, so nothing changed. Before 39c, `DevSmokeTestTenantSeeder` wrote the tenant owner's `users` id into `tenant_subscriptions.created_by_id`, which now points at `platform_users`. The seeder was fixed in 39c to write `NULL`, but databases seeded earlier still hold those values. The repair now clears only that known pattern: a value that is a `users` row and not a `platform_users` row becomes `NULL`. The row is kept, and any other unknown value still stops with 50039. The other platform-target and employee-target columns were checked: `CreateTenant`, `CreateTrayRelease`, the app catalog and the payment gateway handlers write the platform admin's id or `NULL`, the Dapi seeder leaves `global_app_catalog.created_by_id` `NULL`, and no seeder writes project `calendar_events`. A third `AuditCreatorRepairTests` test covers the new path. The migration had not been applied successfully anywhere, so it was changed in place.

The repair leaves out `payment_gateway_configs` and `payment_gateway_country_routes`. `CredentialOwnershipCompletionArchitectureTests` allows no hand-written SQL on those tables, and the test was not loosened. Their `created_by_id` was already nullable and both writers (`CreatePaymentGateway`, `UpdatePaymentGatewayMetadata`) always set the acting platform admin's id, so there is nothing to repair. If a bad value did exist, adding the key would fail and the migration would roll back.

`AuditCreatorRepairTests` (integration) covers both repair paths on `project_categories`: `Guid.Empty` becomes `NULL` and the key's `WITH CHECK` succeeds; an unknown id stops the repair with 50039, names `project_categories=1`, and stays as it is.

## Bad data

The agent cannot connect to the database (the connection needs the `sa` password from `.env`, which the auto-mode classifier refused). The user ran the checks. The UAT database was not touched, and no migration was applied by the agent.

The user recreated the local `CrownusDb` from `feature/testing` (task 39b level) before this migration. A fresh database has only seeded rows. `RepairAuditCreatorsSql` turns any `Guid.Empty` into `NULL` and stops with 50039 if anything else is wrong, so applying the migration is itself the check.

For any other database, this read-only query prints, per table, the rows, `NULL`, `Guid.Empty`, and how many values are `users`, `platform_users`, `employees` or unknown (with up to 3 samples). Run as `sa` with `sqlcmd -C -i <file>`; it changes nothing:

```sql
SET NOCOUNT ON;
EXEC sp_set_session_context N'app.current_tenant_id', N'';
EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';

DECLARE @t TABLE (name sysname);
INSERT @t VALUES
('attendance_alert_deductions'), ('attendance_alerts'), ('bulk_onboarding_batch_rows'), ('bulk_onboarding_batches'), ('calendar_event_participant_rsvp_history'), ('calendar_event_participants'), ('calendar_event_reminders_sent'), ('calendar_events'), ('calendar_meeting_incidents'), ('clock_in_policies'), ('conversation_favorites'), ('conversation_message_attachments'), ('conversation_message_hides'), ('conversation_message_mentions'), ('conversation_message_reactions'), ('conversation_message_revisions'), ('conversation_messages'), ('conversation_mutes'), ('conversation_participants'), ('conversations'), ('dashboard_template_widget_layouts'), ('dashboard_template_widgets'), ('dashboard_templates'), ('dashboard_widget_layouts'), ('dashboard_widgets'), ('employee_addresses'), ('employee_bank_details'), ('employee_dependents'), ('employee_emergency_contacts'), ('employees'), ('entity_assets'), ('global_app_catalog'), ('holiday_calendar_settings'), ('inbox_decision_journal'), ('invitation_tokens'), ('labels'), ('meeting_series'), ('objectives'), ('onboarding_drafts'), ('payment_gateway_configs'), ('payment_gateway_country_routes'), ('personal_calendar_events'), ('planned_task_blocks'), ('position_assignments'), ('positions'), ('project_categories'), ('project_member_invitations'), ('project_members'), ('projects'), ('release_calendar'), ('reporting_manager_changes'), ('roles'), ('sprint_activity_logs'), ('sprints'), ('task_categories'), ('task_clocking_sessions'), ('task_comment_logs'), ('task_comment_reactions'), ('task_comments'), ('task_drafts'), ('task_edit_logs'), ('task_percentage_logs'), ('task_status_change_logs'), ('task_statuses'), ('tasks'), ('tenant_one_time_charges'), ('tenant_subscriptions'), ('tray_app_releases'), ('user_dashboards'), ('users'), ('versions'), ('wm_approval_requests'), ('wm_module_hour_summaries'), ('wm_monitor_alerts'), ('wm_notification_log'), ('wm_task_rate_settings');

DECLARE @name sysname, @sql nvarchar(max);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM @t ORDER BY name;
OPEN cur; FETCH NEXT FROM cur INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
  IF COL_LENGTH(N'dbo.' + @name, N'created_by_id') IS NULL
    PRINT @name + N' | MISSING TABLE/COLUMN in this database';
  ELSE
  BEGIN
    SET @sql = N'
      DECLARE @rows int, @nul int, @empty int, @usr int, @plat int, @emp int, @unk int, @s nvarchar(400);
      SELECT @rows = COUNT(*), @nul = SUM(f.is_null), @empty = SUM(f.is_empty),
             @usr = SUM(f.is_usr), @plat = SUM(f.is_plat), @emp = SUM(f.is_emp),
             @unk = SUM(CASE WHEN f.is_null = 0 AND f.is_empty = 0 AND f.is_usr = 0 AND f.is_plat = 0 AND f.is_emp = 0
                             THEN 1 ELSE 0 END)
      FROM (
        SELECT CASE WHEN c.created_by_id IS NULL THEN 1 ELSE 0 END AS is_null,
               CASE WHEN c.created_by_id = ''00000000-0000-0000-0000-000000000000'' THEN 1 ELSE 0 END AS is_empty,
               CASE WHEN EXISTS (SELECT 1 FROM dbo.users u WHERE u.id = c.created_by_id) THEN 1 ELSE 0 END AS is_usr,
               CASE WHEN EXISTS (SELECT 1 FROM dbo.platform_users p WHERE p.id = c.created_by_id) THEN 1 ELSE 0 END AS is_plat,
               CASE WHEN EXISTS (SELECT 1 FROM dbo.employees e WHERE e.id = c.created_by_id) THEN 1 ELSE 0 END AS is_emp
        FROM dbo.' + QUOTENAME(@name) + N' c
      ) f;
      SELECT @s = STRING_AGG(CAST(v AS nvarchar(36)), N'','') FROM (
        SELECT DISTINCT TOP 3 c.created_by_id v FROM dbo.' + QUOTENAME(@name) + N' c
        WHERE c.created_by_id IS NOT NULL AND c.created_by_id <> ''00000000-0000-0000-0000-000000000000''
          AND NOT EXISTS (SELECT 1 FROM dbo.users u WHERE u.id = c.created_by_id)
          AND NOT EXISTS (SELECT 1 FROM dbo.platform_users p WHERE p.id = c.created_by_id)
          AND NOT EXISTS (SELECT 1 FROM dbo.employees e WHERE e.id = c.created_by_id)) x;
      PRINT CONCAT(''' + @name + N''', '' | rows '', @rows, '' | null '', ISNULL(@nul,0), '' | empty '', ISNULL(@empty,0),
        '' | users '', ISNULL(@usr,0), '' | platform_users '', ISNULL(@plat,0), '' | employees '', ISNULL(@emp,0),
        '' | unknown '', ISNULL(@unk,0), CASE WHEN @unk > 0 THEN '' | sample '' + @s ELSE '''' END);';
    EXEC sp_executesql @sql;
  END
  FETCH NEXT FROM cur INTO @name;
END
CLOSE cur; DEALLOCATE cur;

EXEC sp_set_session_context N'app.tenant_context_mode', NULL;
```

A table whose values are not all `NULL`, `Guid.Empty` or its parent (see "Which parent") would stop the migration with 50039.

## Test fixtures

The integration fixtures wrote creator ids that point at nothing. They were fixed; no test was weakened:

- Meetings: about 30 calls passed `Guid.NewGuid()` as the organizer's or actor's user id (create, edit, split, move, cancel, attendees). They now pass that employee's real user id (new `MyTeamDb.UserIdOf(employeeId)`). `MeetingSeed` writes the organizer's user id into `meeting_series.created_by_id`, the occurrence-writer tests pass the series creator (as `MeetingOccurrenceGenerator` does), and the schema test's raw task insert writes the organizer's user id.
- `EmployeeDetailAndChangePositionIntegrationTests` adds the admin caller's users row.
- The position-assignment and hierarchy-closure tests pass `Guid.Empty` (now stored as `NULL`) instead of a random creator.
- `EfCalendarEventRepositoryTests` (unit) uses an authenticated caller.

## Gate

`verify.ps1 -Full`, folder `dev-env/reports/improve-task-39c-audit-fks/verify-20261124-171316`:

| Step | Result |
|---|---|
| build | PASS |
| unit | 8371 passed, 0 failed |
| architecture | 841 passed, 0 failed |
| integration | 1405 passed, 0 failed, 1 skipped |

Earlier runs on this branch:

- Integration run 1: 900 failures. 880 came from `wm_refresh_module_hour_summary` inserting `Guid.Empty` (fixed in the migration). The rest came from fixtures with random creators and from `clock_in_policies` (made nullable).
- Integration run 4: 31 Meetings failures, all real (they failed again when run alone): random or employee ids used as creators. Fixtures fixed; the Meetings folder then passed 236 of 236.
- `verify-20261124-170708`: architecture failed on `PaymentGatewayRules_StayInApplicationLayerExceptForProviderCatalogForeignKey` (real, failed again when run alone). The repair now leaves the payment gateway tables out (see "Hand-written SQL").
- Two other runs did not count: one was cut off by a shell fork error, and one ran while Docker Desktop was stopped.
