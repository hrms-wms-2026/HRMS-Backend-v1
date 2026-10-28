# Position Capacity-Driven Type & Code Length Backend Correction

## Summary

Removed `positionType` as a user-selectable field from the Position create/update contracts;
backend now derives `unique`/`pooled` from `maxOccupancy`. Reduced position code max length from
40 to 5 characters (DB column, validators, and a guarded migration). Updated all affected tests.

## Files changed

**Request/command contracts (positionType removed):**
- `src/ONEVO.Api/Contracts/OrgStructure/Positions/CreatePositionRequest.cs`
- `src/ONEVO.Api/Contracts/OrgStructure/Positions/UpdatePositionRequest.cs`
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition/CreatePositionCommand.cs`
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommand.cs`
- `src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs` (Create/Update no longer forward `positionType`)

**Validators (positionType rules removed, code max length 5):**
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition/CreatePositionCommandValidator.cs`
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommandValidator.cs`

**Handlers (derive PositionType from MaxOccupancy):**
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition/CreatePositionCommandHandler.cs`
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommandHandler.cs`

**Persistence:**
- `src/ONEVO.Infrastructure/Persistence/Configurations/OrgStructure/Position/PositionConfiguration.cs` (`Code` max length 40 → 5)
- `src/ONEVO.Infrastructure/Migrations/20260812102930_AddPositionCodeMaxLength.cs` (new, guarded)
- `src/ONEVO.Infrastructure/Migrations/20260812102930_AddPositionCodeMaxLength.Designer.cs` (new, generated)
- `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` (`Code` max length synced to 5)

**Not touched (by design):** `Position.cs` entity, `PositionResponse`/`PositionListItemResponse`/`PositionTreeNodeResponse`,
`PositionMapper`, `EfPositionRepository`. Responses still expose `positionType` for display, per the requirement.

**Tests updated:**
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/CreatePositionCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/UpdatePositionCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Unit/Controllers/Tenant/OrgStructure/PositionsControllerTests.cs`
- `tests/ONEVO.Tests.Integration/OrgStructure/Position/PositionsIntegrationTests.cs`

**Out of scope, left untouched:** `POSITION_TEMPLATE_PACKS_BACKEND_REPORT.md`, `PositionTemplatePacksController.cs`,
the `PositionTemplatePacks` feature folder, `PositionTemplatePackSeeder.cs`, and their tests — these are
pre-existing **uncommitted, in-progress work from a different feature** already present in the working
tree when this task started (confirmed via `git status --short --branch` in the first step). They are
unrelated to this task and were not modified.

## Request contract changes

`CreatePositionRequest` / `UpdatePositionRequest` / `CreatePositionCommand` / `UpdatePositionCommand` no
longer have a `PositionType` property. The wire shape is now:

```
CreatePositionRequest(DepartmentId, Name, Code, MaxOccupancy, ReportsToPositionId)
UpdatePositionRequest(DepartmentId, Name, Code, MaxOccupancy, ReportsToPositionId)
```

Since `System.Text.Json` model binding silently drops unknown JSON fields, a client that still sends
`positionType` in the request body will not error — the field is simply ignored. (One integration test,
`Create_CodeLongerThanFiveCharacters_Returns400`, and the removal of the old
`Create_InvalidPositionType_Returns400`/`Create_UniqueTypeWithMaxOccupancyNotOne_Returns400` tests,
document this: an invalid/extra `positionType` is no longer a validation failure.)

## Derived position type rules

Both handlers now set:

```csharp
PositionType = request.MaxOccupancy == 1 ? PositionEntity.TypeUnique : PositionEntity.TypePooled
```

- `maxOccupancy == 1` → `"unique"`
- `maxOccupancy > 1` → `"pooled"`
- `maxOccupancy < 1` is rejected by validation before the handler runs (see below), so the ternary
  never sees a non-positive value.

Validators were simplified to a single unconditional rule per command:

```csharp
RuleFor(x => x.MaxOccupancy).GreaterThanOrEqualTo(1).WithMessage("Capacity must be at least 1.");
```

replacing the old pair of `.When(PositionType == ...)` conditional rules that depended on the
now-removed field.

## Code max-length rules

- Regex tightened from `^[A-Za-z0-9_-]{1,40}$` to `^[A-Za-z0-9_-]{1,5}$` (same allowed character set,
  just the length cap changed, per instructions).
- `MaximumLength(40)` → `MaximumLength(5)` in both validators.
- `NotEmpty()` unchanged — **code remains required**, not optional, in both `CreatePositionCommand` and
  `UpdatePositionCommand` today. The prompt's "whitespace-only becomes null if code is optional" and
  the corresponding test are conditional on optionality that does not exist in the current contract —
  nothing elsewhere in this task asked for that change, so I did not invent an optional-code code path.
  This is called out explicitly rather than silently skipped.
- **Correction to the stated premise:** the task description says code was "currently" capped at 20.
  It was not — the DB column, both validators, and the regex were all `40`. `20` is the max length of
  the unrelated `position_type` column. The target (5) was applied correctly regardless; this note is
  just so the discrepancy isn't silently repeated as fact.

## Migration behavior

`20260812102930_AddPositionCodeMaxLength` alters `positions.code` from `character varying(40)` to
`character varying(5)`. Before the `AlterColumn`, it runs a guard:

```sql
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM positions WHERE code IS NOT NULL AND length(code) > 5) THEN
        RAISE EXCEPTION 'Cannot shorten positions.code to 5 characters: existing row(s) exceed that length. ...';
    END IF;
