# Clock-in Policy Backend — Part 1 Report

## Summary

Backend foundation for Clock-in Policy (`clock_in_policies` + `clock_in_late_deduction_rules`) is implemented end-to-end: domain, EF + RLS migration, repositories, Application CQRS, Attendance API controllers, FluentValidation, and focused unit/architecture tests.

No frontend changes. No commit/push. Biometric/device outage fallback was **not** implemented or referenced in this feature.

## Pre-change inspection

- `git status --short --branch` on `local/reporting-manager-run` showed unrelated prior WIP; Clock-in Policy was **absent** from Domain/Application/Infrastructure/Api before this task.
- Inventory source: `OneVo-HR/database/phase1-table-inventory.md` (`clock_in_policies`, `clock_in_late_deduction_rules`).
- Product docs: `modules/time-attendance/overview.md` (Clock-in Policy screen + late brackets). No dedicated userflow for Clock-in Policy setup was found under `Userflow/`.
- Permissions `attendance:read` / `attendance:write` already seeded; wired on new controllers.
- Closest clone patterns: Departments/Positions (LE-scoped CRUD + archive/restore), ChecklistTemplates (RuleForEach child rules).

## Endpoint contract

Route prefix uses **`/api/v1/attendance/...`** as suggested in the task. There was no existing Attendance controller convention in this backend; product docs prefer `time-attendance` for other TA endpoints — this Part 1 followed the task’s suggested `attendance` prefix deliberately.

| Method | Route | Permission |
|:-------|:------|:-----------|
| GET | `/api/v1/attendance/clock-in-policies?legalEntityId={guid}&includeInactive=` | `attendance:read` |
| GET | `/api/v1/attendance/clock-in-policies/{id}` | `attendance:read` |
| GET | `/api/v1/attendance/legal-entities/{legalEntityId}/clock-in-policies` | `attendance:read` |
| GET | `/api/v1/attendance/legal-entities/{legalEntityId}/clock-in-policies/{id}` | `attendance:read` |
| POST | `/api/v1/attendance/legal-entities/{legalEntityId}/clock-in-policies` | `attendance:write` |
| PUT | `/api/v1/attendance/legal-entities/{legalEntityId}/clock-in-policies/{id}` | `attendance:write` |
| POST | `/api/v1/attendance/legal-entities/{legalEntityId}/clock-in-policies/{id}/archive` | `attendance:write` |
| POST | `/api/v1/attendance/legal-entities/{legalEntityId}/clock-in-policies/{id}/restore` | `attendance:write` |

Notes:

- Tenant id is never accepted from request bodies; it is server-derived from `ICurrentUser`.
- Legal entity is route-scoped (or required query param on tenant-level list).
- Request/response use grouped `workAreaRules.hybrid` (not flat `either_*`).

### Request body shape (`UpsertClockInPolicyRequest`)

```json
{
  "name": "Default Clock-in Policy",
  "scope": {
    "type": "full_company",
    "departmentIds": [],
    "positionIds": [],
    "employeeIds": []
  },
  "effectiveFrom": "2026-08-21",
  "effectiveTo": null,
  "locationVerificationRequired": true,
  "allowedRadiusMeters": 100,
  "workAreaRules": {
    "onsite": { "biometricEnabled": true, "webEnabled": false, "trayEnabled": false, "photoRequired": false },
    "remote": { "biometricEnabled": false, "webEnabled": true, "trayEnabled": true, "photoRequired": true },
    "hybrid": {
      "biometricEnabled": false,
      "webEnabled": true,
      "trayEnabled": true,
      "photoRequired": true,
      "locationCheckRequired": true,
      "sourceRule": "employee_choice"
    },
    "field": {
      "biometricEnabled": false,
      "webEnabled": true,
      "trayEnabled": true,
      "photoRequirement": "required"
    }
  },
  "correctionRequiresApproval": true,
  "notificationRecipientResolver": "management_coverage_owner",
  "lateDeductionRules": [
    { "lateArrivalMinute": 15, "multiplier": 0, "timeOffTypeId": "GUID" },
    { "lateArrivalMinute": 30, "multiplier": 1.0, "timeOffTypeId": "GUID" }
  ],
  "isActive": true
}
```

