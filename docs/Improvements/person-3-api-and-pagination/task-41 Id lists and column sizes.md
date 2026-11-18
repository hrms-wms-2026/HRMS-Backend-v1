# Task 41: Id lists and column sizes

| | |
|---|---|
| Priority | 41 of 42 (lower number = build first). Added 2026-11-15 |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day. Split with a suffix (a, b) if the migration work does not fit |
| Area | DB, Code quality |
| Depends on | nothing |

## What we found

Review: `docs/reports/database/2026-11-15-database-design-review.md`, sections B and C8.

- **Lists of ids stored as text.** `clock_in_policies.department_ids`, `.employee_ids`, `.position_ids`; `user_dashboards.applied_role_ids`; `tenant_subscriptions.selected_modules`. All are `nvarchar(max)`. No foreign key is possible, no index is possible, and a deleted department stays in the list. To answer "which clock-in policy applies to this employee" the application loads policies and searches the text. The earlier code scan found `ClockInPolicyScopeMembershipValidator` calling the database inside a loop for exactly this.
- **151 `nvarchar(max)` columns.** 56 hold JSON and 52 hold long text, which is fine. 43 hold other things that have a natural length: `attendance_records.expected_work_mode_name`, `notification_templates.mail_subject_template`, `sprints.goal`, `exceptions.outcome`, `meeting_signals.process_name`, `platform_auth_events.user_agent`, several `error_message`, `last_error` and `scopes` columns, and encrypted token columns.
- **`tenant_subscriptions` has 45 columns and 29 are nullable.**
- Small value lists are also stored as text: `legal_entities.standard_working_days`, `leave_types.accepted_document_types`.

## What to add and change

1. Clock-in policy scope: add `clock_in_policy_scope_members` with `tenant_id`, `clock_in_policy_id`, `member_type` (department, position, employee), `member_id`. Primary key on `(clock_in_policy_id, member_type, member_id)`. Index on `(tenant_id, member_type, member_id)` so "policies for this employee" is one seek. Check constraint on `member_type`. Copy the lists, switch the code, drop the three text columns one release later.
2. Dashboard roles: add `user_dashboard_roles` (`user_dashboard_id`, `role_id`) with foreign keys to both. Removing a role then removes its link.
3. Subscription modules: add `tenant_subscription_modules` (`tenant_subscription_id`, `module_key`) with a foreign key to the module catalogue.
4. Small fixed lists (`standard_working_days`, `accepted_document_types`): keep as text, but add `CHECK (ISJSON(column) = 1)` and a real length.
5. All JSON columns: add `CHECK (ISJSON(column) = 1)` so broken JSON cannot be stored. Do the 10 most used first.
6. The 43 other columns: set real lengths, for example names 200, subject 300, user agent 512, error messages 2000. Truncate on write in code where the source is outside your control.
7. `tenant_subscriptions`: group the columns by purpose in a table in the pull request (plan, seats, billing cycle, trial, gateway). Propose a split only if a group is null for most rows. Do not split in this task.

## Done when

- "Which clock-in policies apply to employee X" is a single indexed query.
- Deleting a department or a role leaves no stale id behind.
- No column that holds a name, subject or status is `nvarchar(max)`.
- The `tenant_subscriptions` column grouping is written down with a recommendation.
