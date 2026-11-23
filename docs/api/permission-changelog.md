# Tenant permission changelog

One line per change to a tenant permission code: added, renamed, default-granted, soft-retired or
removed. Newest first. The code side of each change is in `PermissionCatalog`
(`src/ONEVO.Application/Common/Authorization/PermissionCatalog.cs`); how to make one is in
[permission-lifecycle-checklists.md](../development/permission-lifecycle-checklists.md).

Each database also keeps its own record of what was applied to it, in the `permission_changes` table
(change key, tenants and roles touched, grant counts, time).

| Date | Code | Change | Why |
|---|---|---|---|
| 2026-11-19 | (all) | Lifecycle added (task 33). Renames now update the row in place, new codes can declare a one-time default grant, and retiring a code that is still granted is refused unless the entry sets `DropGrants` | A rename used to delete every tenant's grants by cascade |
| 2026-11-19 | `agent:*`, `projects:access`, `projects:read`, `tasks:*`, `time:*`, `okr:*`, `wiki:*`, `sprints:*`, `workspaces:*`, `resources:*`, `roadmaps:*` | Removed (recorded; already removed before) | Work Management moved to module entitlement plus project relationships |
| 2026-11-19 | `settings:read`, `settings:admin`, `settings:billing`, `settings:branding`, `settings:integrations`, `settings:alerts`, `settings:system` | Removed (recorded; already removed before) | Never enforced. Duplicates of `billing:manage`, `legal_entity:update`, `legal_entity:read` |
| 2026-11-19 | `employees:read`, `employees:read-team`, `attendance:read-team` | Removed (recorded; already removed before) | Employee visibility is relationship-based (management coverage) |
| 2026-11-19 | `invitations:manage` | Removed (recorded; already removed before) | Resend/revoke is allowed for the person who added the employee, or `users:manage` |
| 2026-11-19 | `inbox:read`, `notifications:read`, auto-granted codes | Listed in the catalogue as Derived / AutoGranted (task 32) | One catalogue for every code; no `permissions` rows change |
| 2026-11-13 | `checklists:manage` | Added; given to every role that held `employees:write` (migration `GrantChecklistsManageToEmployeeWriters`) | Checklist templates split out of `employees:write`. Today this would be a `DefaultGrant` with `HoldersOfCode(employees:write)` |
