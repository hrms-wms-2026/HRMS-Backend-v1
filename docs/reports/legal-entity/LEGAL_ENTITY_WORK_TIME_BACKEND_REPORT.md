# Legal Entity General Settings — Default Work Start/End Time (Backend)

Adds default company work start/end time to Legal Entity General Settings, as a
Phase 1 stand-in ahead of the deferred Time & Attendance shift/schedule feature.

## Scope note: pre-existing dirty branch state

This branch already had substantial **unrelated, uncommitted Position-feature
work** in the working tree before this task started (`PositionsController`,
`CreatePosition`/`UpdatePosition` command changes, `PositionTemplatePacks`,
`AddPositionCodeMaxLength` migration, etc.). None of those files were touched
by this task. The regenerated `ApplicationDbContextModelSnapshot.cs` therefore
also carries that pre-existing `Code` column max-length change (40 → 5) as a
side effect of `dotnet ef migrations add` snapshotting the *whole* current
model — that diff predates this task and was not introduced by it.

## DB fields added

Migration `20260812135635_AddLegalEntityWorkStartAndEndTime` (additive only,
targets `legal_entities` only):

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `work_start_time` | `time` (Postgres `time without time zone`) | yes | |
| `work_end_time` | `time` (Postgres `time without time zone`) | yes | |

Check constraint `ck_legal_entities_work_time_pair`:
```sql
(work_start_time IS NULL AND work_end_time IS NULL)
OR (work_start_time IS NOT NULL AND work_end_time IS NOT NULL AND work_start_time < work_end_time)
```
Defense-in-depth alongside the command validator — enforces pairing and
same-day ordering at the DB layer too. No default value: existing legal
entities get `NULL`/`NULL`, which is a valid, complete state (both-null is
explicitly allowed), so no backfill was needed.

Domain: `LegalEntity.WorkStartTime` / `WorkEndTime` are `TimeOnly?`, matching
existing nullable-value-type conventions on the entity (no `DateTimeOffset`
used, per the task's constraint). EF config maps them via
`HasColumnType("time")`; column names (`work_start_time`/`work_end_time`)
follow the project's default snake_case naming convention (no explicit
`HasColumnName` needed, unlike `FirstDayOfWeek`).

**No `work_schedules` table, no shift management** was added — confirmed via
`OneVo-HR/database/phase1-table-inventory.md`, which models default working
time at the schedule level (`work_schedule_days.start_time`/`end_time`,
per-day, fixed/flexible, overnight-aware) rather than on `legal_entities`.
Adding these two flat columns to `legal_entities` is a deliberate, narrower
Phase 1 simplification per this task's explicit instruction, not the
canonical long-term design — flagged here per the task's own instruction to
confirm this discrepancy with the doc owners before/if it's ever reconciled.

## API contract

`GET /api/v1/org/legal-entities/{id}/general-settings` and
`PUT /api/v1/org/legal-entities/{id}/general-settings` both gained:

```json
{
  "workStartTime": "09:00",
  "workEndTime": "17:30"
}
```

Both fields are nullable (`null` when unset). Format is a fixed `"HH:mm"`
24-hour string.

**JSON format verified, not guessed**: System.Text.Json's built-in `TimeOnly`
converter emits seconds (`"09:00:00"`), which doesn't match the required
`"HH:mm"` contract. Added `TimeOnlyHhMmJsonConverter`
([src/ONEVO.Application/Common/Json/TimeOnlyHhMmJsonConverter.cs](src/ONEVO.Application/Common/Json/TimeOnlyHhMmJsonConverter.cs))
implementing `JsonConverter<TimeOnly>`, registered globally in `Program.cs`
via `AddControllers().AddJsonOptions(...)`. Registering against the
non-nullable `TimeOnly` converter is sufficient — System.Text.Json applies it
automatically to `TimeOnly?` properties too. Verified by a dedicated unit
test suite (`TimeOnlyHhMmJsonConverterTests`) asserting the exact wire
strings (`"09:00"`, `"17:30"`), null round-tripping, and that malformed input
(e.g. `"not-a-time"` or `"09:00:00"`) throws `JsonException` → 400, not a
silent misparse.

## Validation rules (`UpdateLegalEntityGeneralSettingsCommandValidator`)

- Both null → valid (no schedule set).
- Only one of `workStartTime`/`workEndTime` provided → validation error on
  the missing field.
- Both provided, `workStartTime >= workEndTime` → validation error on
  `workStartTime`.
- Same-day only — no overnight-shift support (`workStartTime < workEndTime`
  is a strict same-day comparison, not shift-aware).
- Invalid `"HH:mm"` format or wrong JSON type on the wire → the custom
  converter throws `JsonException` during model binding → ASP.NET Core
  returns 400 before the validator even runs.

All failures surface as `400 Bad Request`, consistent with the rest of this
endpoint's validation.

## Permissions

Unchanged. `GET` and `PUT` both still require `legal_entity:update` (the
endpoint's existing, actual contract — not `org:read`/`org:manage` as an
older doc states; see the pre-existing
`LEGAL_ENTITY_BACKEND_GENERAL_SETTINGS_SCHEMA_RECONCILIATION_REPORT.md`
discrepancy note, which this task did not touch). `tenantId` is still never
accepted from the request body; the route `{id}` remains authoritative and
`TenantId` is sourced from `ICurrentUser`.

## Onboarding/defaults impact

