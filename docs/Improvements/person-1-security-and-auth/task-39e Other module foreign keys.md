# Task 39e: Foreign keys in the other modules

| | |
|---|---|
| Priority | 39 of 42, after task 39a. Added 2026-11-19 |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 2 days. Split again (39e1, 39e2) if needed |
| Area | DB, Security |
| Depends on | task 39a (inventory test and first slice) |

## What is left

124 columns marked `task-39e` in `tests/ONEVO.Tests.Architecture/Schema/missing-foreign-keys.txt`. They are in Auth, Calendar, Conversations, Dashboard, DevPlatform, Inbox, Monitoring, SharedPlatform (notifications, integrations) and Storage. Several of the worst tables in the design review are here: `invitation_tokens` (9), `notification_deliveries` (5), and the monitoring agent tables (`agent_device_id`, `device_registration_id`).

Sort each column into one group:

- `reference`: add the key.
- `polymorphic`: make sure the type column next to it has a check constraint (task 39f).
- `external`: not a row in this database. Rename the column if the name misleads.

The desktop agent writes the monitoring tables at high volume. Check that the retention purge (`MonitoringRetentionPurgeRunner`) deletes children before their parents.

## How to work

- Add keys in `src/ONEVO.Infrastructure/Persistence/Integrity/ReferenceForeignKeys.cs`, one method per module. Delete each fixed line from the allow-list.
- Before each key, grep for hard deletes of the parent and for writes of `Guid.Empty`.
- Use NO ACTION for people. Create migrations with `dotnet ef migrations add`, and do not apply them to a shared database.
- Run the full gate with your worktree path and `-Full`.

## Done when

- No `task-39e` line is left. With 39b, 39c and 39d done, fewer than 100 id columns have no key, and each has a reason (the target of task 39).

## Results (2026-11-25)

Report: `docs/reports/database/2026-11-25-task-39efg-module-keys-checks-cascades.md`. Branch `claude/improve-task-39efg` (39e, 39f and 39g together).

- Migration `20261125053716_AddOtherModuleForeignKeys39e`: 107 NO ACTION keys and 92 indexes, one `ReferenceForeignKeys` method per module. Not applied by the agent.
- Its hand-written `RepairOtherModuleReferencesSql` runs first: `Guid.Empty` and ids of the wrong identity table become `NULL` on nullable columns; any other unknown id stops the migration with 50039 naming the columns. Nothing is deleted.
- No `task-39e` line is left. 17 columns stay without a key, each with a reason (`mixed`, `polymorphic`, `external`, `unused`, a hard delete that a key would block, or the template and version save cycle). 34 id columns have no key in total.
- `MonitoringRetentionPurgeRunner` deletes only `activity_raw_buffer` and `monitoring_evidence_assets` rows, which no key points at.
- Bad data: the read-only check `C:\Users\User\Desktop\orphans39efg.sql` (part 1) found one problem on the local database. The Dapi demo seeder's device ids in `activity_snapshots`, `app_usage_snapshots` and `device_state_snapshots` (12281 rows each) had no `tray_device_registrations` row. The repair adds those registrations for the Dapi demo tenant only, other tenants still stop the migration, and `DapiHistorySeeder` now creates them. Everything else was clean.
