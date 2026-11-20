# Task 39f: Remaining status and type check constraints

| | |
|---|---|
| Priority | 39 of 42, after task 39a. Added 2026-11-19 |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | DB, Security |
| Depends on | task 39a (inventory test and first slice) |

## What is left

`tests/ONEVO.Tests.Architecture/Schema/missing-check-constraints.txt` lists 104 columns: 98 `todo` and 6 `free` (MIME types, which stay open). `permission_changes.kind` came in with task 33 after 39a was built; its values (`rename`, `default-grant`, `soft-retire`, `remove`) are string literals in `PermissionCatalogSynchronizer`.

Order:

1. The review workflow tables of task 37: `access_grant_requests`, `attendance_corrections`, `location_change_requests`, `work_area_change_requests`, `device_change_requests`, `offboarding_task_bypass_requests`, `seat_increase_requests`, `wm_approval_requests`, `project_member_invitations`.
2. The type columns next to polymorphic ids: `wm_approval_requests.target_type`, `wm_monitor_alerts.target_type`, `wm_notification_log.target_type`, `notifications.related_entity_type`, `notification_deliveries.related_entity_type`, `entity_assets.owner_type`.
3. The rest.

Notes:

- If the values exist only as string literals in code, add a constants class or enum first. Grep seeders and raw SQL (`INSERT INTO`, `UPDATE ... SET status`) for values outside it.
- A column that holds open values (a provider's event type, a .NET type name such as `outbox_messages.type`) moves to group `free` with a reason.

## How to work

- Add rules to `src/ONEVO.Infrastructure/Persistence/Integrity/StatusCheckConstraints.cs`. The allowed values are read from the enum or constants class. `StatusCheckConstraintTests` fails if code later gains a value that the migrations do not allow.
- Delete each fixed line from the allow-list.
- Create the migration with `dotnet ef migrations add`, and do not apply it to a shared database. If a row holds an old value, the migration fails as a whole. Report it, and do not change the data silently.

## Done when

- Every `todo` line is gone: the column has a constraint, or is `free` with a reason.