END $$;
```

This **fails loudly instead of truncating**. Truncation was rejected deliberately: `positions.code`
participates in the unique partial index `ix_positions_tenant_id_legal_entity_id_code`
(`(tenant_id, legal_entity_id, code)`, filtered `code IS NOT NULL AND legal_entity_id IS NOT NULL`).
Silently truncating two previously-distinct codes (e.g. `ABCDEF` and `ABCDEG`) to the same 5-character
prefix would collide inside that index and fail the migration anyway, but with a confusing raw
Postgres unique-violation instead of an actionable message — so the guard front-loads the failure with
a clear cause.

Migration generation notes: `dotnet ef migrations add` was run with `ONEVO.Infrastructure` as both
`--project` and `--startup-project` (its own `ApplicationDbContextFactory` design-time factory), to
avoid touching `ONEVO.Api`'s build output. The generated migration was inspected before editing and
contained **only** the `positions.code` alter — no drift from the pre-existing uncommitted
`DependencyInjection.cs`/`EfConfigurationTemplateRepository.cs` changes was folded in. Confirmed after
the fact with `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the
model since the last migration."

## Template/seed updates

**No changes needed.** `PositionTemplatePackSeeder.cs` (uncommitted, in-progress, unrelated feature —
see Files changed) defines `SeedPositionPayload` with fields `position_key`, `position_name`,
`department_name`, `reports_to_position_key`, `linked_role_template_id` — **there is no `code` field at
all**. Strings like `"chief-executive-officer"` are slug-style keys, not position codes; nothing in
that seeder generates or stores a `positions.code` value. The task's instruction was conditional
("update... *if* it creates/generated codes longer than 5") and the condition is false today.

Forward note for whenever "apply pack" ships (turning a `SeedPositionPayload` into an actual
`Position` row): that future code will need to either derive/generate a ≤5-character code or require
the tenant to supply one at apply-time, since the packs currently carry none.

## Reporting manager — findings and next required backend work

Inspected `Employee`, `PositionAssignment`, and `EmployeeHierarchyClosure`:

- `Employee` (`src/ONEVO.Domain/Features/CoreHr/Employee/Entities/Employee.cs`) has no manager field.
- `PositionAssignment` (`src/ONEVO.Domain/Features/CoreHr/PositionAssignment/Entities/PositionAssignment.cs`)
  has `EmployeeId`, `PositionId`, `AssignmentKind`, `EffectiveFrom/To`, `AssignmentStatus` — no manager field.
