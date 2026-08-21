# Legal Entity / Company General Settings — Default Break Duration (Backend)

## Summary

Added a nullable, company-level default break duration (`break_duration_minutes`) to
`legal_entities`, alongside the existing `WorkStartTime`/`WorkEndTime` default work-hours
fields on the General Settings screen. Verified via `git status`, reading the current
LegalEntity entity/config/migrations, and grepping for `BreakDuration`/`WorkStartTime` before
starting: the field did not already exist anywhere in the backend. This work is scoped strictly
to General Settings GET/PUT — Clock-in Policy (a separate, already in-progress feature in this
branch under `src/ONEVO.Application/Features/TimeAttendance/`) was not touched.

## Schema / API contract

**Column:** `legal_entities.break_duration_minutes integer NULL`
**Check constraint:** `ck_legal_entities_break_duration_minutes`: `break_duration_minutes IS NULL OR break_duration_minutes >= 0`
**Migration:** `20260821092355_AddLegalEntityBreakDurationMinutes` (additive only — one `AddColumn` + one `AddCheckConstraint`; applied to the local dev database with `dotnet ef database update`).

GET/PUT `api/v1/org/legal-entities/{id}/general-settings` — response and request both gained a
trailing `breakDurationMinutes: number | null` field, in the same position as the task's example:

```json
{
  "workStartTime": "09:00",
  "workEndTime": "17:30",
  "breakDurationMinutes": 60
}
```

Unset:

```json
{
  "workStartTime": null,
  "workEndTime": null,
  "breakDurationMinutes": null
}
```

`CreateLegalEntityCommand`/`CreateLegalEntityRequest` (Create Company) were deliberately **not**
touched — they never accepted `workStartTime`/`workEndTime` either, so break duration follows
the same precedent and is General-Settings-only.

## Files changed

- `src/ONEVO.Domain/Features/OrgStructure/LegalEntity/Entities/LegalEntity.cs` — `int? BreakDurationMinutes`.
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/LegalEntity/LegalEntityConfiguration.cs` — column mapping + check constraint.
- `src/ONEVO.Infrastructure/Migrations/20260821092355_AddLegalEntityBreakDurationMinutes.cs` (+ `.Designer.cs`) — new migration, generated with `dotnet ef migrations add` and applied locally.
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/DTOs/Responses/LegalEntityGeneralSettingsResponse.cs` — response DTO field.
- `src/ONEVO.Api/Contracts/OrgStructure/LegalEntities/UpdateLegalEntityGeneralSettingsRequest.cs` — request contract field.
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/UpdateLegalEntityGeneralSettings/UpdateLegalEntityGeneralSettingsCommand.cs` — command field.
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/UpdateLegalEntityGeneralSettings/UpdateLegalEntityGeneralSettingsCommandHandler.cs` — sets `entity.BreakDurationMinutes = request.BreakDurationMinutes`, independent of the work-time assignment.
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/UpdateLegalEntityGeneralSettings/UpdateLegalEntityGeneralSettingsCommandValidator.cs` — `GreaterThanOrEqualTo(0)` when not null; no upper bound.
- `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Mappers/LegalEntityMapper.cs` — maps the field into the response.
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs` — passes `request.BreakDurationMinutes` into the command.
- Tests (see below).

## Validation rules

- `null` = not configured (allowed, independent of work start/end time).
- Integer, `>= 0` when provided.
- Negative values rejected with a 400 (`"Break duration must not be negative."`).
- No upper bound: no existing backend validation pattern in this codebase establishes one for a
  duration-style field (checked `GreaterThanOrEqualTo(0)` usages across `ONEVO.Application` —
  none carry a paired upper limit for a duration/minutes field), so none was invented.
- Independent of `WorkStartTime`/`WorkEndTime`: can be set, changed, or left null regardless of
  whether work hours are configured. The existing work-time pairing/ordering rule
  (`ck_legal_entities_work_time_pair`, same-day only) is untouched.

## Tests run

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --configuration Release
  → Build succeeded, 0 errors (2 pre-existing warnings unrelated to this change).

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release --filter "LegalEntity|GeneralSettings"
  → Passed! Failed: 0, Passed: 220, Skipped: 0, Total: 220

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release
  → Failed: 1, Passed: 625, Total: 626
  → The 1 failure (TenantIsolationArchitectureTests.IgnoreQueryFilters_UsageIsExplicitlyAllowlisted,
    flagging EfEmployeeRepository.cs) is PRE-EXISTING and unrelated: EfEmployeeRepository.cs was
    already modified on this branch before this task started (confirmed via `git status` at the
    very start of the session, before any edit in this task). Not touched by this change.

git diff --check
  → No errors (only pre-existing LF→CRLF line-ending warnings on Windows checkout, not actual
    whitespace/conflict-marker problems).

dotnet build tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --configuration Release
  → Not part of the task's specified verification commands, but run anyway to confirm the new
    LegalEntitiesIntegrationTests.cs test methods actually compile (see "Skipped checks" below
    for why they weren't executed). Zero errors attributable to that file (one pre-existing
    CS0618 warning shared by every file in the project that builds a PostgreSqlBuilder). The
    build as a whole fails with 2 CS1503 errors, both in pre-existing, unrelated
    BulkOnboarding WIP test files already broken before this task started.
