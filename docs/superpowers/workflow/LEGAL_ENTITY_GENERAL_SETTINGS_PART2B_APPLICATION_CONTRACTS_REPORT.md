# Legal Entity / Company General Settings — Part 2B Application Contracts Report

**Scope:** Application-layer commands/queries/handlers/validators and API contract DTOs only. No controllers, routes, Postman changes, OneVo-HR doc changes, migrations, EF schema changes, logo file upload/validation, or physical delete.
**Repo:** `C:\onevoNew\HRMS-Backend-v1`
**Builds on:** Part 2A's expanded `LegalEntity` entity, `ILegalEntityRepository`/`EfLegalEntityRepository`, and the `ExpandLegalEntityForGeneralSettings` migration (all unchanged in this phase).

---

## 1. Files Created

**API contracts (requests):**
- `src/ONEVO.Api/Contracts/OrgStructure/LegalEntities/CreateLegalEntityRequest.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/LegalEntities/UpdateLegalEntityGeneralSettingsRequest.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/LegalEntities/DeleteLegalEntityRequest.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/LegalEntities/SetLegalEntityLogoRequest.cs`

**Application DTOs / responses / mapper:**
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/DTOs/LegalEntityAddressDto.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/DTOs/Responses/LegalEntityListItemResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/DTOs/Responses/LegalEntityGeneralSettingsResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/DTOs/Responses/LegalEntityLogoResponse.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Mappers/LegalEntityMapper.cs`

**Queries:**
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/ListLegalEntities/ListLegalEntitiesQuery.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/ListLegalEntities/ListLegalEntitiesQueryHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/GetLegalEntityGeneralSettings/GetLegalEntityGeneralSettingsQuery.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/GetLegalEntityGeneralSettings/GetLegalEntityGeneralSettingsQueryHandler.cs`

**Commands:**
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/CreateLegalEntity/{CreateLegalEntityCommand,CreateLegalEntityCommandHandler,CreateLegalEntityCommandValidator}.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/UpdateLegalEntityGeneralSettings/{UpdateLegalEntityGeneralSettingsCommand,UpdateLegalEntityGeneralSettingsCommandHandler,UpdateLegalEntityGeneralSettingsCommandValidator}.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/DeleteLegalEntity/{DeleteLegalEntityCommand,DeleteLegalEntityCommandHandler,DeleteLegalEntityCommandValidator}.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/SetLegalEntityLogo/{SetLegalEntityLogoCommand,SetLegalEntityLogoCommandHandler,SetLegalEntityLogoCommandValidator}.cs`
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/RemoveLegalEntityLogo/{RemoveLegalEntityLogoCommand,RemoveLegalEntityLogoCommandHandler}.cs`