Inspected `FinalizeOnboardingDraftCommandHandler`
([src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs](src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs)):
it loads the target `LegalEntity` only to validate it exists and is active
(line ~146-148) and never copies *any* General Settings field (not
`Timezone`, `TimeFormat`, `StandardWorkingDays`, and now not
`WorkStartTime`/`WorkEndTime` either) onto the new `Employee` record.

The `Employee` entity has no work-time-related field today (`HireDate`,
`ProbationEndDate`, `TerminationDate`, `DateOfBirth` are the only
`DateOnly`/`DateOnly?` fields; nothing analogous for time-of-day).

Per this task's instruction, **no field was invented** and onboarding was
**not modified**. Follow-up to track separately: once employee-level default
working hours are needed (e.g. for attendance/monitoring expected-hours
checks), that will need either a new `Employee` field or — more likely, given
the canonical schema — the deferred `work_schedules`/`schedule_assignments`
feature, at which point `legal_entities.work_start_time`/`work_end_time`
would become the schedule's fallback default rather than a per-employee
value.

## Tests

**Unit** (`tests/ONEVO.Tests.Unit`):
- `TimeOnlyHhMmJsonConverterTests` (new) — write format, null handling, read
  round-trip, malformed-input `JsonException`.
- `UpdateLegalEntityGeneralSettingsCommandValidatorTests` — both-null valid,
  valid pair valid, only-start error, only-end error, equal-times error,
  start-after-end error.
- `UpdateLegalEntityGeneralSettingsCommandHandlerTests` — persists valid
  start/end onto the entity and into the response; setting both back to null
  clears a previously-set pair.
- `GetLegalEntityGeneralSettingsQueryHandlerTests` — GET response carries
  `WorkStartTime`/`WorkEndTime` from the entity.
- `LegalEntitiesControllerTests` — request fields pass through to the command
  unchanged.

**Integration** (`tests/ONEVO.Tests.Integration`, real Postgres via
Testcontainers): GET returns `"09:00"`/`"17:30"` string format; PUT persists
a valid pair; PUT with only start, only end, or start-not-before-end all
return 400. Existing tenant-isolation and permission tests were left as-is
(untouched by this change) and still pass.

**Architecture** (`tests/ONEVO.Tests.Architecture`):
`LegalEntityGeneralSettingsArchitectureTests` updated —
`LegalEntity_HasExactlyTheInventoryColumns` now expects `WorkEndTime`/
`WorkStartTime`; new
`Model_LegalEntities_WorkStartAndEndTime_MapToNullableTimeColumns` guards the
`time`/nullable/column-name mapping via the EF model. Model snapshot
regenerated by `dotnet ef migrations add`.

### Results

| Suite | Filter | Result |
|---|---|---|
| `dotnet build` (Api + all layers) | — | 0 errors, 0 warnings |
| `dotnet build` (Unit/Architecture/Integration test projects) | — | 0 errors each |
| Unit tests | `~LegalEntity` \| `~TimeOnlyHhMmJsonConverter` | 189 passed, 0 failed |
| Architecture tests | `~LegalEntity` | 73 passed, 0 failed |
| Integration tests | `~LegalEntitiesIntegrationTests` | 38 passed, 0 failed (real Postgres, Testcontainers) |
| `git diff --check` (all touched files) | — | clean (0 conflict markers/whitespace errors; only benign CRLF-normalization warnings on 2 files, pre-existing repo line-ending convention) |

TDD was followed: architecture, validator, handler, mapper (via handler/query
tests), controller, and converter tests were written or extended first, the
test projects were confirmed to fail to compile (RED — the referenced
members didn't exist yet), then production code was added incrementally
until each layer went green.

## Skipped checks

- **Full, unfiltered test suite / full architecture suite**: not run — scoped
  to LegalEntity/General-Settings/converter tests per the task's explicit
  instruction ("focused LegalEntity/GeneralSettings unit tests" /
  "architecture tests if touched"). Given the large amount of pre-existing,
  unrelated uncommitted Position work already in the tree, a full-suite run
  would mix in unrelated signal.
- **EF `database update`**: not run against the local dev database. Only
  `migrations add` was run (against the `MigrationConnection` role, using
  local `.env` credentials) to generate the migration + snapshot; applying it
  to the shared local dev DB was out of scope and not requested.
- A locally running `ONEVO.Api` dev server (pre-existing, unrelated to this
  task) was blocking `dotnet build` with a file lock. Stopped it after
  explicit user confirmation; it was not restarted (not requested).

## Remaining risks

1. **Schema divergence from the canonical Time & Attendance design.** As
   flagged above, `work_schedules`/`work_schedule_days` is the documented
   long-term home for default/per-day working time, with fixed/flexible
   modes and overnight support. This task's two flat columns are a
   deliberate simplification per explicit instruction, but will need a
   reconciliation/migration story once that feature ships (e.g. seeding a
   default `work_schedules` row from `legal_entities.work_start_time`/
   `work_end_time` on first Time & Attendance setup).
2. **No overnight-shift support**, by design in this task — a legal entity
   that genuinely runs a night shift cannot express it via these two fields
   today; it would return a validation error.
3. **No propagation to Employee/attendance.** Nothing currently reads these
   two fields for attendance/monitoring expected-hours logic — they are
   pure General Settings storage today, with no consumer yet.
4. **Doc drift** (pre-existing, not touched by this task): the company-profile
   overview doc still lists `GET` as `org:read`/`PUT` as `org:manage`, while
   the controller enforces `legal_entity:update` for both. Not corrected here
   as it predates and is unrelated to this change.
