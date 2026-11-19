# Task 42: Concurrency tokens, identity columns and unused tables

| | |
|---|---|
| Priority | 42 of 42 (lower number = build first). Added 2026-11-15 |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day. Split with a suffix (a, b) if the migration work does not fit |
| Area | DB, Architecture |
| Depends on | nothing |

## What we found

Review: `docs/reports/database/2026-11-15-database-design-review.md`, sections A6, C7, C9 and C11.

- **Lost updates.** 9 tables have a `rowversion` column. 125 tables have `updated_at`, which means they are edited. On the other 116, when two people save the same row, the second save silently replaces the first.
- **Two identities for one person.** `task_assignments`, `invitation_tokens` and `access_grant_requests` store both `user_id` and `employee_id`. `employee_check_ins` uses `user_id` while the tables next to it use `employee_id`.
- **`task_assignments` is one-to-one with `tasks`.** A unique index on `task_id` means a task can have only one assignment row, so the table works as extra columns of `tasks` reached through a join. 435 of 754 tasks have a row.
- **110 of 219 tables are empty in the dev database.** Most belong to features that have no demo data. Some may no longer be used: `refresh_tokens` (the app uses cookie sessions), `audit_logs`, `task_comment_logs`, `labels`, `job_leases`, `feature_access_grants`, `activity_raw_buffer` are worth a look first.

## What to add and change

1. Concurrency: add a `rowversion` column to the tables that users edit through forms. Start with `tasks`, `objectives`, `sprints`, `projects`, `employees`, `positions`, `departments`, `leave_requests`, `leave_policies`, `clock_in_policies`, `roles`, `legal_entities`, `personal_calendar_events`. Configure it as the EF concurrency token. Return HTTP 409 with the current row when a save loses, and agree with the frontend how to show it.
2. Identity: write the rule in `docs/development/architecture-rules.md`. Business tables refer to a person by `employee_id`. `user_id` is only for login, session and security tables. For the four tables above, keep one column and derive the other, or write down why both are needed.
3. `task_assignments`: decide with the product owner. If a task has one assignee, move `assignee_employee_id`, `assigned_by_employee_id` and `assigned_at` onto `tasks` and drop the table. If several assignees are planned, drop the unique index and make the primary key `(task_id, employee_id)`.
4. Empty tables: for each of the 110, search the code for writes. Sort into three lists: used but no demo data, planned feature, unused. Propose dropping the unused ones, with the entity, configuration, repository and RLS entry.
5. Add a short "how to design a new table" checklist to the same rules file: tenant column with foreign key, tenant-first index, clustered key choice, foreign key for every `_id`, check constraint for every status, real string lengths, `rowversion` if edited, RLS registration.

## Done when

- The named tables reject a stale save with 409 (test).
- The identity rule is written and the four tables each have a decision.
- `task_assignments` has a decision and, if merged, a migration.
- The three lists of empty tables are saved in `docs/reports/database/`.