**Tests:**
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/CreateLegalEntityCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/CreateLegalEntityCommandValidatorTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/UpdateLegalEntityGeneralSettingsCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/UpdateLegalEntityGeneralSettingsCommandValidatorTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/DeleteLegalEntityCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/DeleteLegalEntityCommandValidatorTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/LegalEntityLogoCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/ListLegalEntitiesQueryHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/GetLegalEntityGeneralSettingsQueryHandlerTests.cs`
- `tests/ONEVO.Tests.Architecture/LegalEntityPart2BArchitectureTests.cs`
- `LEGAL_ENTITY_GENERAL_SETTINGS_PART2B_APPLICATION_CONTRACTS_REPORT.md` (this file)

**Not modified:** anything under Part 2A's scope (entity, EF config, repository, migration), any controller, any Postman collection, any OneVo-HR doc, `DependencyInjection.cs` in either layer (MediatR/FluentValidation assembly scanning already auto-registers everything new).

---

## 2. Contracts Added

| Contract | Location | Fields |
|---|---|---|
| `LegalEntityListItemResponse` | Application DTOs (see §8 for why, not Api/Contracts) | id, name, companyCode, logoFileId, isActive, isPrimary |
| `LegalEntityGeneralSettingsResponse` | Application DTOs | id, name, companyCode, logoFileId, registrationNumber, taxRegistrationNumber, vatGstNumber, email, phoneNumber, website, countryCode, currencyCode, timezone, financialYearStartMonth, firstDayOfWeek, standardWorkingDays, defaultLanguage, dateFormat, timeFormat, status, registeredBusinessAddress |
| `CreateLegalEntityRequest` | `Api/Contracts/OrgStructure/LegalEntities` | name, companyCode, registrationNumber, countryCode, currencyCode, taxRegistrationNumber, registeredBusinessAddress, parentLegalEntityId — no tenantId/logoFileId/isPrimary/createdAt/updatedAt |
| `CreateLegalEntityResponse` | reuses `LegalEntityGeneralSettingsResponse` verbatim, per the task's explicit permission | — |
| `UpdateLegalEntityGeneralSettingsRequest` | `Api/Contracts/OrgStructure/LegalEntities` | full field set per spec, no tenantId, no parentLegalEntityId (not part of this screen's edit fields) |
| `DeleteLegalEntityRequest` | `Api/Contracts/OrgStructure/LegalEntities` | confirmName |
| `SetLegalEntityLogoRequest` | `Api/Contracts/OrgStructure/LegalEntities` | fileId |
| `LegalEntityAddressDto` | Application DTOs (shared by request/response) | line1, line2, city, state, postalCode, country — no address shape exists anywhere else in the codebase (confirmed by search), so this defines it once |
| `LegalEntityLogoResponse` | Application DTOs | legalEntityId, logoFileId — return shape for the two logo commands |

---

## 3. Commands / Queries / Handlers Added

| Type | Purpose | Notes |
|---|---|---|
| `ListLegalEntitiesQuery` | Company selector/list | `IncludeInactive` optional flag (default false); tenant from `ICurrentUser` |
| `GetLegalEntityGeneralSettingsQuery` | General Settings screen | tenant-scoped fetch, 404 if entity belongs to another tenant |
| `CreateLegalEntityCommand` | Create Company | applies all safe defaults (see §5), duplicate + parent checks |
| `UpdateLegalEntityGeneralSettingsCommand` | General Settings PUT | fetch-then-mutate, duplicate checks excluding self, working-days normalization, last-active-company guard on deactivate |
| `DeleteLegalEntityCommand` | Delete/deactivate | soft `IsActive = false`, confirmName exact match, last-active-company guard |
| `SetLegalEntityLogoCommand` | Set/replace logo | updates `LogoFileId` only — see §7 for the deferred validation decision |
| `RemoveLegalEntityLogoCommand` | Remove logo | clears `LogoFileId` only, never touches `file_records` |

All handlers:
- Check `ICurrentUser.IsAuthenticated` and `TenantId != Guid.Empty` first, returning `Forbidden` otherwise (matches `CreateRoleCommandHandler`/`UpdateRoleCommandHandler` exactly).
- Source `tenantId` exclusively from `ICurrentUser.TenantId` — never from the request.
- Call `ILegalEntityRepository.SaveChangesAsync(ct)` (Part 2A's repository-owned save), not `IUnitOfWork` — matching the Part 2A report's own note that this repository intentionally has its own `SaveChangesAsync` for this feature.
- The two mutating-existing-row commands (Update, Delete, SetLogo, RemoveLogo) all fetch via `GetByIdForTenantAsync` first and mutate that instance — never construct a detached `LegalEntity` and call `Update()`, per Part 2A's explicit warning (§9 of the Part 2A report).

`LegalEntityMapper` centralizes: `StandardWorkingDays` JSON array ⇄ `IReadOnlyList<int>`, `AddressJson` ⇄ `LegalEntityAddressDto?`, and `IsActive` → `"active"`/`"inactive"` status string.

---

## 4. Validators Added

- `CreateLegalEntityCommandValidator` — name/companyCode/registrationNumber/countryCode/currencyCode required with EF-config-matching max lengths (200/20/50/3/3); taxRegistrationNumber optional (max 80); parentLegalEntityId not `Guid.Empty` when supplied; address sub-fields capped at reasonable lengths.
- `UpdateLegalEntityGeneralSettingsCommandValidator` — every rule from the task's Update validation table: required name/companyCode/registrationNumber/countryCode/currencyCode/timezone/defaultLanguage/dateFormat; financialYearStartMonth 1–12; firstDayOfWeek 1–7; standardWorkingDays non-empty, all values 1–7, no duplicates; timeFormat restricted to `12h`/`24h`; status restricted to `active`/`inactive`; email validated with `EmailAddress()` when supplied; website validated via `Uri.TryCreate(..., UriKind.Absolute, ...)` when supplied; phoneNumber/taxRegistrationNumber/vatGstNumber length-capped when supplied.
- `DeleteLegalEntityCommandValidator` — confirmName required (the exact-match check itself is correctly a handler concern, not a validator concern, since it needs the fetched entity).
- `SetLegalEntityLogoCommandValidator` — fileId not `Guid.Empty`.

All validators are picked up automatically by the existing `AddValidatorsFromAssembly` + `ValidationBehavior<,>` MediatR pipeline (verified present in `ONEVO.Application/DependencyInjection.cs:23,28`) — no manual registration needed.

---

## 5. Business Rules Implemented

- **Create safe defaults** (handler-level, not relying on the entity's own C# defaults, so they're independently testable): `IsPrimary = false` (explicitly forced — the entity's own default is `true`, which would be wrong for every non-provisioning-time company; only `CreateTenantCommandHandler` may set a company primary), `IsActive = true`, `Timezone = "UTC"` (no country-default helper exists — Part 1 audit confirmed no `countries` table), `FinancialYearStartMonth = 1`, `FirstDayOfWeek = 1`, `StandardWorkingDays = [1,2,3,4,5]`, `DefaultLanguage = "en-US"`, `DateFormat = "DD MMM YYYY"`, `TimeFormat = "12h"`.
- **Tenant-scoped uniqueness** — name/companyCode/registrationNumber checked via Part 2A's `NameExistsForTenantAsync`/`CompanyCodeExistsForTenantAsync`/`RegistrationNumberExistsForTenantAsync`, all returning `Conflict` (409), mirroring `CreateRoleCommandHandler`'s duplicate-name pattern. Update passes `excludeId` so the row being edited never conflicts with itself.
- **Parent company validation** — `ParentExistsForTenantAsync(tenantId, parentId)` on Create only (Update's field set, per this task's spec, does not expose `parentLegalEntityId` at all). A missing parent and a parent belonging to another tenant produce the identical `"Parent company not found."` 404 — verified by a dedicated test — so cross-tenant existence is never leaked.
- **Parent self-reference / cycle detection: not applicable, not implemented.** Self-reference is structurally impossible at Create (the new row's `Id` is generated server-side after validation, so a caller cannot reference it), and Update does not accept `parentLegalEntityId` at all — there is no code path in Part 2B that can create or move a parent edge post-creation. No repository method for cycle-walking was added (that would be new Part 2A-owned repository surface, out of this phase). This is a deliberate scope note, not an oversight.
- **Last-active-company guard** — enforced in **two** places: `DeleteLegalEntityCommand` (unconditionally on delete) and `UpdateLegalEntityGeneralSettingsCommand` (only on an active→inactive `status` transition). This second guard was not explicitly spelled out in the task text for Update, but `status` is the same `IsActive` flag the Delete flow protects — allowing `PUT .../general-settings { status: "inactive" }` to silently deactivate the tenant's last company would be a hole around the Delete-only guard the task describes. Flagged here as an applied, not literally-specified, business rule.
- **Delete/deactivate semantics** — soft delete only (`IsActive = false`), `confirmName` compared via `StringComparison.Ordinal` after a single `.Trim()` on the input (case-sensitive exact match, matching the Part 1 audit's stated convention), row never physically removed, no cascading.
- **Fetch-then-mutate everywhere** — Update/Delete/SetLogo/RemoveLogo all call `GetByIdForTenantAsync` first; fields not present in a given request (LogoFileId, ParentLegalEntityId, IsPrimary, CreatedAt, Id, TenantId for Update) are left untouched on the fetched instance and therefore preserved by construction — verified by a dedicated preservation test.
- **StandardWorkingDays normalization** — the validator rejects out-of-range values and duplicates; the handler then sorts ascending and serializes via `LegalEntityMapper.SerializeStandardWorkingDays` before saving.

---

## 6. Status Codes — Deviation From the Part 1 Draft, Documented

Part 1's draft validation table suggested 409 for `confirmName` mismatch and the last-active-company rule. This task's own instructions for Part 2B say explicitly: *"Return conflict ... for duplicates"* but *"Return validation/business failure for deleting last active company"* — a deliberate wording distinction between "conflict" (duplicates) and "validation/business failure" (everything else). Part 2B follows the task's own wording literally:

- Duplicate name/companyCode/registrationNumber → `Result.Conflict` (409).
- `confirmName` mismatch → `Result.Failure` (400, default).
- Last-active-company (both Delete and the Update deactivation guard) → `Result.Failure` (400, default).
- Missing/cross-tenant entity, missing/cross-tenant parent → `Result.NotFound` (404).
- Not authenticated / no tenant context → `Result.Forbidden` (403).

If product wants 409 for the two business-failure cases instead, that's a one-line change in each handler — flagged here rather than guessed at silently.

---

## 7. Deferred to Part 2C

1. **`SetLegalEntityLogoCommand` does not validate that the `fileId` belongs to the tenant.** While implementing it, `tests/ONEVO.Tests.Architecture/FileStorageArchitectureTests.NoApplicationType_BypassesFileStorageService_ByUsingFileRepositoriesDirectly` (a pre-existing, passing architecture test guarding the Storage feature) failed the moment the handler took an `IFileRecordRepository` dependency: **no feature outside `Features.Storage.File` may reference `IFileRecordRepository`/`IFileUploadReservationRepository` directly — only `IFileStorageService` is allowed**, and that service's only methods are upload/reservation flows (`BeginReservationAsync`, `CompleteUploadAsync`, `CancelReservationAsync`, `UploadAsync`); none of them validate an already-uploaded, arbitrary `fileId`'s tenant ownership. Extending `IFileStorageService` with such a lookup is a Storage-feature change, outside this task's OrgStructure/LegalEntity-only scope. Per the task's own explicit fallback (*"May be contract/command-only if file ownership validation is deferred... update logoFileId only"*), the handler fetches the legal entity (tenant-scoped) and sets `LogoFileId = request.FileId` directly, with no cross-tenant file check. **Part 2C (or a Storage-feature follow-up) must add a tenant-scoped file lookup to `IFileStorageService` and call it here before this is safe to expose over HTTP**, since the FK is `ON DELETE SET NULL` and does not enforce tenant matching at the database level either (Part 2A report §7 point 4, analogous issue for the parent FK).
2. **Controller / route wiring** — no controller was created; the 7 endpoints from the Part 1 API contract plan (`GET /legal-entities`, `GET /legal-entities/{id}/general-settings`, `POST /legal-entities`, `PUT /legal-entities/{id}/general-settings`, `DELETE /legal-entities/{id}`, `PUT/DELETE /legal-entities/{id}/logo`) remain to be built in Part 2C, including `[RequirePermission("org:read")]` on List and `[RequirePermission("org:manage")]` on everything else per the Part 1 audit.
3. **Parent cycle detection** — genuinely not applicable given the current field exposure (see §5); if a future phase adds `parentLegalEntityId` to the Update contract, cycle-walking (`HasChildrenAsync`-based or otherwise) becomes necessary and was intentionally not built speculatively.
4. **Integration/HTTP tests** — explicitly Part 2C's responsibility per the task's own phasing.
5. **`company_code` required-at-DB-level and `registration_number` required-at-DB-level** remain open product questions from Part 2A §7, unaffected by this phase (Part 2B enforces "required" only at the application/validator layer for Create, matching Part 2A's recommendation).

---

## 8. Architectural Deviations From the Task's "Likely Paths", With Reasons

1. **Response DTOs live in `Application/Features/OrgStructure/LegalEntity/DTOs/Responses`, not `Api/Contracts/OrgStructure/LegalEntities`.** The task listed both under the same `Api/Contracts` folder as a "likely path." This is not possible as written: `ONEVO.Api` has a project reference to `ONEVO.Application` (never the reverse), and every handler in this change returns `Result<TResponse>` — so the response type must live somewhere `ONEVO.Application` can compile against. Request contracts (pure input shapes, never returned by a handler) do live under `Api/Contracts/OrgStructure/LegalEntities` exactly as instructed, matching the existing convention used by `Auth`/`Admin/Legal` (e.g. `LoginRequest`, `AcceptPendingLegalDocumentsRequest`).
2. **Validators are co-located with their command** (e.g. `Commands/CreateLegalEntity/CreateLegalEntityCommandValidator.cs`), not in a separate `Validators/` folder. This mirrors the proven, existing `CreateRoleCommandValidator.cs`/`UpdateRoleCommandHandler.cs` convention rather than inventing a new one for this feature only.
3. **C# namespaces for Commands/Queries/Mappers/DTOs stop at `OrgStructure`** (e.g. `ONEVO.Application.Features.OrgStructure.Commands.CreateLegalEntity`, not `...OrgStructure.LegalEntity.Commands...`) — this exactly continues the convention Part 2A established for `ILegalEntityRepository`/`LegalEntityConfiguration`/`EfLegalEntityRepository`: a `.LegalEntity` namespace segment collides with the `LegalEntity` entity type and forces using-aliases everywhere. Folder paths still say `LegalEntity` throughout (e.g. `Features/OrgStructure/LegalEntity/Commands/...`); only the C# `namespace` declaration omits the segment.

---

## 9. Tests Added and Results

**Unit tests — handlers (43 new facts across 7 files):**
- `CreateLegalEntityCommandHandlerTests` — success with safe defaults; duplicate name/companyCode/registrationNumber → 409; parent not found / cross-tenant parent → identical 404 message; valid parent persists; not-authenticated → 403.
- `UpdateLegalEntityGeneralSettingsCommandHandlerTests` — success fetches-then-mutates; unexposed fields (LogoFileId, ParentLegalEntityId, CreatedAt, IsPrimary, TenantId) preserved; duplicate name/companyCode/registrationNumber excluding self → 409; deactivating the last active company → failure, no persist; deactivating with other active companies present → success; entity-not-found → 404.
- `DeleteLegalEntityCommandHandlerTests` — valid confirmName soft-deactivates and never calls `Remove`/`RemoveRange`; confirmName mismatch → failure, no persist; last active company → failure, no persist; missing/out-of-tenant entity → 404.
- `LegalEntityLogoCommandHandlerTests` — SetLogo updates only `LogoFileId` (verified `Name` untouched) and persists; SetLogo on missing entity → 404; RemoveLogo clears `LogoFileId` only and persists; RemoveLogo on missing entity → 404.
- `ListLegalEntitiesQueryHandlerTests` — default view excludes inactive; `includeInactive=true` returns all; not-authenticated → 403.
- `GetLegalEntityGeneralSettingsQueryHandlerTests` — in-tenant entity maps correctly; cross-tenant/missing entity → 404.

**Unit tests — validators (27 new facts across 3 files):**
- `CreateLegalEntityCommandValidatorTests` — valid command passes; each required field (name, companyCode, registrationNumber, countryCode, currencyCode) individually empty fails; `Guid.Empty` parent fails.
- `UpdateLegalEntityGeneralSettingsCommandValidatorTests` — valid command passes; empty/out-of-range/duplicate `standardWorkingDays` each fail; out-of-range `financialYearStartMonth`/`firstDayOfWeek` fail; invalid `timeFormat`/`status` fail; invalid vs. valid email; invalid vs. valid website; empty timezone fails.
- `DeleteLegalEntityCommandValidatorTests` — valid confirmName passes; empty confirmName fails.

**Architecture tests — `LegalEntityPart2BArchitectureTests` (32 new facts):**
- No request contract or command/query type exposes a `TenantId` property (reflection-based, per-type).
- No file matching `*LegalEntit*` exists anywhere under `src/ONEVO.Api/Controllers` (no controller/route added).
- `DeleteLegalEntityCommandHandler` source contains no `.Remove(`/`.RemoveRange(`/`DELETE FROM`, and does contain `IsActive = false`.
- `UpdateLegalEntityGeneralSettingsCommandHandler` source: `GetByIdForTenantAsync` appears before `_legalEntities.Update(`, which appears before `SaveChangesAsync`; and the source never contains `new LegalEntity` (guards against the detached-entity anti-pattern).
- Every new Command/Query/Validator file is confirmed to exist under `Features/OrgStructure/LegalEntity/{Commands,Queries}/...` and **not** under the equivalent `DevPlatform/Tenancy` path.
- Every new Api contract file is confirmed to exist under `Contracts/OrgStructure/LegalEntities/`.

**Results:**

| Check | Result |
|---|---|
| `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore` | Succeeded, 0 new warnings (1 pre-existing unrelated `CS8602`) |
| `dotnet test tests/ONEVO.Tests.Unit` (full suite) | `Passed! - Failed: 0, Passed: 1097, Skipped: 0, Total: 1097` |
| `dotnet test tests/ONEVO.Tests.Architecture` (full suite) | `Passed! - Failed: 0, Passed: 330, Skipped: 0, Total: 330` |
| `git diff --check` | Exit code 0; one informational CRLF/LF normalization notice (pre-existing file from Part 2A), not a real whitespace error |

No restore was needed — the working tree already had a restored `bin`/`obj` state from Part 2A's work in the same session.

**One real defect caught and fixed during this phase:** the first `SetLegalEntityLogoCommandHandler` draft injected `IFileRecordRepository` directly to validate the incoming `fileId` belongs to the tenant. Running the full architecture suite (not just the new tests) caught this immediately via the pre-existing `FileStorageArchitectureTests.NoApplicationType_BypassesFileStorageService_ByUsingFileRepositoriesDirectly` test, which forbids exactly this. The handler and its tests were rewritten to the deferred-validation shape described in §7 item 1, and the full suite was re-run clean afterward.

---

## 10. Explicit Confirmation: Out-of-Scope Areas Untouched

Verified via `git status --porcelain`:

- **No controllers added or modified.**
- **No routes added** — no `[HttpGet]`/`[HttpPost]`/`[HttpPut]`/`[HttpDelete]` attribute exists anywhere in this changeset.
- **No Postman files touched.**
- **No OneVo-HR documentation touched** (directory is outside this repo and was not written to).
- **No EF migrations added** — the only migration present (`20260731073116_ExpandLegalEntityForGeneralSettings`) is Part 2A's, unchanged.
- **No EF schema/configuration changes** — `LegalEntityConfiguration.cs` was not touched in this phase.
- **No logo file upload/validation implemented** — `SetLegalEntityLogoCommand` sets the FK column only; no stream handling, no `IFileStorageService` call, no new Storage-feature code.
- **No hard-delete code** — verified both by the architecture test and by manual review of `DeleteLegalEntityCommandHandler.cs`.
- **No RLS changes** — nothing in this phase touches migrations or policies.

---

## 11. Can Part 2C Start?

**Yes.** All application-layer commands, queries, validators, and contracts described in this task are implemented, unit-tested, and architecture-tested green. Part 2C (controller/route wiring + HTTP/integration tests) can now:

- Wire each of the 7 endpoints to its corresponding command/query via `IMediator`, following the exact `RolesController` pattern (`[Authorize(Policy = "TenantPolicy")]`, `[RequirePermission("org:read")]` on List, `[RequirePermission("org:manage")]` on everything else).
- Map `Api/Contracts/OrgStructure/LegalEntities/*Request` records into the corresponding commands in each controller action (matching `RolesController.Create`'s `new CreateRoleCommand(request.Name, ...)` pattern).
- Resolve the deferred logo file-ownership validation (§7 item 1) before exposing the logo endpoints, since that gap is only safe today because no HTTP surface reaches these handlers yet.
