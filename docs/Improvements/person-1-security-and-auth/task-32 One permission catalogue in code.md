# Task 32: One permission catalogue in code

| | |
|---|---|
| Priority | 32 of 34 (lower number = build first). Added 2026-11-15. Do this before adding any new permission |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | Security, Architecture |
| Depends on | nothing |

## What we found

Full review: `docs/reports/security/2026-11-15-permission-system-review.md`.

- Permission codes are plain text in every place they are used: 242 `[RequirePermission]` and `[RequireAnyPermission]` attributes and 105 inline `HasPermission("...")` calls. There is no constants class.
- A typo compiles. The endpoint then rejects everyone except a user with `*`, and no test notices.
- One permission is described in up to six separate places: `PermissionSeeder`, `ModuleAutoGrants`, `DerivedPermissions`, `RoleTemplateSeeder`, `ModuleCatalogSeeder` and the frontend.
- The seven auto-granted codes (for example `leave:read-own`, `attendance:read-own`, `calendar:read`) are enforced by endpoints but have no row in the `permissions` table.
- `DerivedPermissions` refers to `workflows:execute`, which is not in the catalogue at all.

## What to do

1. Create one static catalogue in `ONEVO.Application`, for example `Permissions`, grouped by module: `Permissions.Leave.Approve = "leave:approve"`. Each entry carries code, module, description and a kind: assignable, auto-granted or derived.
2. Make `PermissionSeeder` build its list from the catalogue instead of its own `Perm(...)` lines.
3. Make `ModuleAutoGrants` and `DerivedPermissions` refer to catalogue entries, not text. Put the auto-granted and derived codes in the catalogue too, marked so the role screen shows them as "given automatically" and does not let an admin tick them.
4. Replace the text in attributes and `HasPermission` calls with the constants. Do it one module per commit; the string values do not change, so behaviour does not change.
5. Add an architecture test: every permission argument of `[RequirePermission]`, `[RequireAnyPermission]` and `HasPermission` is a catalogue constant, never a literal.
6. Add a test: every code in `RoleTemplateSeeder`, `ModuleCatalogSeeder`, `ModuleAutoGrants` and `DerivedPermissions` exists in the catalogue.
7. Put module keys and feature keys in the same catalogue (`Modules.X`, `Features.X`). Feature keys are the chosen method for managing API changes (task 43), so they need the same compile-time safety as permission codes.
8. Expose the catalogue as a JSON file or endpoint for the frontend (frontend task 25).

## Done when

- A search for `RequirePermission("` in the API project finds nothing.
- Using a code that is not in the catalogue fails the build or the architecture suite.
- The seeded `permissions` table is identical before and after, apart from the newly listed auto-granted and derived codes.
- Full unit and architecture suites pass.

## Results (2026-11-19)

Branch `claude/improve-task-32-permission-catalogue-f80579`, built on `origin/development` 846b92028.

### What was built

- **Catalogue:** `src/ONEVO.Application/Common/Authorization/PermissionCatalog.cs`.
  - Constants are grouped by code prefix: `PermissionCatalog.Leave.Approve = "leave:approve"`, `PermissionCatalog.SuperAdmin = "*"`, and `PermissionCatalog.Retired.*` for retired codes.
  - Each code also has a `PermissionDefinition(Code, Module, Description, Kind)`. `Kind` is `Assignable`, `AutoGranted`, `Derived`, `NotSeeded` or `Retired`.
  - Lookups: `All`, `Seeded`, `RetiredCodes` and `Find(code)`.
  - Task 33 can add rename and retire metadata to `PermissionDefinition` as optional members.
  - The class is named `PermissionCatalog`, not `Permissions`, to match `PlatformPermissionCatalog` and to avoid clashing with members such as `db.Permissions`.
- **Seeder:** `PermissionSeeder` writes `PermissionCatalog.Seeded` and retires `PermissionCatalog.RetiredCodes`. Its own `Perm(...)` lines and retired list are gone.
  - The 85 seeded rows keep the same code, module and description text, including the existing mis-encoded dash in the `*` description, so the `permissions` table does not change.
  - Auto-granted, derived and not-seeded codes are listed in the catalogue but **not** seeded. Seeding them, and showing them as "given automatically" on the role screen, changes the role screen and needs a kind column, so it is left to task 33 or frontend task 25.
