# Decisions for the 38 unreferenced permission codes, 2026-11-19

Task 33, step 4. Source: `docs/reports/security/2026-11-15-permission-system-review.md`, problem 5. These 38 codes are
seeded and an admin can tick them, but no endpoint, handler or service checks them (checked again on 2026-11-19 after
task 32: no use of their `PermissionCatalog` constants outside the catalogue and the seeders).

**Status: proposed, needs product owner sign-off.** Nothing in code changes until then. Once a row is signed off, the
change is one line in `PermissionCatalog.cs` (see `docs/development/permission-lifecycle-checklists.md`):

- **Planned** = the module is coming. Change the kind to `SoftRetired` with the reason "planned: <module>". The code
  then leaves the role screen and cannot be granted, and any existing grant is kept for when the module ships, when
  the kind goes back to `Assignable`. Because grants are kept, it needs `DropGrants: true` only if the module will not
  come back.
- **Retire** = remove for good: `SoftRetired` in one release, `Retired` in the next.
- **Merge** = a `PermissionRename` onto the enforced code. Grants move across, nothing is lost.

| Code(s) | Module in catalogue | Proposed decision | Reason |
|---|---|---|---|
| `time_off:read`, `time_off:create`, `time_off:approve`, `time_off:manage` | `time_off` | **Merge** into `leave:read`, `leave:create`*, `leave:approve`, `leave:manage` | Only `leave:*` is enforced. `time_off:*` exists because `ModuleCatalogSeeder` maps the `time_off` module to these codes. Do it in one change: move the module ownership in `ModuleCatalogSeeder` to the `leave:*` codes, then declare the four renames, so every tenant's grants move to the enforced codes. \*Check the matching leave code for `create` (requests are created with `leave:read-own` auto-grant today) |
| `analytics:read`, `analytics:view`, `analytics:write`, `analytics:export` | `analytics` | **Planned** | `analytics` is a phase 1 module in the module catalogue, but has no backend feature yet. `analytics:read` and `analytics:view` are duplicates: keep `analytics:view` (it is the module-owned one) and **retire** `analytics:read` |
| `reports:read`, `reports:create` | `reports` | **Retire**, or **merge** into `analytics:view` / `analytics:write` | The module catalogue has "Analytics & Reports" as one module; separate `reports:*` codes would split it |
| `verification:view`, `verification:configure` | `verification` | **Planned** | Phase 1 module (Identity Verification). `verification:review` is already enforced; these two belong to screens not built yet |
| `workforce:view`, `workforce:manage` | `workforce` | **Planned** | Workforce Intelligence pillar; no feature yet |
| `chat:read`, `chat:write`, `chat:manage` | `chat` | **Retire** | The existing `Conversations` feature does not check them. Product owner to confirm no chat module with its own codes is planned |
| `documents:read`, `documents:write`, `documents:manage`, `documents:admin` | `documents` | **Planned** | No documents module yet. `documents:admin` duplicates `documents:manage`: retire it when the module is designed |
| `expense:read`, `expense:create`, `expense:manage` | `expense` | **Planned** | No expense module yet. |
| `grievance:read`, `grievance:write` | `grievance` | **Planned** | No grievance module yet |
| `payroll:read`, `payroll:write` | `payroll` | **Planned** | Payroll is not built; other `payroll:*` codes are in the same state |
| `performance:read`, `performance:read-team` | `performance` | **Planned** | No performance module yet. `read-team` should become relationship-based like employee visibility; decide when the module is designed |
| `skills:read`, `skills:write`, `skills:write-team`, `skills:manage`, `skills:validate` | `skills` | **Planned** | No skills module yet |
| `settings:device`, `settings:device:configure` | `settings` | **Retire** | No device settings screen checks them; same reasoning as the earlier `settings:*` retirements. Product owner to confirm device settings will use the monitoring codes |
| `employees:delete` | `core_hr` | **Retire** | Employees are offboarded (`employees:offboard`) and archived, never hard-deleted |

Count: 4 merge, 25 planned, 9 retire (`analytics:read`, `reports:*` ×2, `chat:*` ×3, `settings:device*` ×2, `employees:delete`) = 38.

## `time_off:*` versus `leave:*`

Keep `leave:*`. It is what every endpoint checks and what existing roles hold. Merging `time_off:*` into it through
renames keeps any grant a tenant made on the `time_off` codes. Doing the reverse (renaming every `leave:*` check to
`time_off:*`) touches far more code and the frontend for no user-visible gain.