- `EmployeeHierarchyClosure` (`src/ONEVO.Domain/Features/CoreHr/EmployeeHierarchyClosure/Entities/EmployeeHierarchyClosure.cs`)
  is a materialized closure table whose own doc comment states it is "derived from
  `positions.reports_to_position_id` and active `PrimaryEmployment` `position_assignments`" — i.e. the
  reporting manager is **currently inferred purely structurally** (position hierarchy + who's assigned
  to which position), exactly the pattern the product direction says to move away from.

**Conclusion:** the schema has no `reportingManagerEmployeeId` (or equivalent) anywhere today. This
confirms the task's stated premise. Per the explicit instruction, this was **not implemented** in this
prompt — position hierarchy (`reports_to_position_id`) stays structural-only, and no manager field was
added to `Position`.

**Recommended next backend prompt:**
1. Add a nullable `reporting_manager_employee_id` (FK → `employees.id`, `Restrict`) to
   `position_assignments`, not to `Position` — it's a property of "this employee's assignment to this
   position for this time range," which fits `PositionAssignment`'s existing per-assignment,
   time-scoped shape (`EffectiveFrom`/`EffectiveTo`) exactly, and lets a reporting line be overridden
   per-assignment without touching the structural position hierarchy.
2. Add validation: the assigned reporting-manager employee must have an active `PrimaryEmployment`
   assignment of their own (can't report to someone who isn't actively employed anywhere).
3. Update the `EmployeeHierarchyClosure` rebuild service to prefer the explicit
   `reporting_manager_employee_id` when present on the active `PrimaryEmployment` assignment, falling
   back to the current structural derivation (`reports_to_position_id` → position holder) only when it's
   null — so existing structural-only tenants keep working unchanged.
4. Expose `reportingManagerEmployeeId` on whatever employee onboarding/assignment endpoint currently
   creates/updates `PositionAssignment` rows, sourced from a picklist of active employees (not
   positions).

Do not add this field to `Position` — that would conflate a structural org-chart concept (which
position reports to which) with a personnel concept (which specific person is someone's manager right
now), and would break the moment two different people hold the same pooled position with different
actual managers.

## Tests run

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj                                   → Build succeeded, 0 errors
dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj                   → Build succeeded, 0 errors
dotnet build tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj   → Build succeeded, 0 errors
dotnet build tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj     → Build succeeded, 0 errors

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Position"
  → 170/170 passed

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj (full suite)
  → 1945/1945 passed

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --filter "FullyQualifiedName~Position"
  → 117/120 passed (3 pre-existing failures, unrelated — see below)

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj (full suite)
  → 561/564 passed (same 3 pre-existing failures)

