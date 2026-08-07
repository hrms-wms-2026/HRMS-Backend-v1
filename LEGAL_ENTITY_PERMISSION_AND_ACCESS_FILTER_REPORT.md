# Legal Entity Permissions & Accessible-Company Filter — Report

**Scope:** Replace the broad `org:manage` gate on Legal Entity create/update/delete/general-settings/logo with dedicated `legal_entity:create/update/delete` permissions, and replace "return every legal entity in the tenant" with an accessible-company filter for `GET /api/v1/org/legal-entities`. Work confined to `HRMS-Backend-v1`.

---

## 1. Permissions added

Three new permission codes, all owned by the existing `org_structure` module (the same module `org:read`/`org:manage` already live in):

| Code | Description | Module |
|---|---|---|
| `legal_entity:create` | Create a legal entity (company) inside the tenant. | `org_structure` |
| `legal_entity:update` | Edit a legal entity's general settings. | `org_structure` |
| `legal_entity:delete` | Deactivate (soft-delete) a legal entity. | `org_structure` |

Added to `PermissionSeeder.GetAllPermissions()` and to `ModuleCatalogSeeder.SeedPermissionOwnershipAsync`'s `org_structure` ownership block. Both seeders are idempotent by construction (existing merge-by-code / merge-by-permission-code logic, unchanged) — no new idempotency code was needed. `org:read`/`org:manage` were left completely untouched.

No `legal_entity:read` permission was added. The company selector (`GET /api/v1/org/legal-entities`) stays gated by the pre-existing `org:read`, with the actual visibility narrowed inside the handler (see §4) — a broad `legal_entity:read` would have re-opened exactly the "see every company" hole this task closes.

## 2. Seeded roles changed