```

New/updated tests, by file:

- `UpdateLegalEntityGeneralSettingsCommandValidatorTests.cs`: null/zero/positive accepted,
  negative rejected, independence from work-time fields.
- `UpdateLegalEntityGeneralSettingsCommandHandlerTests.cs`: persists a value, persists null
  (clearing a prior value), independence from work-time fields.
- `GetLegalEntityGeneralSettingsQueryHandlerTests.cs`: GET returns the configured value and
  returns null when unset.
- `LegalEntitiesControllerTests.cs`: controller forwards `breakDurationMinutes` from the request
  into the command (positional-constructor call sites updated for the new DTO field).
- `LegalEntityGeneralSettingsArchitectureTests.cs`: `LegalEntity_HasExactlyTheInventoryColumns`
  updated to include `BreakDurationMinutes`; new
  `Model_LegalEntities_BreakDurationMinutes_MapsToNullableIntColumn` guards the nullable-int /
  column-name mapping the same way the existing work-time test guards `TimeOnly?`.
- `LegalEntitiesIntegrationTests.cs` (HTTP/Testcontainers-backed): GET returns the value, PUT
  saves a valid value, PUT accepts/persists null, PUT rejects a negative value (400), PUT rejects
  a decimal value (400 — see note below), and PUT accepts a break duration with both work-time
  fields left null (independence, asserted at the HTTP boundary). **Written but not executed** in
  this session — outside the verification commands the task specified (`ONEVO.Tests.Unit` and
  `ONEVO.Tests.Architecture` only); this project needs Docker/Testcontainers or `ONEVO_TEST_DB`
  and was not run here. It **is** confirmed to compile: `dotnet build
  tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --configuration Release` shows
  zero errors for `LegalEntitiesIntegrationTests.cs` (one pre-existing `CS0618` obsolete-API
  warning only, already present before this task, shared by every other file in this test
  project that constructs a `PostgreSqlBuilder`). That build does report 2 `CS1503` errors, but
  both are in `BulkOnboardingValidateTests.cs`/`BulkOnboardingCreateDraftsTests.cs`
  (`IBulkOnboardingValidationRunner` signature mismatch) — pre-existing, unrelated WIP on this
  branch (those files were already modified before this task started, per the session's initial
  `git status`), not something this task touched or caused. Existing
  `Update_ValidWorkStartAndEndTime_Returns200_AndPersists` / `Update_OnlyWorkStartTimeProvided_Returns400`
  / etc. work-time tests in this same file are unmodified aside from the shared `UpdateBody`
  helper gaining an optional `breakDurationMinutes` parameter (default `null`, so all existing
  calls are unaffected).

**Decimal handling:** `BreakDurationMinutes` is `int?`, so a JSON body containing
`"breakDurationMinutes": 7.5` fails at ASP.NET Core's JSON model-binding step, before
`UpdateLegalEntityGeneralSettingsCommandValidator` (FluentValidation) ever runs — `[ApiController]`
converts that binding failure into an automatic 400. `Update_DecimalBreakDurationMinutes_Returns400`
asserts this directly (constructing the raw JSON payload by hand, since the typed `UpdateBody` test
helper can't express a non-integer value). No separate FluentValidation rule for
non-integer/decimal values was needed or added.

Existing `WorkStartTime`/`WorkEndTime` unit tests are unmodified and still pass (see the 220/220
unit-test result above, which includes them). Migration/model snapshot is clean — the new
migration and `ApplicationDbContextModelSnapshot.cs` were both generated by `dotnet ef migrations
add` from the final entity/configuration state, and `dotnet ef database update` applied cleanly
against the local Postgres dev database.

## Skipped checks

- `ONEVO.Tests.Integration` (the new HTTP-level tests above) was not **executed** — not in the
  task's specified verification command list, and this project requires either Docker
  (Testcontainers) or a configured `ONEVO_TEST_DB` local server, neither of which was set up in
  this session. Compilation was verified instead (see "Tests run" above) — the new test methods
  build cleanly. The tests follow the file's existing pattern precisely (same
  `UpdateBody`/`CreateCompanyAsync` helpers as the `WorkStartTime`/`WorkEndTime` HTTP tests they
  sit next to).
- Manual/browser API smoke test not performed from this side; frontend report covers the UI-side
  verification.

## Remaining risks

- The break duration field carries no upper bound by design (per the task's instruction not to
  invent one). If product later wants a sane ceiling (e.g. reject a 10,000-minute break), that's
  a validator-only follow-up — no schema change needed.
- The new integration tests are unverified by an actual test run in this session (see Skipped
  checks). They were written to mirror the file's own established conventions closely, but they
  should be run (`dotnet test tests/ONEVO.Tests.Integration/...` with Docker or `ONEVO_TEST_DB`
  set) before being relied on as a merge gate.
- A locally running `ONEVO.Api` dev server (PID from an earlier session, started 12:36 PM) was
  holding a file lock that blocked `dotnet build`/`dotnet ef`. It was stopped to unblock this
  work and restarted afterward via `dotnet run --project src/ONEVO.Api --launch-profile https`
  (port 7229, matching `launchSettings.json`). If that dev server had unsaved in-memory state,
  it was lost on restart — nothing on disk was affected.