## DB mapping (hybrid API ↔ either_* persistence)

| API / UI | Entity property | Column |
|:---------|:----------------|:-------|
| `workAreaRules.hybrid.biometricEnabled` | `EitherBiometricEnabled` | `either_biometric_enabled` |
| `workAreaRules.hybrid.webEnabled` | `EitherWebEnabled` | `either_web_enabled` |
| `workAreaRules.hybrid.trayEnabled` | `EitherTrayEnabled` | `either_tray_enabled` |
| `workAreaRules.hybrid.photoRequired` | `EitherPhotoRequired` | `either_photo_required` |
| `workAreaRules.hybrid.locationCheckRequired` | `EitherLocationCheckRequired` | `either_location_check_required` |
| `workAreaRules.hybrid.sourceRule` | `EitherSourceRule` | `either_source_rule` |

Scope ID arrays use native PostgreSQL `uuid[]` per inventory (`department_ids`, `position_ids`, `employee_ids`).

Late deduction rules are child rows in `clock_in_late_deduction_rules`, ordered ascending by `late_arrival_minute` on read/write.

## Permissions

| Action | Permission |
|:-------|:-----------|
| List / Get | `attendance:read` |
| Create / Update / Archive / Restore | `attendance:write` |

Both codes already existed in `PermissionSeeder` / module catalog. No new permission strings invented.

## Validation rules

- Name required, trimmed, max 120.
- Legal entity must exist, be active, tenant-scoped.
- Scope type: `full_company` | `department` | `position` | `employee`.
- `full_company`: no department/position/employee IDs.
- `department` / `position` / `employee`: required target IDs; membership validated against same tenant + legal entity.
- `effectiveFrom` required; `effectiveTo` nullable and must be ≥ `effectiveFrom` when set.
- `allowedRadiusMeters` required and positive when location verification is on.
- Hybrid `sourceRule`: `onsite` | `remote` | `employee_choice`.
- Field `photoRequirement`: `off` | `optional` | `required`.
- Late rules: positive minute, multiplier ≥ 0, non-empty `timeOffTypeId`, no duplicate minutes; stored ascending.

### Time Off type existence

`time_off_types` is **not implemented** in this backend yet. Column `time_off_type_id` is stored as `uuid` **without FK**. FluentValidation requires a non-empty GUID only. Existence/active/tenant checks are deferred until Time Off ships (documented risk).

## Conflict / overlap rules

Implemented conservative overlap prevention for **active** policies:

- Same legal entity + same `scope_type` + overlapping effective date ranges.
- `full_company`: any second active overlapping policy is rejected.
- `department` / `position` / `employee`: conflict only when target ID sets intersect.

**Missing product evidence:** docs do not define precedence across scope types (e.g. employee override vs department). No cross-scope precedence was invented. Restore also re-checks overlap.

## RLS

Migration `20260821063814_AddClockInPolicies`:

- `TenantTables = ["clock_in_policies", "clock_in_late_deduction_rules"]`
- ENABLE + FORCE RLS + `tenant_isolation` policy (admin / tenant mode), matching Departments pattern.
- Architecture test asserts TenantTables + policy coverage for this migration.

## Files changed / added

### Domain
- `src/ONEVO.Domain/Features/TimeAttendance/Entities/ClockInPolicy.cs`
- `src/ONEVO.Domain/Features/TimeAttendance/Entities/ClockInLateDeductionRule.cs`

