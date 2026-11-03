# Notification Permission Deduplication Report

## What was duplicated

The backend defined two overlapping customer-facing permissions for notification management:

- `notifications:manage` — defined in `PermissionSeeder.cs` under the `notifications` module, and owned by the `notifications` module in `ModuleCatalogSeeder.cs`.
- `settings:notifications` — defined in `PermissionSeeder.cs` under the `configuration` module, but owned by the `notifications` module in `ModuleCatalogSeeder.cs` (a metadata inconsistency between the two seeders). Both frontend route/nav configs already listed both codes together in an OR-permission check, meaning frontend gating never actually depended on `settings:notifications` alone.

## Which permission was kept / removed

- **Kept**: `notifications:manage` — canonical Phase 1 permission for notification templates, delivery settings, channels, and notification admin configuration. Not renamed.
- **Removed**: `settings:notifications` — fully removed from seed definitions and retired via migration (not merely left inert).

## Part A — Usages found (before editing)

Searched both repos for `settings:notifications` and `notifications:manage`. Confirmed usages:

| File | Usage |
|---|---|
| `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs:161` | Defined `settings:notifications` under module `configuration` |
| `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs:239` | Owned `settings:notifications` under module `notifications` (inconsistent with above) |
| `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs:238` | Owned `notifications:manage` under module `notifications` (unchanged) |
| `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs:153` | Defined `notifications:manage` under module `notifications` (unchanged) |
| `Hrms--Web-application---front-end---v1/src/app/app.routes.ts:58` | Route guard permissions array: `['settings:notifications', 'notifications:manage']` (OR logic) |
| `Hrms--Web-application---front-end---v1/src/app/layouts/main-layout/nav/nav-items.config.ts:107` | Nav item `requiredPermissions`: `['settings:notifications', 'notifications:manage']` (OR logic) |
| `Hrms--Web-application---front-end---v1/src/app/layouts/main-layout/nav/nav-access.ts:13` | Doc comment example listing `settings:notifications` |

Checked and confirmed **no references** to `settings:notifications` in:
- `RoleTemplateSeeder.cs` — only seeds "HR Manager" / "Workspace Member" templates, neither references notifications permissions.
- `DefaultRoleSeeder.cs` — derives Owner-role grants dynamically from `ModuleCatalogSeeder` ownership + `PermissionSeeder` definitions (no hardcoded permission codes), so it needed no direct edit.
- `DevSmokeTestTenantSeeder.cs` — no permission-code literals at all (grants come from module entitlements).
- Any controller `RequirePermission` attribute in `src/`.
- Any backend test file.
- Any frontend `.spec.ts` file.

No notification settings UI component exists yet — both frontend references were placeholder route/nav entries (`loadPlaceholder`).

## Part B — Backend changes

