# Legal Entity / Company General Settings — Backend Schema Reconciliation Report

Scope: `C:\onevoNew\HRMS-Backend-v1` only. Logo/file-storage/asset ownership code was
intentionally excluded from this task (see explicit statement below). Documentation
source of truth was `C:\onevoNew\OneVo-HR` (read-only for this task).

## 1. Decision checkpoint: `country_id` deferred

**Finding:** The canonical docs (`database/schemas/org-structure.md`,
`database/phase1-table-inventory.md`) define `legal_entities.country_id uuid FK ->
countries`. Auditing this backend found **no `countries` table, no `Country` domain
entity, and no EF configuration for one anywhere in the repository** — confirmed by
searching the full `src` tree, every migration (including `20260519061316_AddLookupTables.cs`,
which adds `employment_statuses` / `employment_types` / `work_modes` / `severities` /
`approval_statuses` lookup tables but not `countries`), and the current
`ApplicationDbContextModelSnapshot.cs`. The only country data anywhere in the backend is a
static 10-entry in-memory dictionary (`GetCountryDefaultsQueryHandler`) used by an unrelated
Developer Platform admin endpoint (`GET /admin/v1/reference/countries/{code}/defaults`),
keyed by ISO alpha-2 codes — disconnected from `LegalEntity` entirely. A code comment in
`CreateLegalEntityCommandHandler.cs` already documents this gap: *"no country-default
helper exists yet (Part 1 audit - no countries table)."*

Building the canonical `countries` table is real, standalone scope (new entity, EF
configuration, migration, seed data for a supported-country list, and a backfill/FK
step on `legal_entities`) that collides with this task's explicit "do not add new
tables" rule and reaches into shared infrastructure used elsewhere (e.g. the docs also
FK `employees.nationality_id -> countries`).

**User decision (this task):** Do not create `countries` in this task. Continue with
Parts B/C/D only. `legal_entities.country_code` (`varchar(3)`, ISO 3166-1 alpha-3 —
confirmed alpha-3 in existing fixtures/tests, e.g. `"LKA"`) remains the sole persistence
for country. `country_id -> countries` is documented here as an **open backend schema
gap requiring a separate "Country Reference Table Foundation" task**, not resolved by
this change.

## 2. Files read (audit)

