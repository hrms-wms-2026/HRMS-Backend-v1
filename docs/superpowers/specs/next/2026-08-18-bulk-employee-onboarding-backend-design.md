# Bulk Employee Onboarding — Backend Design

**Status:** Approved by user 2026-08-18, ready for implementation planning.

**Companion spec:** `Hrms--Web-application---front-end---v1/docs/superpowers/specs/2026-08-18-bulk-employee-onboarding-frontend-design.md` (frontend consumer of this API — the upload/map/validate/review wizard reached from Settings → Bulk Onboarding). This document is the backend half; the two share the API contract in §6.

**Origin:** brainstormed live with the user 2026-08-18 via `superpowers:brainstorming`. Grounded in `docs/superpowers/project_ core/phase1-table-inventory.md`, `ONEVO_Backend_Architecture_Document.md` (updated same session — see §7), and cross-checked against the actual current codebase, in particular the existing single-employee onboarding-draft flow this feature reuses.

---

## 1. Goal

Let HR upload a CSV of many prospective employees at once, map its columns to system fields with a one-row preview before committing, validate every row (partial success — bad rows are reported, good rows proceed), bulk-create `onboarding_drafts` for the valid rows, review the batch, and bulk-finalize selected drafts (creating employees/users/invitations exactly as the existing single-employee finalize flow does, just looped).

## 2. Scope

**In scope:** CSV upload and parsing, ephemeral (this-upload-only) column mapping with a resolved-preview step, per-row validation with partial success and per-row error reporting, background batch processing for draft creation and finalize (per the backend NFR: "Use background jobs for payroll, imports, exports, reports, and retention cleanup" — bulk onboarding is exactly an import), a batch review surface, and bulk finalize with the same aggregate outcomes (`finalized` / `waiting_for_seat` / `waiting_for_position_approval` / `failed`) the single finalize endpoint already produces.