- **Replacement:** every permission code text in `src` (outside migrations) now names a constant. That covers 140 source files and 421 constant references.
  - It includes `[RequirePermission]`, `[RequireAnyPermission]`, `HasPermission` and `UserHasPermissionCodeAsync` calls, local permission constants in handlers, `ModuleAutoGrants`, `DerivedPermissions`, `PermissionResolver`, `ModuleCatalogSeeder`, `DapiOrgStructureData`, `DevSmokeTestTenantSeeder` and the `"*"` checks.
  - The HR Manager role template JSON is serialized from the constants and keeps the same stored text: `["attendance:read","leave:approve","leave:manage","leave:read"]`.
  - `RequirePermission("` now finds nothing in the API project.
- **Guards:** `tests/ONEVO.Tests.Architecture/Security/PermissionCatalogArchitectureTests.cs` (10 tests) fails on:
  - a permission code literal anywhere in `src` outside the catalogue (migrations excluded);
  - any string literal passed to `RequirePermission`, `RequireAnyPermission`, `HasPermission` or `(User)HasPermissionCodeAsync`, which also catches a typo;
  - use of a retired constant, apart from one allowed case (see below);
  - an endpoint attribute code that is unknown, retired or not seeded (checked by reflection);
  - a catalogue constant without a definition, a definition without a constant, or a duplicate code;
  - the seeder writing anything other than the assignable catalogue entries;
  - an auto-grant, a derived trigger or a seeded role code list (`RoleTemplateSeeder`, `DapiOrgStructureData`) using a code the catalogue does not know, or one of the wrong kind;
  - a change to the stored HR Manager template JSON.

  I checked the guards by adding a probe with `"leave:read"` and `HasPermission("leave:raed")`: two guards failed as expected, and the probe was then reverted.

### Mismatches found (reported, not changed)

| Code | Where | Problem |
|---|---|---|
| `projects:access` | `WorkApprovalEngine.HrFallbackPermission` | This code is **retired**, so no user holds it. The value is kept and is the one allowed retired use in the guard. Task 33 should pick the replacement. |
| `monitoring:alerts:read`, `monitoring:alerts:resolve`, `workflows:execute` | `DerivedPermissions` trigger lists | No `permissions` row and no endpoint checks them. Marked `NotSeeded`. |
| `employees:read-own`, `attendance:read-own`, `attendance:write-own`, `leave:read-own`, `workforce:dashboard` | `ModuleAutoGrants` | Auto-granted with no `permissions` row. Marked `AutoGranted`, still not seeded. |
| `calendar:read`, `activity:read:self` | `ModuleAutoGrants` | Auto-granted **and** seeded as assignable. Left as is. |
| `inbox:read`, `notifications:read` | `PermissionResolver` | Derived, with no row. Marked `Derived`. |
| (count) | review report | The seeder has 85 codes, not the 83 the review reports. |

### Not done (follow-ups)

- Step 7 of "What to do": module and feature keys (`Modules.X`, `Features.X`) are not in the catalogue yet.
- Step 8 of "What to do": expose the catalogue as JSON for the frontend. This changes the API shape, so it belongs with frontend task 25.
- Seed auto-granted and derived rows with a kind flag, so the role screen can show them as given automatically. This is task 33 or frontend task 25.
- Developer Platform codes (`PlatformPermissionCatalog`) were already constants and are unchanged.

### Verification

- Targeted runs: the 10 PermissionCatalog architecture tests pass, and the unit tests filtered on Auth, Permission and Role pass (1172).
- `find-unauthorized-endpoints.ps1`: 0 actions without an authorization attribute. The 32 explicit `[AllowAnonymous]` actions are unchanged by this task.
- Full gate (`verify.ps1 <worktree> -Full`): PASS. build PASS; unit 8019 passed, 0 failed; architecture 723 passed, 0 failed; integration 1251 passed, 0 failed, 1 skipped. Logs: `dev-env/reports/improve-task-32-permission-catalogue-f80579/verify-20261119-193730`. An earlier run (`verify-20261119-193145`) failed one unit test, `WidgetDataOrchestratorTests.Slow_provider_times_out_into_error`; it passed 3 of 3 solo runs and in the full re-run, so it is a timing flake unrelated to this change.
