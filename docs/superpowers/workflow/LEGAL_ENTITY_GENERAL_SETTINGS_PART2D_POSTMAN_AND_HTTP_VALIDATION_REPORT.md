# Legal Entity / Company General Settings — Part 2D Postman & HTTP Validation Report

**Scope:** Re-verification, Swagger route confirmation, Postman collection update, environment variables, manual-flow documentation, and required source checks. No backend behavior, schema, or docs were changed.
**Repo:** `C:\onevoNew\HRMS-Backend-v1`
**Environment note:** Docker was available in this session (it was not in Parts 2A–2C), which let several checks run for real instead of being documented as blocked.

---

## 1. Re-Run Verification

All four commands ran exactly as specified, plus the full integration suite since Docker was available.

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` | Succeeded, 0 warnings, 0 errors |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal` | `Passed! - Failed: 0, Passed: 1107, Skipped: 0, Total: 1107` |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal` | `Passed! - Failed: 0, Passed: 343, Skipped: 0, Total: 343` |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter "LegalEntity\|LegalEntities" --no-restore --verbosity minimal` | `Passed! - Failed: 0, Passed: 19, Skipped: 0, Total: 19` — **against a real PostgreSQL via Testcontainers**, not merely compiled (Docker was unavailable for this in Parts 2A–2C) |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --verbosity minimal` (full suite, Docker available) | `Failed: 1, Passed: 131, Total: 132` on first run |

**The one full-suite failure was proven unrelated, per the task's instruction to classify only after a focused rerun:**

- Failing test: `BaseForgotPasswordRestrictedRoleHttpIntegrationTests` (unrelated feature — base-domain forgot-password flow, `tests/ONEVO.Tests.Integration/Auth/`).
- Failure mode: `Npgsql.NpgsqlException: Failed to connect ... SocketException: ... actively refused it` inside `WebApplicationFactory.CreateDefaultClient` → classic Testcontainers port-contention flakiness when ~20 parallel Testcontainers-backed test classes start Postgres containers simultaneously in one run, not a code defect.
- Focused rerun: `dotnet test ... --filter "FullyQualifiedName~BaseForgotPasswordRestrictedRoleHttpIntegrationTests"` → `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`.
- Conclusion: confirmed environment flakiness, unrelated to any Legal Entity change in Parts 2A–2D. No source code was touched to "fix" it, per the task's explicit instruction not to touch unrelated auth code.

---

## 2. Swagger/OpenAPI Route Exposure — Verified Live

Rather than reasoning about this from source alone, the API was actually run against a real PostgreSQL database in this session (see §5 for the full setup) and its live `swagger.json` was fetched and inspected.

```
GET /api/v1/org/legal-entities                              -> [ 'get', 'post' ]
GET /api/v1/org/legal-entities/{id}/general-settings         -> [ 'get', 'put' ]
GET /api/v1/org/legal-entities/{id}                          -> [ 'delete' ]
GET /api/v1/org/legal-entities/{id}/logo                     -> [ 'delete' ]
```

All 6 required method/path combinations are present. **`put` does not appear under `/api/v1/org/legal-entities/{id}/logo`** — confirmed absent both in the live OpenAPI document and by the architecture test (`LegalEntitiesControllerArchitectureTests.NoPutLogoRoute_Exists`, still passing).

---

## 3. Postman Collection Updated

Collection: `postman/collections/ONEVO Organization Admin API` (the tenant/customer-facing collection — the Developer Platform admin collection was not touched).

**New folder:** `06. Organization - Companies` (10 request files, all new).

| # | Request | Method | Route |
|---|---|---|---|
| A | List Companies | GET | `{{base_url}}/api/v1/org/legal-entities` |
| B | Get Company General Settings | GET | `{{base_url}}/api/v1/org/legal-entities/{{legal_entity_id}}/general-settings` |
| C | Create Company | POST | `{{base_url}}/api/v1/org/legal-entities` |
| D | Update Company General Settings | PUT | `{{base_url}}/api/v1/org/legal-entities/{{legal_entity_id}}/general-settings` |
| E | Remove Company Logo | DELETE | `{{base_url}}/api/v1/org/legal-entities/{{legal_entity_id}}/logo` |
| F | Delete Company | DELETE | `{{base_url}}/api/v1/org/legal-entities/{{legal_entity_id}}` |
| G | Create Company - Duplicate Name Should 409 | POST | same as C, reusing the name |
| H | Delete Company - Wrong Confirm Name Should 400 | DELETE | same as F, wrong `confirmName` |
| I | Delete Company - Last Company Should Fail 400 | DELETE | same as F |
| J | Get Missing Company Should 404 | GET | nil-GUID id |

`Create Company` carries an `afterResponse` script (matching the one existing example of this pattern in the repo, `01. Admin Auth/Login.request.yaml`) that saves the response's `id` into `{{legal_entity_id}}`. No other repo Postman request uses a script beyond that one precedent, so no other request here was given one.

### Two body corrections made to match the real, implemented API — not the task's literal draft text

The task's literal Create/Update JSON bodies were written against an **earlier draft** of the contract (matching the Part 1 audit's speculative field list), not the contract Part 2B actually shipped. Pasting them verbatim would have produced requests that either silently dropped data or hard-failed validation. Both were corrected, since the entire point of this phase is validating the *real* HTTP behavior:

1. **`"address"` → `"registeredBusinessAddress"`.** `CreateLegalEntityRequest`/`UpdateLegalEntityGeneralSettingsRequest` (Part 2B, confirmed by rereading the actual source) both use the field name `registeredBusinessAddress`. Sending `"address"` as given would have been silently ignored by System.Text.Json's default unknown-property handling — the request would still succeed, just without ever capturing the address.
2. **Update body: `"isActive": true` → `"status": "active"`.** The real `UpdateLegalEntityGeneralSettingsRequest` has no `isActive` field at all — the field is `status`, a required string restricted to `"active"`/`"inactive"` by `UpdateLegalEntityGeneralSettingsCommandValidator`. Sending `isActive` and omitting `status` would have made every real run of "Update Company General Settings" fail with 400 ("Status must be 'active' or 'inactive'."), silently, the first time anyone actually ran it.
3. **Create body: dropped `vatGstNumber`, `email`, `phoneNumber`, `website`, `timezone`, `financialYearStartMonth`, `firstDayOfWeek`, `standardWorkingDays`, `defaultLanguage`, `dateFormat`, `timeFormat`.** None of these exist on `CreateLegalEntityRequest` — Create only collects identity + legal basics by design (Part 2B report, confirmed again by rereading the source); those fields are General-Settings-only and were kept on the Update request, where they do exist. Leaving them on Create would not have broken anything (unknown JSON properties are ignored), but would have documented a request shape that doesn't match the real contract, which is exactly the kind of drift this validation phase exists to catch.

Everything else from the task's literal request bodies (sample values, header names, `X-CSRF-Token` usage, negative-test intents) was kept as given. `countryCode`/`address.country` use the task's `"LK"` (ISO 3166-1 **alpha-2**) rather than the `"LKA"` (alpha-3) convention used everywhere else in the codebase's own tests — this was left as-is because the validator only checks `MaximumLength(3)`, not an exact alpha-3 format, so `"LK"` passes validation; it's flagged here only as a documentation inconsistency, not a functional bug.

### Environment variables

`postman/environments/New Environment.environment.yaml` already had `base_url`, `tenant_email`, `tenant_password`, and `tenant_csrf_token` (all matching the task's required values exactly, including `tenant_email: siyasiyamala932@gmail.com`). The only addition made: `legal_entity_id: ''`.

`tenant_host` was not used anywhere in the new folder — every new request uses `{{base_url}}`, per the task's explicit instruction and per the actual backend behavior (Legal Entity routes resolve tenant from the authenticated session/exchange flow, not from host-based routing).

### A pre-existing, unrelated folder was found and left untouched

`07. Organization - Company` (already present before this phase) contains four requests (`List/Get/Create/Update Legal Entity`) that reference `{{tenant_host}}/organization/legal-entities` with a `PATCH` verb and flat `country`/`address` string fields — **none of which match any route this backend has ever exposed**. This is stale/speculative content, unrelated to Parts 2A–2C's actual implementation. The task scoped this phase to creating/updating `06. Organization - Companies` only, so `07. Organization - Company` was deliberately left as-is rather than deleted or merged — flagging it here as a cleanup candidate for whoever owns the Postman collection next, not fixing it unprompted.

### Postman files are gitignored — expected, not an error

`postman/` is listed in `.gitignore` (line 26), so **none** of the Postman changes in this phase (or any pre-existing Postman file, including `07. Organization - Company`) appear in `git status`. This is the existing, working convention for this repo (the Postman workspace is synced/managed outside this git history) — confirmed by checking that every other pre-existing Postman file is equally untracked. The new files exist on disk exactly as listed above.

---

## 4. Manual Validation Flow

### Documented flow (as specified)

1. **Login from base domain:** `POST {{base_url}}/api/v1/auth/login` with `{{tenant_email}}`/`{{tenant_password}}`. If the response indicates workspace selection, MFA, or pending legal acceptance, resolve that first. Capture `onevo_session` and `onevo_csrf` from `Set-Cookie`; set `{{tenant_csrf_token}}` to the `onevo_csrf` cookie value.
2. `GET` **List Companies** → expect 200 and at least one company (every tenant has one primary company from provisioning).
3. `POST` **Create Company** → expect 201, response contains `id`; the `afterResponse` script saves it to `{{legal_entity_id}}`.
4. `GET` **Get Company General Settings** → expect 200, fields match what was just created.
5. `PUT` **Update Company General Settings** → expect 200 (this controller returns `Ok(result.Value)` on success, never 204, per `LegalEntitiesController.UpdateGeneralSettings` — confirmed by rereading the Part 2C source). Re-`GET` to verify changed fields.
6. `DELETE` **Remove Company Logo** → expect 204; re-`GET` shows `logoFileId: null`.
7. `DELETE` **Delete Company** with a wrong `confirmName` → expect 400.
8. `DELETE` **Delete Company** with the correct `confirmName` → expect 204 (unless it is the tenant's last active company).
9. Attempt to delete the tenant's last remaining active company → expect 400, row must remain.

### Execution status: real evidence via two independent paths, not a raw curl walkthrough

**Path A — proven for real, in this session:** `LegalEntitiesIntegrationTests` (Part 2C) runs this exact flow end-to-end — real tenant provisioning via the admin API, real owner-invite acceptance, real base-domain login → session-exchange → `onevo_session`/`onevo_csrf` cookies, then every one of steps 2–9 above against a real PostgreSQL database via Testcontainers. It passed **19/19** in this session (§1), which is a stronger proof than a manual run because it asserts exact status codes and response bodies rather than requiring a human to eyeball them.

**Path B attempted — a real, running local instance:** since Docker was available, this session also stood up the actual API host against a real (throwaway) PostgreSQL database — not Testcontainers, a genuinely separate `docker run postgres:16-alpine` container, bootstrapped with the project's own `ops/postgres/local-bootstrap-roles.sql`/`local-post-migration-grants.sql`, migrated with `dotnet ef database update` (this was also the **first time** the Part 2A `ExpandLegalEntityForGeneralSettings` migration has ever been applied to a real database — it only existed as generated-but-unapplied DDL until now, and it applied cleanly), and run via `dotnet run`. Admin login (`POST /admin/v1/auth/login`) succeeded for real against this instance, proving the app boots and authenticates correctly end-to-end outside the test harness. Fetching `/swagger/v1/swagger.json` from this live instance is what produced §2's route confirmation.

Continuing the manual flow past admin login required creating a tenant via `POST /admin/v1/tenants` (to get an owner account matching `{{tenant_email}}`, since that account does not pre-exist in a fresh database). This call returned `500 Internal Server Error` — the server log showed `Npgsql.PostgresException 42501: new row violates row-level security policy for table "invitation_tokens"`. This is a genuine RLS/session-context issue in the **pre-existing tenant-provisioning path** (`CreateTenantCommandHandler`, `invitation_tokens` table) — entirely outside Legal Entity code, and the task explicitly lists tenant-provisioning code as off-limits to touch or debug. It most likely reflects an incomplete manual role/session bootstrap on my part (this throwaway instance skipped whatever additional grant or `SET ROLE`/session-variable step production's real deploy process performs for admin-context writes) rather than a real product bug — the identical tenant-creation code path passed cleanly, for real, 19/19 times minutes earlier via the project's own Testcontainers-backed integration harness, which sets up RLS through the exact same migrations. Per the task's explicit scope boundary, this was not investigated further or fixed; the throwaway Postgres container, the temporary repo-root `.env`, and the running API process were all torn down afterward. `git status` was re-checked and shows no residue from this exercise.

**Net result:** the manual flow is documented exactly as specified above; steps 1 and 3–9 already have a genuine, passing, automated proof (Path A) from this same session; step 2 (Swagger/boot) has an independent, genuine manual proof (Path B). No result in this report was fabricated or assumed.

---

## 5. Required Source Checks

All run as literal greps against the actual current source, not inferred from memory.

| Check | Command / target | Result |
|---|---|---|
| No `tenantId` in request contracts | `grep TenantId` over `src/ONEVO.Api/Contracts/OrgStructure/` | 0 matches |
| PUT logo route absent | `grep HttpPut` over `LegalEntitiesController.cs` | Only one match: `[HttpPut("{id:guid}/general-settings")]` — no logo route |
| DELETE logo route present | `grep HttpDelete` over `LegalEntitiesController.cs` | Two matches: `{id:guid}` and `{id:guid}/logo`, both `[RequirePermission("org:manage")]` |
| `org:read`/`org:manage` usage correct | `grep RequirePermission` over `LegalEntitiesController.cs` | `List` → `org:read`; `GetGeneralSettings`, `Create`, `UpdateGeneralSettings`, `Delete`, `RemoveLogo` → `org:manage` — matches the task's permission table exactly |
| No direct `DbContext` in the controller | `grep "ApplicationDbContext|DbContext"` over `LegalEntitiesController.cs` | 0 matches |
| No storage-repository direct usage in the LegalEntity feature | `grep "IFileRecordRepository|IFileUploadReservationRepository"` over `Features/OrgStructure/LegalEntity/` | 1 match, in `SetLegalEntityLogoCommandHandler.cs` — verified by reading it: it is a **comment** explaining why that dependency is deliberately *not* used (Part 2C's reasoning for deferring `PUT /logo`), not an actual field, constructor parameter, or usage |

---

## 6. Confirmation: PUT /logo Remains Deferred

Confirmed at every layer checked in this phase:

- **Source:** no `[HttpPut(".../logo")]` action exists on `LegalEntitiesController`.
- **Live Swagger:** no `put` operation under `/api/v1/org/legal-entities/{id}/logo` in the actual running app's OpenAPI document (§2).
- **Architecture test:** `LegalEntitiesControllerArchitectureTests.NoPutLogoRoute_Exists`, still passing.
- **Postman:** `06. Organization - Companies` contains only `Remove Company Logo` (DELETE); no Set/Upload Logo request was added, per the task's explicit instruction.

The underlying reason is unchanged from Part 2C: `SetLegalEntityLogoCommandHandler` sets `LogoFileId` with no tenant-ownership/purpose validation, and the only architecturally-allowed storage entry point (`IFileStorageService`) has no lookup method that could provide it without becoming an out-of-scope Storage-feature change.

---

## 7. Files Changed in This Phase

**Created:**
- `postman/collections/ONEVO Organization Admin API/06. Organization - Companies/*.request.yaml` (10 files, listed in §3)
- `LEGAL_ENTITY_GENERAL_SETTINGS_PART2D_POSTMAN_AND_HTTP_VALIDATION_REPORT.md` (this file)

**Changed:**
- `postman/environments/New Environment.environment.yaml` — added `legal_entity_id: ''`

**Not changed:** any C#/backend source, any migration/schema/entity/repository/application file, any OneVo-HR documentation, any unrelated auth/payment/legal-document/MFA/password-reset/storage/tenant-provisioning code, git history/staging/commits.

**Created and fully removed again (no trace left):** a temporary repo-root `.env` file and a throwaway Docker Postgres container (`onevo-legal-e2e`), used only for the live Swagger/boot verification in §2/§4 and torn down immediately after. `git status` was re-verified clean of any residue from this exercise.

---

## 8. Confirmation: No Backend Behavior/Schema/Docs Changed

No compile or runtime blocker was found in any Legal Entity code during this phase (build, unit, architecture, and the real Testcontainers-backed integration suite all passed). The one real blocker encountered (§4, the `invitation_tokens` RLS error during a from-scratch manual environment build) is in unrelated tenant-provisioning code, was not proven to be a genuine product defect (the same code path passes for real via the project's own integration harness), and — per the task's explicit scope boundary — was neither investigated further nor "fixed." No migration, schema, entity, repository, application-layer file, or OneVo-HR document was modified anywhere in this phase.

---

## 9. Remaining Items (Beyond This Phase's Scope)

1. **`07. Organization - Company`** (pre-existing, stale) still exists alongside the new `06. Organization - Companies` folder — a future cleanup pass should decide whether to delete or repurpose it, to avoid confusing anyone browsing the collection.
2. **The `invitation_tokens` RLS gap** encountered while manually bootstrapping a from-scratch dev database (§4) is worth a real investigation by whoever owns local dev tooling — either `ops/postgres/setup-local-db.ps1`/`local-bootstrap-roles.sql` is missing a grant needed for admin-context tenant provisioning against a truly from-scratch database, or there's an environment-specific step this session's manual bootstrap didn't replicate. This is unrelated to Legal Entity and was correctly left untouched here.
3. **Logo upload (`PUT /logo`)** remains unbuilt, per Part 2C's decision, reconfirmed here.