**Out of scope (phase boundaries):** `.xlsx`/Excel parsing — CSV only for phase 1; the user's brief said "CSV/Excel" but `.xlsx` needs a new licensed dependency (EPPlus/ClosedXML) and isn't worth adding until asked for explicitly. Auto-creating missing Department/Position from CSV values — both must already exist; a row referencing a missing one fails validation with a specific message pointing at where to create it (explicit product decision — see brainstorming transcript). A "Reporting Manager" CSV column — **corrected after the design was approved**: verified against `Employee.cs` (`src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs`) and `SaveOnboardingDraftCommand`, neither has any manager-like field to write to. The only reporting relationship in the schema is `Position.ReportsToPositionId` (`src/ONEVO.Domain/Features/OrgStructure/Position/Entities/Position.cs:18`) — a position-to-position link configured in Organization → Positions, not a per-employee value. Reporting manager is therefore entirely implied by whichever Position a row resolves to (§4.2's `resolved_position_id`); there is nothing for a separate manager column to set, in bulk or single-employee onboarding alike, so none is added. Persisted/reusable column-mapping templates ("save this mapping for next time") — that is the already-deferred `data_import_mapping` configuration-template idea (see `project_config_templates_phase1.md`); this feature's mapping is ephemeral, scoped to one upload, and does not reopen that deferred work. Storing the uploaded CSV file in Cloudflare R2 — see §4.3 for why raw rows are persisted directly instead.

## 3. Current-state facts this design depends on

Verified directly against the codebase:

- **The single-employee flow this feature extends already exists and works**: `SaveOnboardingDraftCommand`/`SaveOnboardingDraftCommandHandler` creates/updates an `onboarding_drafts` row; `FinalizeOnboardingDraftCommand`/`FinalizeOnboardingDraftCommandHandler` (`src/ONEVO.Application/Features/CoreHr/OnboardingDrafts/Commands/...`) converts a draft into a real `Employee` + `User` + `InvitationToken` + instantiated `EmployeeChecklistTask` rows, handling seat-limit (`waiting_for_seat`) and position-approval (`waiting_for_position_approval`) branches. Bulk onboarding must produce the exact same outcomes per row, not a parallel simplified version.
- **Background workers in this codebase never dispatch through MediatR, and cannot use `ICurrentUser` as-is.** `CurrentUserService : ICurrentUser` (`src/ONEVO.Infrastructure/Identity/CurrentUser/CurrentUserService.cs`) reads `TenantId`/`UserId`/`Permissions` from `IHttpContextAccessor.HttpContext.User` claims — outside an HTTP request, `HttpContext` is null and every one of those resolves to `Guid.Empty`/empty. `OutboxProcessor` (`src/ONEVO.Infrastructure/Services/SharedPlatform/Outbox/OutboxProcessor.cs`) and `ActivityDailySummaryJob`/`AgentCommandExpiryJob` all follow the same shape instead: a `BackgroundService` with `PeriodicTimer`/`Task.Delay` polling, `_services.CreateAsyncScope()` per batch, resolving repositories/services directly from that scope — never `IMediator.Send`. `EmployeeOnboardingInviteEmailOutboxHandler` avoids the tenant-context problem entirely by carrying every needed value (`TenantId`, `EmployeeId`, etc.) in its own payload and never issuing a tenant-scoped read.
- **Tenant context *is* explicitly settable per-scope**: `TenantContextAccessor : IWritableTenantContext` (`src/ONEVO.Infrastructure/Identity/Tenancy/TenantContextAccessor.cs`) has `Resolve(TenantRegistryEntry tenant)`, and `TenantRlsInterceptor` (`src/ONEVO.Infrastructure/Persistence/Interceptors/TenantRlsInterceptor.cs`) reads that per-DbContext-scope instance to set the `app.current_tenant_id`/`app.tenant_context_mode` Postgres session variables on every connection open — so a background worker's own `IServiceScope` can call `IWritableTenantContext.Resolve(...)` and get correct RLS behavior for that scope's queries. This is the mechanism that makes background reuse of tenant-scoped repositories viable at all.
- **`SaveOnboardingDraftCommandHandler`/`FinalizeOnboardingDraftCommandHandler` read `_currentUser.TenantId`/`_currentUser.UserId` at every call site** (11 and 9 usages respectively) rather than taking them as parameters — this is the actual blocker to reuse, not tenant RLS. §5 below is the resolution.
- **`IFileStorageService`** (`Application/Features/Storage/File/ServiceInterfaces/IFileStorageService.cs`) is the mandatory single entry point for anything that needs Cloudflare R2 (quota-reserved uploads, signed URLs). It is *not* used here — see §4.3.
- **`ICurrentUser.HasPermission`** and the `[RequirePermission("employees:write")]`/`[RequirePermission("employees:read")]` attributes already gate every `OnboardingDraftsController` action (`GetById`/`List` → `employees:read`; create/update/finalize → `employees:write`). No new permission code is introduced; bulk onboarding reuses `employees:write` and `employees:read` at the same granularity, matching how checklist templates and `change-position` already do.
- **`[Idempotent]`** is an existing action filter already applied to `ChecklistTemplatesController.Create` and is required here on bulk finalize (re-POSTing the same batch-finalize request must not double-invite).
- **Module gating**: `ModuleCatalogSeeder.cs` seeds `core_hr.onboarding` (`Included = true`) under the `core_hr` module — bulk onboarding is reachable under that same existing module, no catalog change needed.

## 4. Data model

New tables, both `ITenantOwnedEntity`, explicit RLS policies (this repo has migrations literally named `AddMissingRlsPolicies`/`AddFileStorageRlsPolicies` — easy to omit, must not be), tenant-aware indexes on `(tenant_id, batch_id)` and `(tenant_id, status)`. Added to `phase1-table-inventory.md` in the same pass as the migration (see plan).

### 4.1 `bulk_onboarding_batches`

| Column | Type | Notes |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | FK -> tenants |
| `legal_entity_id` | uuid | FK -> legal_entities; batch-level default |
| `default_employment_type` | varchar(30) | Batch-level default, CSV column can override per row |
| `default_work_mode_id` | int | FK -> work_modes; batch-level default, CSV column can override per row |
| `default_checklist_template_id` | uuid | Nullable, FK -> checklist_templates; batch-level default, CSV column can override per row |
| `column_mapping` | jsonb | `{ "firstName": "First Name", "workEmail": "Email", ... }` — ephemeral, this-batch-only; never persisted as a reusable template |
| `original_file_name` | varchar(255) | Display only |
| `status` | varchar(30) | `mapping_pending`, `validated`, `draft_creation_pending`, `drafts_created`, `finalize_pending`, `finalize_completed` |
| `total_rows` | int | |
| `valid_rows` | int | Nullable until validated |
| `invalid_rows` | int | Nullable until validated |
| `created_by_user_id` | uuid | FK -> users; the acting user the background worker impersonates for every row (§5) |
| `created_at` | timestamptz | |
| `updated_at` | timestamptz | |
| `completed_at` | timestamptz | Nullable; set when finalize_completed |

**Row cap:** 200 rows per file, enforced at upload time (`total_rows > 200` rejected before any row is persisted).

### 4.2 `bulk_onboarding_batch_rows`

| Column | Type | Notes |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | |
| `batch_id` | uuid | FK -> bulk_onboarding_batches |
| `row_number` | int | 1-based, matches the CSV row for error reporting |
| `raw_data` | jsonb | Original cell values keyed by detected CSV header, untouched by mapping |
| `resolved_department_id` | uuid | Nullable; set at validation time by looking up the mapped column's value against existing departments |
| `resolved_position_id` | uuid | Nullable; same. Reporting manager is not a separate field anywhere in this schema (see §2) — it is implied entirely by this position's own configured `ReportsToPositionId`, exactly as it already is for single-employee onboarding. |
| `resolved_template_id` | uuid | Nullable; row override of `default_checklist_template_id`, resolved by name against `IChecklistTemplateRepository.ListOnboardingMatchesAsync(tenantId, legalEntityId, resolved_department_id, resolved_position_id)` (existing method, same one the single-employee wizard's template-picker step uses) |
| `status` | varchar(30) | `pending_mapping`, `valid`, `invalid`, `draft_created`, `draft_failed`, `finalized`, `waiting_for_seat`, `waiting_for_position_approval`, `finalize_failed` |
| `error_message` | text | Nullable; set on `invalid`/`draft_failed`/`finalize_failed` |
| `onboarding_draft_id` | uuid | Nullable FK -> onboarding_drafts; set once the draft is created |
| `created_at` | timestamptz | |
| `updated_at` | timestamptz | |

**Unique:** `(tenant_id, batch_id, row_number)`.

Bulk-created drafts are ordinary `onboarding_drafts` rows: `last_saved_step = 'review_and_submit'` (they carry complete data, not a partial wizard state), `started_by_id = created_by_user_id`. They therefore also appear in the existing **My Drafts** list (`ListOnboardingDraftsQuery`, filtered by `started_by_id`) — this is intentional reuse, not a bug. The batch review screen is a second view over the same underlying drafts, joined through `bulk_onboarding_batch_rows.onboarding_draft_id`.

### 4.3 Why raw CSV rows are stored in Postgres, not R2 via `IFileStorageService`

Routing the uploaded file through `IFileStorageService.UploadAsync` would consume tenant storage quota for a file nobody needs after parsing, and the architecture doc's file-security rules require uploaded files to move through `pending_scan → available` before they're safe to read back — but this worker needs to read the file back immediately to parse it, which is exactly the state that section says not to trust yet. Storing each row's raw cell values directly in `bulk_onboarding_batch_rows.raw_data` (jsonb) on upload sidesteps both problems and removes the round-trip of uploading, then immediately re-downloading, the same bytes. No file record, no reservation, no signed URL — a 200-row CSV's cell data is trivial by comparison to file storage's actual concerns (documents, photos, exports).

## 5. Service refactor: extract tenant/user-parameterized write logic

Per §3's finding, `SaveOnboardingDraftCommandHandler` and `FinalizeOnboardingDraftCommandHandler` cannot be called via `IMediator.Send` from a background scope. The fix is mechanical, not a redesign: move each handler's `Handle` body into a new Application-layer service whose methods take `tenantId`/`actingUserId` as explicit parameters instead of reading `_currentUser`, and make the existing MediatR handler a thin adapter over it. Behavior for the existing single-employee HTTP flow does not change — it is the same code, called the same way, just via an injected service instead of inline in the handler.

- **`IOnboardingDraftWriteService`** (`Application/Features/CoreHr/OnboardingDraft/Services/`):
  - `Task<Result<OnboardingDraftResponse>> SaveAsync(Guid tenantId, Guid actingUserId, SaveOnboardingDraftCommand request, CancellationToken ct)` — the entire current `SaveOnboardingDraftCommandHandler.Handle` body, with `_currentUser.TenantId`/`_currentUser.UserId` replaced by the two parameters. Kept general (handles both the create and update-existing branches) rather than trimmed to bulk's create-only need, so it stays a true drop-in and single-employee behavior can't silently drift from bulk behavior.
  - `Task<Result<FinalizeOnboardingDraftResponse>> FinalizeAsync(Guid tenantId, Guid actingUserId, Guid draftId, CancellationToken ct)` — same treatment for the entire current `FinalizeOnboardingDraftCommandHandler.Handle` body (all ~450 lines: field validation, position-approval branch, seat-decision branch, user/employee/invitation/checklist-task creation, outbox enqueue).
  - `SaveOnboardingDraftCommandHandler.Handle` becomes `return _writeService.SaveAsync(_currentUser.TenantId, _currentUser.UserId, request, ct);`. `FinalizeOnboardingDraftCommandHandler.Handle` becomes the equivalent one-line delegation. Both existing handler classes keep their `[RequirePermission]`-gated controller entry points unchanged.
  - The bulk background worker (§6.1) resolves `IWritableTenantContext.Resolve(...)` for its scope, then calls `_writeService.SaveAsync(batch.TenantId, batch.CreatedByUserId, ..., ct)` / `FinalizeAsync(...)` per row, exactly as the HTTP path does, just with explicit values instead of `ICurrentUser`.

This is the only non-additive change to existing code this feature requires. Everything else (new tables, new controller, new worker) is new surface area.

## 6. API surface (`ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs`, `ONEVO.Api/Contracts/CoreHr/BulkOnboarding/`)

### 6.1 `BulkOnboardingBatchProcessor` (background worker)

Same poll-loop shape as `OutboxProcessor`/`ActivityDailySummaryJob`: a `BackgroundService` with `PeriodicTimer`, no MediatR. Each tick: `_services.CreateAsyncScope()`, resolve a batch in `draft_creation_pending` or `finalize_pending` (oldest first, one batch per tick to keep row-ordering simple), resolve `IWritableTenantContext` from that same scope and call `.Resolve(...)` with the batch's tenant looked up via the existing tenant-registry repository, then loop the batch's rows calling `IOnboardingDraftWriteService.SaveAsync`/`FinalizeAsync` (§5) with `batch.TenantId`/`batch.CreatedByUserId` explicitly — never `ICurrentUser`. Each row's result is stamped individually (`draft_created`/`draft_failed` or the four finalize outcomes) so one row's failure doesn't lose the others' progress; the batch's own `status` flips to the terminal value (`drafts_created`/`finalize_completed`) only after every row in it has been attempted. Registered as a hosted service in `DependencyInjection.cs` alongside the existing jobs.

`OnboardingDraftsController`'s actual route is `[Route("api/v1/onboarding/drafts")]` (verified — not a `/people/...` prefix). Bulk onboarding follows that same `api/v1/onboarding/{subfeature}` convention: all routes under `/api/v1/onboarding/bulk-batches`, `RequirePermission("employees:write")` for every mutating action, `employees:read` for read-only ones.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/onboarding/bulk-batches` | Multipart upload (CSV + batch-level defaults). Parses header + rows, enforces 200-row cap, persists `bulk_onboarding_batches` (`status: mapping_pending`) + `bulk_onboarding_batch_rows` (`raw_data`, `status: pending_mapping`). Returns batch id, detected column headers, an auto-suggested mapping (name-similarity match against known field labels), and row count. Synchronous — this is a header-row read plus N trivial inserts, not the NFR's "import." |
| `POST` | `/api/v1/onboarding/bulk-batches/{id}/preview` | Submits/edits the column mapping. Applies it to row 1 only, resolves department/position/template *names* (not just IDs) for display, returns the resolved preview. Synchronous, read-only, no persistence beyond the batch's stored `column_mapping`. Re-callable any number of times before validating. |
| `POST` | `/api/v1/onboarding/bulk-batches/{id}/validate` | Applies the confirmed mapping to every row, runs full per-row validation (required fields, legal-entity/department/position/template existence, in-file duplicate email, duplicate-in-tenant email/employee-number), updates every row's `status`/`error_message`/`resolved_*` columns, sets batch `status: validated` + `valid_rows`/`invalid_rows`. Synchronous — read-heavy but bounded to 200 rows, needed for immediate interactive feedback. |
| `POST` | `/api/v1/onboarding/bulk-batches/{id}/create-drafts` | Sets batch `status: draft_creation_pending`. `BulkOnboardingBatchProcessor` (§6.1) picks it up, calls `IOnboardingDraftWriteService.SaveAsync` per valid row, stamps each row `draft_created`/`draft_failed` + `onboarding_draft_id`, sets batch `status: drafts_created` on completion. Fire-and-poll: returns immediately with the batch in `draft_creation_pending`. |
| `GET` | `/api/v1/onboarding/bulk-batches/{id}` | Batch status + row-level results, for polling and for the review screen. `employees:read`. |
| `POST` | `/api/v1/onboarding/bulk-batches/{id}/finalize` | Body: selected `onboardingDraftId` list. `[Idempotent]`. Sets batch `status: finalize_pending`; worker calls `IOnboardingDraftWriteService.FinalizeAsync` per selected draft, stamps each row's outcome (`finalized`/`waiting_for_seat`/`waiting_for_position_approval`/`finalize_failed`), sets batch `status: finalize_completed`. Response/poll result reports the same four-way aggregate `FinalizeOnboardingDraftResponse` already returns per draft today — bulk gets this for free by reusing the shared service, not by reimplementing seat/approval logic. |

## 7. Contracts-folder architecture-doc fix

Done in this session, ahead of implementation (`ONEVO_Backend_Architecture_Document.md`): §2.1.1 now lists `Contracts/{Feature}/{SubFeature}/` in the canonical folder tree with a purpose row (wire-level `*Request`/`*ViewModel` records a controller binds and maps into a command/query — verified against `ChecklistTemplatesController` mapping `CreateChecklistTemplateRequest` → `CreateChecklistTemplateCommand`, and `PagedResultViewModel`/`AuthViewModelMapper`/`ObjectiveViewModelMapper` already living there). §2.1.2's `DTOs/Requests|Responses` row is reconciled rather than contradicted: `Application/DTOs` is the use-case input/output shape, `Api/Contracts` is the HTTP wire shape, and a controller maps between them. §3.8's build checklist gained a step for adding `Contracts` records. This feature's own controller follows the corrected doc exactly.

## 8. Testing

- **Unit** (`ONEVO.Tests.Unit`): row-validation/mapping-resolution logic as a pure class in `Helpers/` (no EF) — auto-suggest mapping accuracy, in-file duplicate-email detection, row-cap enforcement, missing-department/position/manager error messages, required-field checks. `IOnboardingDraftWriteService.SaveAsync`/`FinalizeAsync` re-tested with explicit tenant/user params (existing `SaveOnboardingDraftCommandHandlerTests`/equivalent finalize tests, if any, should still pass unmodified against the extracted service — confirms the refactor preserved behavior). `BulkOnboardingBatchProcessor`'s poll-and-scope logic, mocking the write service.
- **Integration** (`ONEVO.Tests.Integration`, Testcontainers): 401/403 on missing `employees:write`; tenant isolation — tenant A cannot read/act on tenant B's batch or rows; end-to-end partial success (mixed valid/invalid rows in one file); seat-limit path during bulk finalize (plan's subscription fixture has `max_employees` low enough to force `waiting_for_seat` on some rows); position-approval path during bulk finalize; finalize idempotency (re-POST same batch does not double-create employees/invitations).

## 9. Open items for the plan to resolve

- Exact CSV column header labels and the auto-suggest matching rule (exact/case-insensitive/fuzzy) — implementation detail, not a design blocker.
- Whether `BulkOnboardingBatchProcessor` is one `BackgroundService` polling all three pending statuses (`draft_creation_pending`, `finalize_pending`) or the poll loop is unified with a `job_type` discriminator — either is fine; plan should pick based on how `OutboxProcessor`'s `BatchSize`/poll-interval constants are configured for comparison.