Docs (OneVo-HR, read-only, not modified):
- `database/phase1-table-inventory.md`
- `database/schemas/org-structure.md`
- `modules/org-structure/overview.md` (confirms the same 25-column `legal_entities`
  shape and the same `country_id uuid FK -> countries`; its endpoint table also says
  `org:read` for `GET .../general-settings`, reinforcing mismatch #5 below)
- `modules/org-structure/legal-entities/overview.md`
- `modules/org-structure/company-profile/overview.md`
- `modules/org-structure/company-profile/end-to-end-logic.md`
- `Userflow/Configuration/tenant-settings.md`
- `Userflow/Org-Structure/legal-entity-setup.md`

Backend (HRMS-Backend-v1):
- `src/ONEVO.Domain/Features/OrgStructure/LegalEntity/Entities/LegalEntity.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/LegalEntity/LegalEntityConfiguration.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/LegalEntity/EfLegalEntityRepository.cs`
- `src/ONEVO.Infrastructure/Migrations/20260731073116_ExpandLegalEntityForGeneralSettings.cs` (+ Designer)
- `src/ONEVO.Infrastructure/Migrations/20260519061316_AddLookupTables.cs`
- `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` (legal_entities block)
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/LegalEntities/CreateLegalEntityRequest.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/LegalEntities/UpdateLegalEntityGeneralSettingsRequest.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/CreateLegalEntity/*`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/UpdateLegalEntityGeneralSettings/*`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/DeleteLegalEntity/*`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/DTOs/Responses/LegalEntityGeneralSettingsResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Mappers/LegalEntityMapper.cs`
- `src/ONEVO.Application/Features/DevPlatform/Tenancy/Commands/CreateTenant/CreateTenantCommandHandler.cs` (CountryCode usage at provisioning)
- `src/ONEVO.Application/Features/InfrastructureModule/CountryDefaults/Queries/GetCountryDefaults/*`
- `src/ONEVO.Api/Controllers/Admin/DevPlatform/SystemReference/AdminReferenceController.cs`
- `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs`
- `src/ONEVO.Application/Common/ServiceInterfaces/IDateTimeProvider.cs`
- `tests/ONEVO.Tests.Architecture/LegalEntityGeneralSettingsArchitectureTests.cs`
- `tests/ONEVO.Tests.Architecture/LegalEntityPart2BArchitectureTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/*` (all 9 existing files)
- `tests/ONEVO.Tests.Integration/OrgStructure/LegalEntity/LegalEntitiesIntegrationTests.cs`

## 3. Files changed

| File | Change |
|:--|:--|
| `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/LegalEntity/LegalEntityConfiguration.cs` | `FirstDayOfWeek` now explicitly mapped to column `week_start_day` (was implicit snake_case `first_day_of_week`); check constraint renamed `ck_legal_entities_first_day_of_week` → `ck_legal_entities_week_start_day` |
| `src/ONEVO.Infrastructure/Migrations/20260803120557_RenameLegalEntityFirstDayOfWeekToWeekStartDay.cs` (+ `.Designer.cs`) | New additive-safe migration: drop old check constraint → `RENAME COLUMN` (data-preserving) → add renamed check constraint |
| `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` | Regenerated by `dotnet ef migrations add` to reflect the column rename |
| `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/UpdateLegalEntityGeneralSettings/UpdateLegalEntityGeneralSettingsCommandHandler.cs` | Injects `IDateTimeProvider`; `entity.UpdatedAt = DateTimeOffset.UtcNow` → `_dateTimeProvider.UtcNow` |
| `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/DeleteLegalEntity/DeleteLegalEntityCommandHandler.cs` | Same `IDateTimeProvider` fix for the soft-deactivate `UpdatedAt` write |
| `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/UpdateLegalEntityGeneralSettingsCommandHandlerTests.cs` | Constructor updated for new `IDateTimeProvider` dependency; added `Handle_ValidRequest_SetsUpdatedAt_FromDateTimeProvider_NotSystemClock` |
| `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/DeleteLegalEntityCommandHandlerTests.cs` | Constructor updated; added `UpdatedAt` assertion to the soft-deactivate success test |
| `tests/ONEVO.Tests.Architecture/LegalEntityGeneralSettingsArchitectureTests.cs` | Added `Model_LegalEntities_FirstDayOfWeek_MapsToWeekStartDayColumn` |
| `tests/ONEVO.Tests.Architecture/LegalEntityPart2BArchitectureTests.cs` | Added `LegalEntityHandlers_DoNotCallDateTimeOffsetUtcNowDirectly` (Update + Delete handlers) |

No new tables, no new entities, no controller/route changes, no permission changes, no
contract shape changes (`CountryCode` stays a request/response field on both Create and
Update).

## 4. Final backend LegalEntity persistence shape

Unchanged column set (25 columns, matches the existing `LegalEntity_HasExactlyTheInventoryColumns`
architecture test, which still passes with no edits — no properties were added or removed):

`id, tenant_id, parent_legal_entity_id, name, company_code, logo_file_id,
registration_number, tax_registration_number, vat_gst_number, email, phone_number,
website, country_code, currency_code, address_json, timezone,
financial_year_start_month, week_start_day (was first_day_of_week), standard_working_days,
default_language, date_format, time_format, is_active, is_primary, created_at, updated_at`

`is_primary` is a backend-only field beyond the docs' 25-column list (load-bearing for
`GetPrimaryByTenantIdAsync` / tenant provisioning) — left as-is, documented as a
deviation, not a bug.

## 5. Docs/backend mismatches found (before fix)

| # | Mismatch | Resolution |
|:-:|:--|:--|
| 1 | `country_id uuid FK -> countries` required by docs; no `countries` table exists anywhere in backend | **Deferred** — see §1 |
| 2 | `week_start_day` (docs) stored as `first_day_of_week` (backend, via unmapped snake_case convention) | **Fixed** — explicit `HasColumnName("week_start_day")` + migration |
| 3 | `updated_at` set via `DateTimeOffset.UtcNow` directly in `UpdateLegalEntityGeneralSettingsCommandHandler` and `DeleteLegalEntityCommandHandler`, bypassing `IDateTimeProvider` | **Fixed** |
| 4 | `registration_number` is nullable per docs; backend validators (`CreateLegalEntityCommandValidator`, `UpdateLegalEntityGeneralSettingsCommandValidator`) require it (`NotEmpty`) and enforce tenant-uniqueness | **Not changed** — not named in this task's Part B field list as a required fix, and changing required/optional validation is a product-behavior decision, not a mapping reconciliation. Documented as a follow-up. |
| 5 | `GET /{id}/general-settings` is `org:manage` in the controller; both `legal-entities/overview.md` and `modules/org-structure/overview.md` endpoint tables say `org:read`, while `Userflow/Configuration/tenant-settings.md` says `org:manage` is the *only* permission for this flow and explicitly forbids a read-only variant | **Not changed** — task says "preserve permissions" and the docs conflict with each other (2 vs. 1). Documented as a doc/backend conflict, not resolved here. |
| 6 | `DELETE /{id}/logo` route exists beyond the five routes named in this task's Part C list | **Not changed** — pre-existing logo route, out of scope by the logo-exclusion rule |
| 7 | All other non-logo fields (name, company_code, tax_registration_number, vat_gst_number, email, phone_number, website, address_json, timezone, financial_year_start_month, standard_working_days, default_language, date_format, time_format, is_active, currency_code) | **Verified matching** — see §6 table |

## 6. Non-logo field mapping table (verified against docs)

| Docs field | Backend property | Column | Type | Status |
|:--|:--|:--|:--|:--|
| `name` | `Name` | `name` | `varchar(200)` NOT NULL | ✅ match |
| `company_code` | `CompanyCode` | `company_code` | `varchar(20)` nullable, tenant-unique | ✅ match |
| `registration_number` | `RegistrationNumber` | `registration_number` | `varchar(50)` — backend requires it (docs say nullable) | ⚠️ see mismatch #4 |
| `tax_registration_number` | `TaxRegistrationNumber` | `tax_registration_number` | `varchar(80)` nullable | ✅ match |
| `vat_gst_number` | `VatGstNumber` | `vat_gst_number` | `varchar(50)` nullable | ✅ match |
| `email` | `Email` | `email` | `varchar(254)` nullable | ✅ match |
| `phone_number` | `PhoneNumber` | `phone_number` | `varchar(20)` nullable | ✅ match |
| `website` | `Website` | `website` | `varchar(255)` nullable | ✅ match |
| `country_id` | *(none — `CountryCode` string instead)* | `country_code` | `varchar(3)` | ⚠️ deferred, see §1 |
| `currency_code` | `CurrencyCode` | `currency_code` | `varchar(3)` NOT NULL | ✅ match |
| `address_json` | `AddressJson` | `address_json` | `jsonb` nullable | ✅ match |
| `timezone` | `Timezone` | `timezone` | `varchar(50)` nullable | ✅ match |
| `financial_year_start_month` | `FinancialYearStartMonth` | `financial_year_start_month` | `int`, CHECK 1–12 | ✅ match |
| `week_start_day` | `FirstDayOfWeek` | `week_start_day` (was `first_day_of_week`) | `int`, CHECK 1–7 | ✅ **fixed this task** |
| `standard_working_days` | `StandardWorkingDays` | `standard_working_days` | `jsonb`, validated ⊆[1,7] non-empty | ✅ match |
| `default_language` | `DefaultLanguage` | `default_language` | `varchar(10)` NOT NULL | ✅ match |
| `date_format` | `DateFormat` | `date_format` | `varchar(20)` NOT NULL | ✅ match |
| `time_format` | `TimeFormat` | `time_format` | `varchar(10)`, CHECK `12h`/`24h` | ✅ match |
| `is_active` | `IsActive` | `is_active` | `boolean` | ✅ match |
| `updated_at` | `UpdatedAt` | `updated_at` | `timestamptz` nullable, now via `IDateTimeProvider` | ✅ **fixed this task** |

## 7. API request/response behavior

Unchanged. Routes, permissions, and contract shapes are exactly as they were before this
task:

- `GET /api/v1/org/legal-entities` — `org:read`
- `GET /api/v1/org/legal-entities/{id}/general-settings` — `org:manage` (see mismatch #5)
- `POST /api/v1/org/legal-entities` — `org:manage`
- `PUT /api/v1/org/legal-entities/{id}/general-settings` — `org:manage`
- `DELETE /api/v1/org/legal-entities/{id}` — `org:manage`
- `DELETE /api/v1/org/legal-entities/{id}/logo` — `org:manage` (pre-existing, logo, untouched)

`CountryCode` (string) continues to be accepted/exposed on Create and Update contracts;
no `countryId` field was added to any request/response DTO. No `tenantId` field exists on
any request contract (confirmed by existing `LegalEntityPart2BArchitectureTests` and by
this task's `rg -n "tenantId"` search, which only matched explanatory code comments).

## 8. Tests added/updated

Unit (`tests/ONEVO.Tests.Unit`):
- `UpdateLegalEntityGeneralSettingsCommandHandlerTests.Handle_ValidRequest_SetsUpdatedAt_FromDateTimeProvider_NotSystemClock` (new)
- `DeleteLegalEntityCommandHandlerTests.Handle_ValidConfirmName_SoftDeactivates_AndDoesNotRemoveRow` (extended with `UpdatedAt` assertion)
- Both files' `BuildSut()` helpers updated for the new `IDateTimeProvider` constructor parameter

Architecture (`tests/ONEVO.Tests.Architecture`):
- `LegalEntityGeneralSettingsArchitectureTests.Model_LegalEntities_FirstDayOfWeek_MapsToWeekStartDayColumn` (new)
- `LegalEntityPart2BArchitectureTests.LegalEntityHandlers_DoNotCallDateTimeOffsetUtcNowDirectly` (new, `[Theory]` over Update + Delete handlers)

Not added (explicitly out of scope per §1 decision): countryCode → country_id mapping
tests, unsupported-countryCode rejection tests, `country_id` FK model tests. These
belong to the future Country Reference Table Foundation task.

Integration: no new integration tests were added — the existing
`LegalEntitiesIntegrationTests.cs` suite already exercises create/update/delete/list
against a real PostgreSQL instance (via Testcontainers) and now also implicitly
validates that the new migration applies cleanly, since `AdminTestFactory.MigrateDatabaseAsync`
runs the full migration set including the `week_start_day` rename.

## 9. Verification command outputs and counts

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
  → Build succeeded, 0 Warning(s), 0 Error(s)

dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
  → Passed! Failed: 0, Passed: 1175, Skipped: 0, Total: 1175
    (re-run with the exact --no-restore --no-build flags after all edits; matches the
    earlier full-build run)

dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
  → Passed! Failed: 0, Passed: 403, Skipped: 0, Total: 403
    (same re-run confirmation)

Mutation check on the one new schema-guarding test (not part of the task's literal
command list, done to verify the test can actually fail): temporarily changed
LegalEntityConfiguration's HasColumnName("week_start_day") to a wrong probe value,
re-ran Model_LegalEntities_FirstDayOfWeek_MapsToWeekStartDayColumn in isolation →
FAILED as expected ("Expected: week_start_day, Actual: zzz_probe"). Reverted the probe,
re-ran → PASSED. Confirms the test is not vacuous.

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --verbosity minimal
  → Failed: 2, Passed: 130, Skipped: 0, Total: 132, Duration: 13 m 13 s

    LegalEntitiesIntegrationTests itself is not named anywhere in the failure output —
    every test in that class passed, confirming the week_start_day migration applies
    cleanly to a real PostgreSQL instance and create/update/delete/list still work
    end-to-end through the real HTTP pipeline.

    The 2 failures were investigated, not just assumed pre-existing. Both were
    re-run in isolation (--filter on the two failing test names):
      1. BaseForgotPasswordRestrictedRoleHttpIntegrationTests...RestrictedRoleHttp_CreatesTokenAndOutboxRowWithoutRlsViolation
         — first run: Npgsql "Failed to connect ... target machine actively refused
         it" during WebApplicationFactory host startup. Re-run in isolation: PASSED.
         Confirmed Testcontainers connection-timing flake (13-minute suite spinning
         up many Postgres containers concurrently), not a real failure.
      2. TenantProvisioningE2ETests.Full_tenant_provisioning_flow
         — 403 Forbidden, "Permission 'roles:read' required," at the same
         GET /api/v1/roles assertion on both the full run and the isolated re-run
         (deterministic, reproduces every time). Traced the permission: `roles:read`
         is seeded under module `"roles"` (PermissionSeeder.cs:167). No file this
         task touched has any relation to roles, permissions, or subscription-plan
         module entitlements — this task's diff is confined to
         LegalEntity/week_start_day/IDateTimeProvider. This is a real, deterministic,
         pre-existing failure in the current tree's state, most likely connected to
         the concurrent, uncommitted Department Foundation work already sitting in
         this working directory (e.g. `AddOrgModuleToStarterPlan` migration touches
         the same `subscription_plans.included_modules_json` module-entitlement
         mechanism `roles:read` depends on) — but this was not fixed or further
         root-caused, since it is out of this task's Legal Entity scope. Flagged as
         a follow-up in §11, not silently written off.

dotnet ef migrations script --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api
  → Run with an added --idempotent flag (not in the task's literal command list) so the
    script could be generated without a live database matching the existing migration
    history; behavior of the new migration itself is identical either way. Confirmed
    sequence for legal_entities: DROP CONSTRAINT ck_legal_entities_first_day_of_week;
    ALTER TABLE legal_entities RENAME COLUMN first_day_of_week TO week_start_day;
    ALTER TABLE legal_entities ADD CONSTRAINT ck_legal_entities_week_start_day CHECK (week_start_day BETWEEN 1 AND 7);
    (script generated to a scratch file for inspection only, then deleted, not committed)

rg -n "company_settings|general_settings" src tests
  → No matches (no separate settings table/entity/repository/controller exists)

rg -n "tenantId" src\ONEVO.Api\Contracts\OrgStructure\LegalEntities src\ONEVO.Api\Controllers\Tenant\OrgStructure\LegalEntitiesController.cs
  → Matches only in explanatory code comments ("Deliberately excludes tenantId...");
    no property or parameter named tenantId

rg -n "DateTimeOffset\.UtcNow" src\ONEVO.Application\Features\OrgStructure\LegalEntity
  → 2 remaining matches, both in logo handlers explicitly out of scope for this task:
    Commands/SetLegalEntityLogo/SetLegalEntityLogoCommandHandler.cs:43
    Commands/RemoveLegalEntityLogo/RemoveLegalEntityLogoCommandHandler.cs:35
    (Update and Delete handlers — the only ones this task was allowed to touch — are clean)

rg -n "entity_assets|FileRecord|file_records|Cloudflare|R2|SetLegalEntityLogo|RemoveLegalEntityLogo" ...
  → Matches confined to the logo command files themselves (SetLegalEntityLogo*,
    RemoveLegalEntityLogo*), confirming no logo/file/asset code was touched by this task

git diff --check
  → No whitespace/conflict-marker errors (only pre-existing CRLF/LF line-ending notices,
    consistent with the rest of the repo)
```

## 10. Explicit statements

- **Logo/file/asset handling was intentionally ignored and left unchanged.** No edits
  were made to `LogoFileId`/`logo_file_id`, `SetLegalEntityLogoCommand*`,
  `RemoveLegalEntityLogoCommand*`, `LegalEntityGeneralSettingsResponse`'s `LogoFileId`
  field (read-only, unchanged), `SetLegalEntityLogoRequest`, `file_records`,
  `entity_assets`, `FileStorage`, or any Cloudflare/R2 storage logic. The `DELETE
  /{id}/logo` route was left exactly as it was.
- **OneVo-HR docs were read but not modified.** All reads under `C:\onevoNew\OneVo-HR`
  were read-only; no file in that directory was written to.

## 11. Remaining risks / follow-ups

1. **`country_id -> countries` is unresolved.** This is the largest open item: a future
   task must design and build the canonical `countries` reference table (id, name, code,
   phone_code, currency_code per `phase1-table-inventory.md`), seed it, add
   `legal_entities.country_id` as a nullable-then-required FK with a backfill from
   `country_code` (already alpha-3, so the backfill should be a direct code match), and
   decide whether `country_code` is dropped or kept as a transitional/API-only field.
   This also affects `employees.nationality_id -> countries` per the docs and should be
   scoped as shared infrastructure, not a LegalEntity-only change.
2. **`registration_number` nullability** — docs say nullable, backend requires it and
   enforces tenant-uniqueness. Left unchanged in this task; needs a product decision
   before either side is changed.
3. **Permission conflict on `GET /{id}/general-settings`** — three-way disagreement
   between the controller (`org:manage`), `legal-entities/overview.md` (`org:read`), and
   `tenant-settings.md` (`org:manage`-only, no read-only variant). Left unchanged
   (task said preserve permissions); needs doc reconciliation on the OneVo-HR side.
4. **Docs mandate an `outbox_messages` / `LegalEntityCountrySet` event on country change**
   (for Calendar's holiday-calendar-setting integration) that Create/Update do not
   currently write. Not requested by this task, not added, but adjacent to the field
   this task touched and worth flagging for the eventual country_id follow-up.
5. Logo handlers (`SetLegalEntityLogoCommandHandler`, `RemoveLegalEntityLogoCommandHandler`)
   still call `DateTimeOffset.UtcNow` directly rather than `IDateTimeProvider` — left
   untouched per the logo-exclusion rule, but should be fixed alongside any future logo
   task for consistency with the rest of the LegalEntity feature.
6. **Pre-existing, unrelated integration failure confirmed deterministic:**
   `TenantProvisioningE2ETests.Full_tenant_provisioning_flow` fails on every run (full
   suite and isolated re-run) with `403 Forbidden, "Permission 'roles:read' required."`
   at `GET /api/v1/roles`. `roles:read` is seeded under the `"roles"` module
   (`PermissionSeeder.cs:167`); no file this task touched has any relation to roles,
   permissions, or subscription-plan module entitlements. Plausibly connected to the
   concurrent, uncommitted Department Foundation work already present in this working
   directory before this task started (e.g. the untracked `AddOrgModuleToStarterPlan`
   migration touches the same `subscription_plans.included_modules_json`
   module-entitlement mechanism `roles:read` depends on), but not root-caused or fixed
   here — out of this task's Legal Entity scope. The other integration failure seen on
   the full run (`BaseForgotPasswordRestrictedRoleHttpIntegrationTests...`) passed on
   isolated re-run and is a Testcontainers connection-timing flake, not a real failure.