dotnet ef migrations has-pending-model-changes → "No changes have been made to the model since the last migration."
git diff --check → no reported errors (only benign LF→CRLF autocrlf warnings)
```

New/updated Position unit tests of note:
- `Handle_DerivesUniquePositionType_WhenMaxOccupancyIsOne` / `..._GreaterThanOne` (create + update handlers)
- `Validator_RejectsCodeLongerThanFiveCharacters` / `Validator_AllowsCodeAtFiveCharacters` (create + update)
- `Validator_RejectsMaxOccupancyBelowOne` (`[Theory]` 0 and -1) (create + update)
- `Validator_AllowsMaxOccupancyGreaterThanOne` (create)
- Removed `Validator_RejectsInvalidPositionType` / `Validator_RejectsSingleOccupancyCapacityOtherThanOne`
  (premise no longer exists — `PositionType` isn't an input anymore)

Integration test changes: every position `code` literal in `PositionsIntegrationTests.cs` (~65
occurrences) was shortened to ≤5 characters, and `positionType` was removed from every request payload.
Four cases needed care beyond a mechanical rename, preserved intentionally:
- `Create_DuplicateCodeCaseInsensitiveInSameLegalEntity_Returns409` — `DUPCD`/`dupcd` still collide case-insensitively.
- `Create_SameCodeInDifferentLegalEntity_IsAllowed` — `SHRDP` stays identical across both legal entities.
- `List_Search_FindsByCode` — redesigned as a 5-char-safe prefix search (`SRCHM` vs `OTHRC`, search=`SRCH`).
- `List_SortByCode_Descending_Orders` — `AAAAA`/`ZZZZZ` preserves ordering.

Two tests were deleted (obsolete premise, not just renamed):
- `Create_InvalidPositionType_Returns400` — sending `positionType` now does nothing; it's silently ignored, not a 400.
- `Create_UniqueTypeWithMaxOccupancyNotOne_Returns400` — "unique + maxOccupancy 2" is no longer an
  invalid combination; it's just a valid pooled position now.

One test was renamed and repurposed:
- `Create_PooledTypeWithMaxOccupancyLessThanOne_Returns400` → `Create_MaxOccupancyZero_Returns400`
  (drops `positionType` from the payload; the 400 now comes from the unconditional `>= 1` rule).

One new integration test was added: `Create_CodeLongerThanFiveCharacters_Returns400`.

## Skipped checks

- **Full `PositionsIntegrationTests.cs` execution was not run.** It requires Docker/Testcontainers
  (real Postgres) which isn't available in this environment. It was verified via build (compiles
  clean) and manual review of every changed assertion/payload, but not via an actual test run. This is
  a real gap, not an oversight — flagging it explicitly as requested.
- Postman collection / API docs (if any exist describing `positionType` as a request field) were not
  searched for or updated — out of the explicit scope of this prompt.

## Remaining risks

1. **Integration suite unexecuted** (see above) — if any of the 60+ hand-edited code literals or the
   four special-case tests have a subtle mistake, it won't surface until Docker is available to run
   `dotnet test tests/ONEVO.Tests.Integration`.
2. **A running `ONEVO.Api.exe` process (PID 25384) was stopped** during this session to release a file
   lock blocking `dotnet build`/`dotnet ef`, with explicit user confirmation beforehand. If anything
   depended on that dev server staying up, it will need to be restarted manually.
3. **Existing tenant data with `positions.code` longer than 5 characters** will now block the
   migration (by design — see Migration behavior). Before running this migration against any
   environment with real data, check for and resolve any such rows first; the migration will not do it
   for you.
4. The **Position Template Packs** feature already in this working tree (uncommitted) has no `code`
   field on its seed payload. If/when "apply pack" is implemented, it must independently satisfy the
   ≤5-character code constraint added here — this report flags it but does not implement it.

## Reports-to pooled target correction

### Old incorrect rule

Both `CreatePositionCommandHandler` and `UpdatePositionCommandHandler` rejected any
`reportsToPositionId` whose target position had `PositionType != PositionEntity.TypeUnique`:

```csharp
if (reportsTo.PositionType != PositionEntity.TypeUnique)
    return Result<PositionResponse>.UnprocessableEntity(
        "Reports-to position must be a unique (single-occupancy) position; pooled positions cannot be selected as reporting targets.");