1. `PermissionSeeder.cs`: removed the `Perm("settings:notifications", ...)` line from `GetAllPermissions()`.
2. `ModuleCatalogSeeder.cs`: removed the `{ Module = "notifications", Perm = "settings:notifications" }` ownership entry. `notifications:manage` ownership entry unchanged.
3. No role/template/dev-smoke grant referenced `settings:notifications`, so no replacement was needed there.
4. `notifications:manage` was not removed or modified.
5. No new permission was introduced.
6. **DB cleanup migration added**: `20260806082829_RetireSettingsNotificationsPermission.cs`. This project has a direct precedent for this exact pattern — `20260713061128_RetireIntegrationsReadPermission.cs` — which was followed exactly:
   - `DELETE FROM module_permission_ownership WHERE permission_code = 'settings:notifications';` (no FK from this table to `permissions`, so it's cleaned up explicitly first).
   - `UPDATE role_templates SET permission_codes_json = permission_codes_json - 'settings:notifications' WHERE permission_codes_json ? 'settings:notifications';` (defensive — covers any tenant-created role template that may reference it, even though the seeded system templates never did).
   - `DELETE FROM permissions WHERE code = 'settings:notifications';` — this cascades automatically to `role_permissions` and `user_permission_overrides` because both carry `ON DELETE CASCADE` on their `permission_id` FK to `permissions.id` (see `PermissionConfiguration.cs` / `UserPermissionOverrideConfiguration.cs`), so no explicit DELETE was needed for those two tables.
   - `Down()` is intentionally one-way (raises an exception), matching the precedent migration, because deleted tenant role grants and user overrides cannot be safely reconstructed.
   - Applied successfully against the local dev Postgres database via `dotnet ef database update` (also picked up two other pending unrelated migrations already queued).
7. `notifications:manage` continues to be granted to Owner via `DefaultRoleSeeder`'s dynamic module-entitlement + `PlatformBaselineModules` mechanism — unchanged and still covered by `DefaultRoleSeederTests.cs`'s existing baseline-bootstrap test.

## Part C — Frontend changes

1. `app.routes.ts`: `Notification Settings` route's `permissions` data narrowed from `['settings:notifications', 'notifications:manage']` to `['notifications:manage']`.
2. `nav-items.config.ts`: `Notification Settings` nav item's `requiredPermissions` narrowed the same way.
3. `nav-access.ts`: updated a doc-comment example that referenced `settings:notifications` to `notifications:manage` (no behavioral code change, comment only).
4. No unrelated Settings pages were touched (General Settings still gated on `legal_entity:update`, Roles & Permissions still on `roles:manage`).
5. No notification settings UI exists yet (placeholder route) — confirmed zero remaining `settings:notifications` references anywhere under `Hrms--Web-application---front-end---v1/src`.

## Part D — Tests

- `PermissionSeederTests.cs`: added assertions that seeded codes do not contain `settings:notifications` and do contain `notifications:manage`.
- `ModuleCatalogSeederTests.cs`: seeded a pre-existing `settings:notifications` permission row (simulating a database that hasn't run the retirement migration yet) and a `notifications:manage` row, then asserted the seeder does **not** produce a `settings:notifications` ownership row and **does** produce `notifications:manage` owned by the `notifications` module.
- `DefaultRoleSeederTests.cs` and `RoleTemplateSeeder`/`DevSmokeTestTenantSeeder` were not modified: neither ever referenced `settings:notifications` (confirmed by search in Part A), so there was no regression surface requiring new assertions there — `DefaultRoleSeederTests.cs` already asserts `notifications:manage` reaches Owner via the baseline-bootstrap mechanism.
- No dedicated unit test was added for the migration's raw SQL itself — this codebase has no precedent for unit-testing migration SQL (Postgres-specific `jsonb ?`/`-` operators aren't supported by the SQLite/InMemory providers used in this test suite for other seeders). Instead the migration was verified by actually applying it (`dotnet ef database update`) against a real local Postgres database, which succeeded cleanly.

## Test/build results

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore` | Build succeeded, 0 errors |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore` | Passed: 1422, Failed: 0 |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore` | Passed: 536, Failed: 0 |
| `npm test -- --watch=false` (frontend) | 54 test files, 262 tests passed |
| `npm run build` (frontend) | Build succeeded |
| `npm run build:staging` (frontend) | Build succeeded |
| `git diff --check` (both repos) | No errors (only pre-existing CRLF/LF line-ending warnings on files unrelated to this task) |

## Files changed

**Backend:**
- `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`
- `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs`
- `src/ONEVO.Infrastructure/Migrations/20260806082829_RetireSettingsNotificationsPermission.cs` (new)
- `src/ONEVO.Infrastructure/Migrations/20260806082829_RetireSettingsNotificationsPermission.Designer.cs` (new, generated)
- `tests/ONEVO.Tests.Unit/Features/Auth/PermissionSeederTests.cs`
- `tests/ONEVO.Tests.Unit/Features/Auth/ModuleCatalogSeederTests.cs`

**Frontend:**
- `src/app/app.routes.ts`
- `src/app/layouts/main-layout/nav/nav-items.config.ts`
- `src/app/layouts/main-layout/nav/nav-access.ts` (comment only)

## Remaining risks

- Any already-provisioned tenant database that has **not yet run** the `RetireSettingsNotificationsPermission` migration will still have an active `settings:notifications` permission row (and any role grants/overrides referencing it) until that migration is applied. The migration is one-way by design — reversing it is not supported, matching the `RetireIntegrationsReadPermission` precedent.
- No production/staging databases were touched — the migration was only applied to the local dev database referenced in this repo's `.env`.
- The two placeholder frontend routes (General/Notification Settings) still have no real component behind them (`loadPlaceholder`), so there is no live notification-settings screen to smoke-test end-to-end yet.

## Not committed or pushed by this task

This task did not run `git commit` or `git push` at any point.

**However**, during this session a commit (`50c3837`, "legal entity backend changes") was created and pushed to `origin/feature/mkcert-tenant-subdomain-https` by an external process (not this task's tool calls — most likely a parallel terminal/IDE session on your machine). That commit bundled this task's notification-permission changes together with pre-existing, unrelated, already-uncommitted legal-entity work. This was flagged to you mid-task and you asked me to leave it as-is rather than perform git surgery. All build/test verification above was run against the current (now-committed) working tree state and is accurate regardless.
