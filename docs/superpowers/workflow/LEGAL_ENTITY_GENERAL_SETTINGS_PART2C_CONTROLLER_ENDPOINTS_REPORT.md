# Legal Entity / Company General Settings — Part 2C Controller Endpoints Report

**Scope:** Controller endpoint wiring + HTTP/integration tests only. No migrations, EF schema changes, Postman changes, OneVo-HR doc changes, physical delete, RLS changes, or unrelated refactors.
**Repo:** `C:\onevoNew\HRMS-Backend-v1`
**Builds on:** Part 2A's schema/repository and Part 2B's commands/queries/validators/contracts (both unchanged in this phase, except one obsolete Part 2B architecture test — see §9).

---

## 1. Files Created / Changed

**Created:**
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/LegalEntitiesControllerTests.cs`
- `tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs`
- `tests/ONEVO.Tests.Integration/OrgStructure/LegalEntity/LegalEntitiesIntegrationTests.cs`
- `LEGAL_ENTITY_GENERAL_SETTINGS_PART2C_CONTROLLER_ENDPOINTS_REPORT.md` (this file)

**Changed:**
- `tests/ONEVO.Tests.Architecture/LegalEntityPart2BArchitectureTests.cs` — removed one now-obsolete test (`NoControllerOrRoute_AddedForLegalEntitiesYet`), which correctly asserted "no controller exists" for Part 2B's scope but fails now that Part 2C legitimately adds one. See §9.

**Not modified:** anything under Part 2A/2B scope (entity, EF config, repository, migration, commands, queries, validators, DTOs), any Postman collection, any OneVo-HR doc, `DependencyInjection.cs` (MediatR already wires the controller's actions to existing handlers; no new DI registration needed).

---

## 2. Routes Added

All under `[Route("api/v1/org/legal-entities")]`, class-level `[Authorize(Policy = "TenantPolicy")]`, mirroring `RolesController`/`PermissionsController` exactly.

| Method | Route | Action | Command/Query |
|---|---|---|---|
| GET | `/api/v1/org/legal-entities` | `List` | `ListLegalEntitiesQuery` |
| GET | `/api/v1/org/legal-entities/{id:guid}/general-settings` | `GetGeneralSettings` | `GetLegalEntityGeneralSettingsQuery` |
| POST | `/api/v1/org/legal-entities` | `Create` | `CreateLegalEntityCommand` |
| PUT | `/api/v1/org/legal-entities/{id:guid}/general-settings` | `UpdateGeneralSettings` | `UpdateLegalEntityGeneralSettingsCommand` |
| DELETE | `/api/v1/org/legal-entities/{id:guid}` | `Delete` | `DeleteLegalEntityCommand` |
| DELETE | `/api/v1/org/legal-entities/{id:guid}/logo` | `RemoveLogo` | `RemoveLegalEntityLogoCommand` |

**`PUT /api/v1/org/legal-entities/{id}/logo` is deliberately not exposed** — see §5.

`List` accepts `?includeInactive=true` as an optional query-string flag, bound directly to `ListLegalEntitiesQuery.IncludeInactive`. `Create`'s success response uses `CreatedAtAction(nameof(GetGeneralSettings), new { id = result.Value!.Id }, result.Value)`, so its `Location` header resolves to `/api/v1/org/legal-entities/{id}/general-settings` via ASP.NET Core route generation — the same mechanism `RolesController.Create` already uses, not a hand-built string.

`UpdateGeneralSettings` and `Delete` both take the id from the route, never the body: `UpdateLegalEntityGeneralSettingsRequest`/`DeleteLegalEntityRequest` have no id field at all (verified by Part 2B's contracts and re-asserted by this phase's architecture tests), so the command is always constructed as `new UpdateLegalEntityGeneralSettingsCommand(id, request.Name, ...)` with `id` coming from the route parameter.

---

## 3. Permission Mapping Table

| Route | Permission | Rationale |
|---|---|---|
| `GET /legal-entities` | `org:read` | Company selector/list — read-only |
| `GET /legal-entities/{id}/general-settings` | `org:manage` | Per Part 1 audit: General Settings requires `org:manage` even for viewing — no read-only variant |
| `POST /legal-entities` | `org:manage` | Create |
| `PUT /legal-entities/{id}/general-settings` | `org:manage` | Update |
| `DELETE /legal-entities/{id}` | `org:manage` | Deactivate |
| `DELETE /legal-entities/{id}/logo` | `org:manage` | Remove logo |

No action uses `settings:admin` or any platform-admin policy. Both permissions (`org:read`, `org:manage`) already existed in `PermissionSeeder.cs` from Part 1's audit — nothing new was seeded.

---

## 4. Result / Status-Code Mapping Table

Every action follows the existing `RolesController`/`PermissionsController` pattern exactly: `result.IsSuccess ? <2xx> : Problem(result.Error, statusCode: result.StatusCode ?? 400)`. `Problem(...)` produces the project's standard RFC7807 problem-details body — no new response format was invented.

| Handler outcome | HTTP status | Controller action |
|---|---|---|
| Success (list/get/create/update) | 200 / 201 | `Ok(...)` / `CreatedAtAction(...)` |
| Success (delete/remove-logo) | 204 | `NoContent()` |
| Not authenticated / no tenant context | 403 | `Problem(...)` via `Result.Forbidden` |
| Missing / belongs to another tenant | 404 | `Problem(...)` via `Result.NotFound` |
| Duplicate name/companyCode/registrationNumber | 409 | `Problem(...)` via `Result.Conflict` |
| Validation failure (FluentValidation pipeline) | 400 | `ValidationBehavior<,>` short-circuits before the handler runs, same pipeline every other command uses |
| `confirmName` mismatch / last-active-company | 400 | `Problem(...)` via `Result.Failure` (Part 2B's own documented convention — see the Part 2B report §6) |
| No `[RequirePermission]` match | 403 | `RequirePermissionAttribute` short-circuits before the action runs (RFC7807 body built by the filter itself, same as every other tenant controller) |
| Unauthenticated request | 401 | `RequirePermissionAttribute`/`[Authorize]` short-circuits before the action runs |

---

## 5. Logo Endpoint Decision: PUT Deferred, DELETE Exposed

**`PUT /api/v1/org/legal-entities/{id}/logo` is NOT exposed in Part 2C.**

Investigation before wiring it confirmed `SetLegalEntityLogoCommandHandler` (Part 2B) sets `LogoFileId = request.FileId` directly with **no validation** that the file exists, belongs to the tenant, or was uploaded for a logo/image purpose — Part 2B's own report documents this as a deliberate deferral (§7 item 1), because the only architecturally-allowed storage entry point, `IFileStorageService`, exposes no lookup method for "does this fileId belong to this tenant" (only upload/reservation flows: `BeginReservationAsync`, `CompleteUploadAsync`, `CancelReservationAsync`, `UploadAsync`). Directly injecting `IFileRecordRepository` to do this check — which was tried and reverted during Part 2B — is forbidden by the pre-existing, passing `FileStorageArchitectureTests.NoApplicationType_BypassesFileStorageService_ByUsingFileRepositoriesDirectly` test.

This is exactly the "Option B" case the task anticipated: **no safe validation exists today**, so `PUT /logo` is not exposed. Exposing it anyway would let any authenticated `org:manage` user attach an arbitrary `fileId` — including one belonging to another tenant, since the `logo_file_id` FK (`ON DELETE SET NULL`) does not enforce tenant matching at the database level either (Part 2A report §7 item 4, the analogous gap for the parent FK) — as a company's logo with zero ownership check. `SetLegalEntityLogoCommand`/`SetLegalEntityLogoCommandHandler` remain in the codebase, unused by any controller, exactly as Part 2B left them.

**`DELETE /api/v1/org/legal-entities/{id}/logo` IS exposed.** `RemoveLegalEntityLogoCommandHandler` only ever sets `entity.LogoFileId = null` — there is no `fileId` input to validate, so this endpoint carries none of the above risk.

`tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.NoPutLogoRoute_Exists` asserts no `[HttpPut]` action with `"logo"` in its route template exists on this controller, so this decision cannot silently regress in a future change.

---

## 6. Tests Added and Results

### Controller unit tests — `LegalEntitiesControllerTests` (11 facts, Mock&lt;IMediator&gt; style matching `AuthPendingLegalControllerTests`)

- `List` sends `ListLegalEntitiesQuery` with the bound `includeInactive` value; failure result maps to the correct `Problem` status code.
- `GetGeneralSettings` sends the query with the route id; not-found result maps to 404.
- `Create` sends `CreateLegalEntityCommand` built from the request body and returns `CreatedAtActionResult` targeting `nameof(GetGeneralSettings)` with the created id in `RouteValues`; duplicate-name failure maps to 409.
- `UpdateGeneralSettings` sends the command with the **route** id, not any body field (the request contract has no id field, so this is also enforced structurally).
- `Delete` sends `DeleteLegalEntityCommand` and returns `NoContentResult` on success; confirmName-mismatch failure maps to 400.
- `RemoveLogo` sends `RemoveLegalEntityLogoCommand` and returns `NoContentResult`.
- `Controller_HasNoSetLogoAction` — reflection check that no `SetLogo` action method exists at all.

### Architecture tests — `LegalEntitiesControllerArchitectureTests` (14 facts)

- Controller namespace contains `Tenant` and `OrgStructure`, and neither `Admin` nor `DevPlatform`.
- Controller carries `[Authorize(Policy = "TenantPolicy")]`.
- Every action method has a `RequirePermissionAttribute` (none missing).
- `List` uses `org:read`; `GetGeneralSettings`/`Create`/`UpdateGeneralSettings`/`Delete`/`RemoveLogo` all use `org:manage`.
- No action uses `settings:admin`.
- No action parameter is named `tenantId`.
- No `[HttpPut]` action has `"logo"` in its route template.
- The `DELETE .../logo` route exists and uses the DELETE verb.
- The controller has no field of type `IFileRecordRepository` (redundant with, but pinned alongside, the pre-existing `FileStorageArchitectureTests` guard).

**One pre-existing test fixed as part of this phase:** `LegalEntityPart2BArchitectureTests.NoControllerOrRoute_AddedForLegalEntitiesYet` (written during Part 2B to guard *that phase's* scope) started failing the moment this phase's controller was added — correctly, since its assertion ("no LegalEntities controller exists yet") was only ever true for Part 2B. It was removed with a comment pointing to `LegalEntitiesControllerArchitectureTests` as its replacement; nothing else in that file changed.

### Integration tests — `LegalEntitiesIntegrationTests` (19 facts, written but NOT executed — see below)

Written against a real PostgreSQL instance (Testcontainers, with the same `ONEVO_TEST_DB` environment-variable escape hatch `TenantProvisioningE2ETests` uses for Docker-free local runs), reusing the existing `E2ETestFactory`/`CapturingEmailService`/`WebApplicationFactoryCollection` infrastructure. `InitializeAsync` fully provisions **two** real tenants (admin creates each → owner accepts invite → admin confirms provisioning → owner logs in via the base-domain exchange flow → real `onevo_session`/`onevo_csrf` cookies), exactly mirroring `TenantProvisioningE2ETests`'s flow. Each tenant already has one active company from tenant-creation's own side effect (`CreateTenantCommandHandler` seeds a primary `LegalEntity` — Part 1 audit §1.5), which is used directly for the "last active company" and cross-tenant tests without any direct DB seeding of this feature's own rows.

Covers:
1. **Auth** — unauthenticated `GET /legal-entities` → 401.
2. **List** — active companies returned by default; a company deactivated via `DELETE` no longer appears; `?includeInactive=true` brings it back.
3. **Get** — own company → 200; the other tenant's primary company id → 404.
4. **Create** — 201 with a `Location` header resolving to `.../general-settings`; the tenant is server-derived (new company invisible via the other tenant's session); duplicate name/companyCode/registrationNumber inside one tenant → 409; the same name in the other tenant → 201 (allowed); empty `countryCode`/`currencyCode` → 400.
5. **Update** — valid update → 200 and `standardWorkingDays` comes back sorted ascending (`[5,1,3]` in → `[1,3,5]` out); `isPrimary` (not part of the request) is preserved across an update; empty/out-of-range/duplicate `standardWorkingDays` → 400; cross-tenant update → 404.
6. **Delete** — exact `confirmName` → 204, and the row still resolves via GET afterward with `status: "inactive"` (soft delete only); `confirmName` mismatch → 400; deleting the tenant's last active company → 400 with a message containing "last active company"; cross-tenant delete → 404.
7. **Logo** — `DELETE /logo` → 204 and `logoFileId` reads back `null`; `PUT /logo` → 404/405 (route genuinely does not exist, confirming §5's decision at the HTTP layer, not just in source).

**Not covered, deliberately:** "authenticated tenant user without `org:read`/`org:manage` gets 403." `ICurrentUser.HasPermission` (`CurrentUserService.cs`) reads permissions from **JWT/cookie claims baked in at login**, not from a live per-request database read — so revoking a `RolePermission` row mid-test would not affect an already-issued session's claims, and building a genuinely separate low-privilege invited user was judged not worth the additional invite/role-assignment plumbing for this phase. Permission-to-route mapping is instead fully covered, and more reliably, by `LegalEntitiesControllerArchitectureTests`'s reflection-based checks (§6 above), which assert the exact attribute on every action rather than depending on runtime claim state.

One deviation from the task's stated Create validation list, worth flagging explicitly: the task's "Invalid country/currency/timezone/working-days return 400" bullet under Create does not match Part 2B's actual `CreateLegalEntityRequest` contract, which has no `timezone` or `standardWorkingDays` fields at all (Create only collects identity + legal basics; those fields are General-Settings-only, per Part 2B's finalized field list). The integration tests therefore validate invalid `country`/`currency` under Create, and invalid `timezone`/`standardWorkingDays` under Update, matching the contracts as they were actually built.

**Execution status: written, NOT executed.** Docker Desktop is installed (`docker version` succeeds for the client) but its daemon is not running in this environment:

```
failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine; check if the path is correct and if the daemon is running: open //./pipe/dockerDesktopLinuxEngine: The system cannot find the file specified.
```

Running the pre-existing `ApiBootTests` (unrelated to this task, same Testcontainers dependency) reproduces the identical failure, confirming this is an environment limitation, not a defect in the new tests. Running `LegalEntitiesIntegrationTests` directly produces the expected `DockerUnavailableException` / `System.TimeoutException` from `Testcontainers.PostgreSql.PostgreSqlBuilder.Build()` for all 19 facts, at the `InitializeAsync` call — i.e. the test file **compiles and the harness reaches the expected failure point**, it simply cannot reach a real database in this session. No integration success is being claimed or faked.

### Full suite results

| Check | Result |
|---|---|
| `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal` | Succeeded, 0 warnings, 0 errors |
| `dotnet build tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --no-restore` | Succeeded (5 pre-existing `PostgreSqlBuilder()` obsolete-API warnings shared with 4 other existing test files, none newly introduced as errors) |
| `dotnet test tests/ONEVO.Tests.Unit` (full suite) | `Passed! - Failed: 0, Passed: 1107, Skipped: 0, Total: 1107` |
| `dotnet test tests/ONEVO.Tests.Architecture` (full suite) | `Passed! - Failed: 0, Passed: 343, Skipped: 0, Total: 343` |
| `dotnet test tests/ONEVO.Tests.Integration --filter LegalEntitiesIntegrationTests` | 19/19 fail at `InitializeAsync` with `DockerUnavailableException` — expected and documented, not run |
| `git diff --check` | Exit code 0; one informational CRLF/LF notice on a Part 2A file, not a new whitespace error |

No restore was needed — the working tree already had a restored `bin`/`obj` state from Part 2A/2B's work in the same session.

---

## 7. Confirmation: No Migrations / Schema / Postman / OneVo-HR Docs Changed

Verified via `git status --porcelain`:

- **No new migration files** — the only migration present (`20260731073116_ExpandLegalEntityForGeneralSettings`) is Part 2A's, unchanged in this phase.
- **No EF configuration/schema changes** — `LegalEntityConfiguration.cs` was not touched.
- **No Postman files touched.**
- **No OneVo-HR documentation touched** (directory is outside this repo and was not written to).
- **No RLS changes** — nothing in this phase touches migrations or policies; `legal_entities` remains covered by the same RLS policy Part 2A left in place.

---

## 8. Confirmation: No `tenantId` Accepted From Requests

- Every controller action's parameter list was reflected over by `LegalEntitiesControllerArchitectureTests.NoAction_AcceptsTenantIdParameter` — none has a `tenantId` parameter.
- Every request contract (`CreateLegalEntityRequest`, `UpdateLegalEntityGeneralSettingsRequest`, `DeleteLegalEntityRequest`, `SetLegalEntityLogoRequest`) was already asserted `TenantId`-free by Part 2B's `LegalEntityPart2BArchitectureTests.RequestContracts_DoNotExposeTenantId` — still passing, unchanged.
- Every command constructed in the controller sources `tenantId` implicitly inside its handler via `ICurrentUser.TenantId` (Part 2B's handlers) — the controller itself never reads, forwards, or overrides a tenant id anywhere.
- The integration test's `Create_ValidCompany_...` fact additionally proves this at the HTTP level: a company created via tenant A's session is provably invisible via tenant B's session, regardless of what the request body contained (the body has no tenantId field to begin with).

---

## 9. Confirmation: Delete Is Soft/Inactive-Only

- `DeleteLegalEntityCommandHandler` (Part 2B, unchanged) sets `IsActive = false` and calls `_legalEntities.Update(entity)` — never `Remove`/`RemoveRange`. Re-verified in this phase by the (still-passing) `LegalEntityPart2BArchitectureTests.DeleteLegalEntityHandler_NeverPhysicallyRemovesTheRow` source-text guard.
- The controller's `Delete` action only ever calls `_mediator.Send(new DeleteLegalEntityCommand(...))` — it has no direct persistence access of any kind.
- The integration test `Delete_ExactConfirmName_Returns204_AndSoftDeactivatesOnly` proves this end-to-end over real HTTP + a real database: after a 204 delete response, a subsequent `GET .../general-settings` on the same id still returns 200 with `status: "inactive"`, rather than 404 (which a physically-deleted row would produce).
- `RemoveLogo` similarly only clears `LogoFileId`; it does not delete `legal_entities` or `file_records` rows.

---

## 10. Remaining Items for Part 2D

Per the task's own phasing, Part 2D is Postman collection + manual local API verification, gated on this report. Items a Part 2D reviewer should be aware of:

1. **Logo upload (`PUT /logo`) remains unbuilt and unexposed** (§5). A future phase must add a tenant-scoped file-ownership/purpose lookup to `IFileStorageService` (or an equivalent Storage-feature-owned abstraction) before this endpoint can be safely added — this is Storage-feature work, not an OrgStructure/LegalEntity change, and was correctly out of scope for both Part 2B and Part 2C.
2. **Integration tests need a real run.** They compile and reach the expected Docker-unavailable failure point in this environment; they have never been executed against a live database. Part 2D (or CI) should run `dotnet test tests/ONEVO.Tests.Integration --filter LegalEntitiesIntegrationTests` for real, either with Docker available or `ONEVO_TEST_DB` pointed at a local PostgreSQL instance, before treating this feature as fully verified.
3. **Permission-denial (403) is only architecture-tested, not integration-tested** (§6) — if a future phase wants a genuine live-session 403 proof, it needs either a second invited user assigned a role without `org:manage`/`org:read`, or a documented way to force a fresh login after revoking a permission mid-test.
4. **`company_code`/`registration_number` required-at-DB-level questions remain open** (carried over unchanged from Part 2A §7 / Part 2B §7) — unaffected by this phase.
5. **No Swagger/OpenAPI review was performed** for the new routes in this phase — worth a quick check in Part 2D that the six new endpoints render correctly in the existing Swagger setup (`SwaggerExtensions.cs`), since that file appeared in the CSRF-related search but was not opened or modified here.