**No seeder code changes were required for the Owner grant.** `DefaultRoleSeeder.SeedDefaultRolesAsync` grants a tenant's Owner role every permission whose `Module` is in the tenant's subscribed module list (or the platform baseline), excluding `*` and `ModuleAutoGrants` entries. Because the three new permissions are owned by `org_structure` — a module every tenant that has legal entities already subscribes to (it's what makes `org:read`/`org:manage` reach Owner today) — Owner automatically receives `legal_entity:create/update/delete` the moment they exist in the catalog. This is proven by a new test, `SeedDefaultRolesAsync_GrantsLegalEntityPermissionsToOwner_WhenOrgStructureModuleIncluded` (`DefaultRoleSeederTests.cs`), mirroring the pre-existing `org:read`/`org:manage` grant test.

**HR Manager / Work Manager (dev-smoke roles) were deliberately left untouched.** `DevSmokeTestTenantSeeder.HrManagerPermissionCodes` (`org:read, org:manage, employees:read, employees:write, roles:read`) and `WorkManagerPermissionCodes` (`org:read, employees:read, projects:read, tasks:read, tasks:write`) are explicit, hardcoded lists — they do not include any `legal_entity:*` code, so neither role gains it. This satisfies "no accidental grant to normal HR Manager / Work Manager." The dev-smoke "Tenant Owner" role, by contrast, is seeded with every currently-seeded permission except `*` (`DevSmokeTestTenantSeeder.ResolveRolePermissionsAsync`), so it picks up the three new codes automatically on the next seeder run — no edit needed there either.

No role-seeding source file was modified. Only the permission *catalog* (Task 1 files below) changed.

## 3. Before/after endpoint permission table

| Endpoint | Before | After |
|---|---|---|
| `GET /api/v1/org/legal-entities` | `org:read` | `org:read` (unchanged — visibility is now filtered inside the handler, not by the attribute) |
| `GET /api/v1/org/legal-entities/{id}/general-settings` | `org:manage` | `legal_entity:update` |
| `POST /api/v1/org/legal-entities` | `org:manage` | `legal_entity:create` |
| `PUT /api/v1/org/legal-entities/{id}/general-settings` | `org:manage` | `legal_entity:update` |
| `DELETE /api/v1/org/legal-entities/{id}` | `org:manage` | `legal_entity:delete` |
| `DELETE /api/v1/org/legal-entities/{id}/logo` | `org:manage` | `legal_entity:update` (logo implementation itself unchanged) |

`org:manage` still gates Department/Position management and general org navigation elsewhere in the codebase — nothing there was touched.

## 4. Accessible-company filtering rule

`ListLegalEntitiesQueryHandler` no longer calls a "list every legal entity in tenant" method unconditionally. It computes:

```csharp
hasManagementAccess = currentUser.HasPermission("legal_entity:update")
                    || currentUser.HasPermission("legal_entity:delete");
```

(deliberately **not** `org:manage` — a user can have broad org-management rights, e.g. the dev-smoke HR Manager, without being allowed to see every company) and calls one new repository method, `ILegalEntityRepository.ListAccessibleAsync(tenantId, userId, hasManagementAccess, includeInactive, ct)`:

- **`hasManagementAccess == true`** (tenant owner / admin-level legal-entity manager): returns every legal entity in the tenant, active-only unless `includeInactive=true`.
- **`hasManagementAccess == false`** (regular user): resolves the caller's own **active** `employees` row (joined to `employment_statuses` on `code == "active"`, scoped to the same tenant) and returns at most that one legal entity — and only if it is itself active. `includeInactive` is **ignored** on this branch; a regular user can never use the query flag to discover an archived company.
- **No active employee row** → empty list (not "all tenant legal entities," not an error).
- Tenant isolation is enforced throughout — every branch filters by `tenantId` first.

The old `ILegalEntityRepository.ListByTenantAsync` method (which returned every row for a tenant with no accessibility check) was deleted rather than left unused, so nothing in the codebase can accidentally reintroduce the "list everything" behavior by calling it.

## 5. Current limitation

`Employee.UserId` is unique — one user maps to exactly one employee row, and therefore exactly one `legal_entity_id`. A regular (non-management) user can access exactly **one** legal entity today, whichever their single active employee row points at. This is a known, accepted constraint for this task, not a bug.

## 6. Deployment note (permission backfill gap)

`DefaultRoleSeeder` grants permissions to a tenant's Owner role **once, at tenant-creation time**, from whatever the permission catalog contains at that moment. Adding `legal_entity:*` to the catalog does **not** retroactively grant it to already-provisioned tenants' Owner roles — `RolePermission` rows are not recomputed from the catalog on each request (`PermissionResolver.ResolveAsync` only unions existing `RolePermission` rows with currently-active modules; it does not re-derive from `Permission.Module`). Only tenants created **after** this change automatically get `legal_entity:create/update/delete` on their Owner role via normal provisioning.

This plan did not build a backfill migration — it was out of scope for this task. If any production tenant already exists before this change ships, its Owner role will lose the ability to create/update/delete legal entities (it previously had that ability via `org:manage`) until a manual/migration backfill grants the three new permission codes to existing Owner roles.

The local dev-box `DevSmokeTestTenantSeeder` is unaffected by this gap: its `SeedTenantRoleAsync` does an idempotent additive backfill of missing `RolePermission` rows for existing roles on every application restart, so the seeded Acme/Dapi "Tenant Owner" roles pick up the new permissions automatically the next time the app starts — confirmed by running the full local test suite in this session (which starts the app and re-seeds).

## 7. Future follow-up

The accessible-company query (`ListAccessibleAsync`) should eventually be extended to consult `position_assignments` / a multi-legal-entity authority model, once that model exists, so a single user can legitimately be granted visibility into more than one legal entity without being a tenant-wide admin. Today a user's accessible-company set is hard-limited to the one legal entity their single `employees` row points at (see §5) — this is a temporary simplification, not the intended long-term model per the task brief.

## 8. Verification results

All commands run from `HRMS-Backend-v1`:

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` | **Success**, 0 errors |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal` | **Passed** — 1422/1422 |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal` | **Passed** — 536/536 |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~LegalEntitiesIntegrationTests"` (Docker/Testcontainers) | **Passed** — 25/25, including 6 new accessible-company / permission-matrix tests |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~LegalEntity\|FullyQualifiedName~Auth\|FullyQualifiedName~DevSmoke"` (broader sweep, Docker/Testcontainers, 21m35s) | 127/128 passed. The 1 failure (`BaseForgotPasswordRestrictedRoleHttpIntegrationTests`, unrelated to Legal Entities) failed on a raw Postgres socket connection error during `InitializeAsync()`/Testcontainers startup — a resource-exhaustion flake after dozens of sequential Postgres containers over 21+ minutes, not a code regression. Confirmed by re-running that class alone: **3/3 passed**. |
| `git diff --check` | No errors (only pre-existing LF→CRLF line-ending warnings, not whitespace-conflict errors) |

Two running `ONEVO.Api.exe` dev-server processes were locking the build output at the start of this session (PIDs 52692/57100) and were stopped, with the user's explicit confirmation, before any build/test command could succeed.

## 9. Files changed

Source:
- `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`
- `src/ONEVO.Infrastructure/Persistence/Seeders/ModuleCatalogSeeder.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/LegalEntity/EfLegalEntityRepository.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/ListLegalEntities/ListLegalEntitiesQueryHandler.cs`
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs`

Tests:
- `tests/ONEVO.Tests.Unit/Features/Auth/OrgPermissionSeedTests.cs`
- `tests/ONEVO.Tests.Unit/Features/Tenancy/DefaultRoleSeederTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/EfLegalEntityRepositoryTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/ListLegalEntitiesQueryHandlerTests.cs`
- `tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs`
- `tests/ONEVO.Tests.Integration/OrgStructure/LegalEntity/LegalEntitiesIntegrationTests.cs`

Planning artifact (not code): `docs/superpowers/plans/2026-08-06-legal-entity-permission-access-filter.md`.

**Pre-existing unrelated uncommitted changes found in the working tree, left untouched by this task:** `.postman/resources.yaml`, `postman/environments/New Environment.environment.yaml`, `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs`, `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs`, and an untracked `DEV_SMOKE_ORG_MODULE_PERMISSION_SEED_FIX_REPORT.md` at the repo root. These predate this session (last commit: `1fdb97f` on 2026-08-05) and are unrelated to Legal Entity permissions — flagging so they are not mistaken for this task's output or accidentally swept into a future commit alongside it.

## 10. Explicit scope statement

No frontend repository work, no OneVo-HR docs work, no Postman file work, no logo/upload/asset work, and no country/countries-table work was performed as part of this task. No `git add`/`commit`/`push` was run — all changes above are left staged only in the working tree for the user to review and commit.