```

This predates the capacity-driven type model documented above (where `positionType` is now purely
*derived* from `maxOccupancy`, not user-selectable) and was left over from when `positionType` was an
independent, user-chosen field. Once capacity-driven derivation shipped, any manager/team-lead/
project-manager position created with `maxOccupancy > 1` became `pooled` and was permanently
unselectable as a reporting target — even though nothing about a position holding multiple occupants
disqualifies it from being reported to.

### New validation rule

The `PositionType` check was removed from both handlers (no validator ever contained it — it was
handler-only logic). A `reportsToPositionId` target — unique or pooled — is now accepted as long as it
passes the remaining structural checks, all pre-existing and unchanged:

- exists in the same tenant + legal entity (`GetByIdForLegalEntityAsync` returning non-null)
- `IsActive` (create + update)
- not the same position as the one being created/updated (update: `ReportsToPositionId == PositionId` rejected in both the validator and the handler; create: not applicable, a new position has no id yet)
- not a descendant of the position being updated, i.e. no reporting cycle (`IsDescendantAsync`, update only — create has no existing id to be a descendant of)

No new error messages were introduced; the four pre-existing user-facing messages ("Reports-to
position not found in this legal entity.", "Reports-to position is inactive.", "A position cannot
report to itself.", "Cannot set reports-to: would create a circular reporting hierarchy.") already
match the required behavior and expose no technical enum names.

Employee reporting-manager remains untouched — this is a structural `positions.reports_to_position_id`
fix only, consistent with the "Reporting manager" findings already recorded above in this report (no
`reportingManagerEmployeeId` field exists or was added anywhere).

Department head position validation (`UpdateDepartmentCommandHandler`/`CreateDepartmentCommandHandler`,
`HeadPositionId`) was inspected and does **not** have an equivalent `PositionType` restriction — it only
checks existence, active status, and `DepartmentId` match. Nothing to change there.

### Files changed

- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition/CreatePositionCommandHandler.cs`
  (removed the `PositionType != TypeUnique` check and its error return)
- `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommandHandler.cs`
  (same removal)
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/CreatePositionCommandHandlerTests.cs`
  (`Handle_ReturnsUnprocessableEntity_WhenReportsToPositionIsPooled` → `Handle_AllowsPooledPositionAsReportsToTarget`,
  now asserts success and that the pooled target's id/name flow through)
- `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/UpdatePositionCommandHandlerTests.cs`
  (same rename/repurpose, plus `IsDescendantAsync`/`CountActiveReportsToPositionAsync` mocks needed to
  reach the success path)

No validator, entity, configuration, repository, or department-head files needed changes.

### Tests run (TDD)

- Wrote both `Handle_AllowsPooledPositionAsReportsToTarget` tests first; ran them against the
  unmodified handlers and confirmed **RED**: both failed with `Assert.True() Failure: Expected True,
  Actual False` (the handler still returned the 422).
- Removed the `PositionType` check from both handlers; re-ran and confirmed **GREEN**.
- `dotnet build src/ONEVO.Api/ONEVO.Api.csproj` → Build succeeded, 0 errors (a stale `ONEVO.Api.exe`,
  PID 36624, was locking `ONEVO.Application.dll`; stopped with explicit user confirmation first, same
  as the PID 25384 precedent above).
- `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Position"`
  → **188/188 passed**.
- `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj` (full suite) → **1977/1977 passed**.
- `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --filter "FullyQualifiedName~Position"`
  → **126/129 passed**; the 3 failures are the same pre-existing `PositionTemplatePacksController`
  architecture failures already documented above (unrelated uncommitted WIP feature — a second
  `[Controller]` under `OrgStructure`), not caused by this change.
- `git diff --check` → no reported errors (only benign LF→CRLF autocrlf warnings).
- Integration tests (`PositionsIntegrationTests.cs`) were **not run** — same Docker/Testcontainers
  unavailability noted above. No integration test in that file asserted the old pooled-reports-to 422,
  so none needed updating; this was confirmed by grep (`Pooled|pooled|single-occupancy` only matches an
  unrelated `positionType` display assertion).

### Remaining risks

1. Integration suite still unexecuted (Docker/Testcontainers unavailable in this environment) — same
   gap as the rest of this report.
2. A second `ONEVO.Api.exe` (PID 36624) was stopped mid-session with user confirmation; restart the dev
   server manually if it was in use.
3. This change only widens what's *structurally* valid. It does not add any product-level guidance on
   *when* pointing a report at a pooled position is a good idea (e.g. UI copy, org-chart display for a
   pooled manager) — that's a frontend/UX concern, out of scope per the task's "do not edit frontend"
   instruction.
