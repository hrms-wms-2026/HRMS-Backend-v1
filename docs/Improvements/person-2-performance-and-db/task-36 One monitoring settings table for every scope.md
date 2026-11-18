# Task 36: One monitoring settings table for every scope

| | |
|---|---|
| Priority | 36 of 42 (lower number = build first). Added 2026-11-15 |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day. Split with a suffix (a, b) if the migration work does not fit |
| Area | DB, Code quality |
| Depends on | nothing |

## What we found

Review: `docs/reports/database/2026-11-15-database-design-review.md`, section A2.

Three tables hold the same list of monitoring switches:

| Table | Scope | Columns shared |
|---|---|---|
| `monitoring_feature_toggles` | `legal_entity_id` | 13 of 14 |
| `monitoring_policy_overrides` | `scope_type` + `scope_id` | 15 of 17 |
| `employee_monitoring_overrides` | `employee_id` | 15 of 17 |

The switches are `activity_monitoring`, `application_tracking`, `document_tracking`, `communication_tracking`, `screenshot_capture`, `auto_screenshot_capture`, `meeting_detection`, `device_tracking`, `work_location_verification`, `identity_verification`, `biometric`, `idle_threshold_minutes`, `allowed_radius_meters`. `employee_monitoring_overrides` has one more, `photo_required`.

A new monitoring feature needs the same column added in three migrations, three entity classes and the resolver (`MonitoringToggleResolverService`, where the duplicate scan already found repeated blocks).

## What to add and change

1. Keep one table, `monitoring_settings`: `id`, `tenant_id`, `scope_type` (`tenant`, `legal_entity`, `department`, `position`, `employee`), `scope_id`, the switch columns, `override_reason`, `set_by_id`, audit columns.
2. Decide the meaning of "not set at this level". For the base (legal entity) row every switch has a value. For override rows a switch is nullable and null means "inherit". Write this in the migration comment and the entity summary.
3. Add a unique index on `(tenant_id, scope_type, scope_id)`.
4. Add a check constraint on `scope_type` for the allowed values.
5. Add `photo_required` to the shared list so all scopes have the same columns.
6. Migration: copy the legal entity rows with `scope_type = 'legal_entity'`, the employee rows with `scope_type = 'employee'`, and the existing policy override rows as they are. Keep the old tables for one release.
7. Rewrite the resolver as one ordered lookup: employee, then position, department, legal entity. One query with `scope_id IN (...)`, not one query per level.
8. Register the table for RLS.

## Done when

- Adding a monitoring switch is one column in one table.
- The resolver returns the same effective settings before and after for the test tenants (write this test first, against the old tables).
- The old tables are dropped in a later migration after one release.