### Application
- `Features/TimeAttendance/RepositoryInterfaces/IClockInPolicyRepository.cs`
- `Features/TimeAttendance/Models/ClockInPolicyWriteModels.cs`
- `Features/TimeAttendance/DTOs/Responses/ClockInPolicyResponse.cs`
- `Features/TimeAttendance/Mappers/ClockInPolicyMapper.cs`
- `Features/TimeAttendance/Validation/ClockInPolicyValidationRules.cs`
- `Features/TimeAttendance/Services/ClockInPolicyScopeMembershipValidator.cs`
- Commands: Create / Update / Archive / Restore (+ validators/handlers)
- Queries: List / Get / GetById
- `src/ONEVO.Application/DependencyInjection.cs` (scope membership validator registration)

### Infrastructure
- Configurations under `Persistence/Configurations/TimeAttendance/`
- `Persistence/Repositories/TimeAttendance/EfClockInPolicyRepository.cs`
- `Migrations/20260821063814_AddClockInPolicies.cs` (+ Designer + model snapshot update)
- `ApplicationDbContext.cs` DbSets
- `DependencyInjection.cs` repository registration

### API
- `Contracts/Attendance/ClockInPolicies/UpsertClockInPolicyRequest.cs`
- `Controllers/Tenant/Attendance/ClockInPoliciesController.cs`
- `Controllers/Tenant/Attendance/LegalEntityClockInPoliciesController.cs`

### Tests
- Unit: validators, handlers, mapper (`tests/ONEVO.Tests.Unit/Features/TimeAttendance/ClockInPolicies/`)
- Architecture: `ClockInPolicyControllerArchitectureTests.cs`

## Tests run

| Check | Result |
|:------|:-------|
| `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --configuration Release --no-restore` | Passed |
| `dotnet test ...Unit... --filter FullyQualifiedName~ClockInPolicy\|...Attendance` | **18 passed** |
| `dotnet test ...Architecture... --filter FullyQualifiedName~ClockInPolicy` | **10 passed** |
| Full Architecture suite | **624 passed, 1 failed** — pre-existing `IgnoreQueryFilters_UsageIsExplicitlyAllowlisted` on `EfEmployeeRepository.cs` (unrelated to this task; present in prior WIP) |
| `dotnet ef migrations has-pending-model-changes` | **No pending changes** |
| `git diff --check` (Clock-in Policy paths) | Clean |
| Integration / Docker HTTP + RLS E2E | **Skipped** — Docker Desktop unable to start |

## Skipped checks and why

1. **HTTP integration tests** (create→get→update→archive→restore, 403/400, tenant isolation, RLS E2E): Docker daemon unavailable (`Docker Desktop is unable to start`). Documented as skipped, not passed.
2. **Repository Postgres tests** with real `uuid[]` / RLS: same Docker blocker.
3. **Time Off type existence validation**: Time Off module tables not present in backend.

## Remaining risks / missing product evidence

1. Overlap precedence across different scope types is undefined in product docs; only same-scope conservative overlap is enforced.
2. `time_off_type_id` has no FK / existence check until `time_off_types` lands.
3. Inventory/schema notes sometimes call the internal work-area value `either` with user-facing “Either”; this task intentionally exposes API/UI as **Hybrid** per product language instruction while persisting `either_*`.
4. Route prefix `attendance` vs product docs’ `time-attendance` may need alignment when more TA APIs ship.
5. No default/fallback fake policies are created.

## Explicit exclusion confirmation

**Biometric / device outage fallback was not implemented, referenced, linked, or mentioned in Clock-in Policy API contracts, handlers, entities, migrations, or tests for this feature.**  
`biometric_outage_fallbacks` remains out of scope.

## Verification commands (executed)

```text
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --configuration Release --no-restore
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release --no-build --filter "FullyQualifiedName~ClockInPolicy|FullyQualifiedName~Attendance"
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release --filter "FullyQualifiedName~ClockInPolicy"
dotnet ef migrations has-pending-model-changes --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
git diff --check  # focused Clock-in Policy paths
```
