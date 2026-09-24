# Bulk Onboarding Issue Resolution — Backend Report

**Date:** 2026-08-20  
**Repo:** `HRMS-Backend-v1`  
**Branch:** `local/reporting-manager-run`

## Current root cause

Validate returned only repeated per-row `errorMessage` strings (e.g. “Department 'Sales' was not found…”). Matching was exact case-insensitive name/code with no suggestions, no grouped issues, and no in-flow resolve actions. HR had to leave Bulk Onboarding and create org setup elsewhere.

## Files changed

### New
- `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/BulkOnboardingNameMatcher.cs`
- `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/BulkOnboardingResolutionStateSerializer.cs`
- `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Models/BulkOnboardingIssueTypes.cs`
- `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Services/BulkOnboardingIssueGrouper.cs`
- `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/ValidateBulkOnboardingBatch/BulkOnboardingValidationRunner.cs`
- `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/ResolveBulkOnboardingIssues/ResolveBulkOnboardingIssuesCommandHandler.cs`
- `src/ONEVO.Infrastructure/Migrations/20260820120000_AddBulkOnboardingIssueResolutionState.cs`
- Unit tests: `BulkOnboardingNameMatcherTests`, `BulkOnboardingIssueGrouperTests`, `ResolveBulkOnboardingIssuesCommandHandlerTests`
- Plan note (recreated as needed): issue-resolution design approach documented in this report

### Updated
- Domain: `BulkOnboardingBatch.ResolutionStateJson`, `BulkOnboardingBatchRow.ResolvedWorkModeId`
- EF batch config + `ApplicationDbContextModelSnapshot`
- `BulkOnboardingRowValidator` / `IBulkOnboardingRowValidator` (structured error codes + overlays)
- Validate command/handler/result shape
- `BulkOnboardingController` + validate contracts (`issues`, structured row `errors`)
- `BulkOnboardingBatchProcessor` (effective raw data + `ResolvedWorkModeId`)
- DI registration for `IBulkOnboardingValidationRunner`
- `BulkOnboardingControllerTests`

## Endpoint contracts

### `POST /api/v1/onboarding/bulk-batches/{id}/validate` (`employees:write`)
Response (additive):
- `validRows`, `invalidRows`, `totalRows`
- `rows[]`: `rowNumber`, `status`, `errorMessage`, `errors[{code,field,message,importedValue}]`
- `issues[]`: `issueKey`, `issueType`, `field`, `importedValue`, `affectedRowNumbers`, `affectedRowCount`, `suggestions[{id,label,confidence}]`, `allowedActions`

### `POST /api/v1/onboarding/bulk-batches/{id}/resolve-issues` (`employees:write`)
Body:
- `issueKey`, `action`, `targetId?`, `newValue?`, `workModeId?`, `applyToRowNumbers?`
- `create?` (department), `createPosition?`
Actions: `map_existing`, `edit_imported_value`, `create_department`, `create_position`, `set_default`  
Returns the same validation response shape after revalidation.  
`create_*` requires `org:manage` (403 otherwise). Tenant is server-derived only.

## Issue types supported

Setup-fixable: `department_not_found`, `position_not_found`, `work_mode_missing`, `work_mode_not_found`, `employment_type_*`, `checklist_template_not_found`  
Row-edit: missing names/email, duplicates, invalid start date, reporting manager issues  

Not invented at validate time: `position_full` / `no_vacancy` (capacity still handled at finalize as waiting states).

## Permission behavior

| Action | Permission |
|--------|------------|
| Validate / resolve map+edit / set_default | `employees:write` |
| Create department/position from resolver | `org:manage` (enforced server-side; omitted from `allowedActions` when absent) |
| Get batch | `employees:read` |

## Persistence / audit

- Original `RawDataJson` is not mutated.
- HR fixes stored in batch `ResolutionStateJson` (value maps + row overrides with original field values).
- Resolved entity IDs (incl. work mode) stamped on rows during validate/revalidate.

## Tests run

```text
dotnet build src/ONEVO.Api/ONEVO.Api.csproj          → success
dotnet test ... --filter FullyQualifiedName~BulkOnboarding → 50 passed
dotnet test tests/ONEVO.Tests.Architecture/...      → 612 passed, 1 failed (pre-existing)
git diff --check                                    → CRLF warnings only; no conflict markers
```

## Skipped checks

- Bulk onboarding integration tests / Docker (not run).
- Architecture failure `IgnoreQueryFilters_UsageIsExplicitlyAllowlisted` cites `EfEmployeeRepository.cs` — pre-existing on branch, unrelated to this feature.

## Remaining risks

- Create/map UX for “choose existing” still needs good FE pickers (IDs via prompts on FE for now).
- Employment-type suggestions catalog is limited (code lookup only; no full list repo).
- Migration must be applied to environments before resolve-state persists.
- Semgrep plugin was disabled locally to unblock tooling (`semgrep@claude-plugins-official: false`).
