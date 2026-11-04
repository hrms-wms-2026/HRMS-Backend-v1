# Employee Onboarding Backend Correction Report

## What was broken

- Drafts used `EmployeeName` and an active `ScheduleId` despite the employee model using separate names and no implemented schedule source.
- `WorkModeId` existed on employees but was absent from onboarding drafts and unvalidated.
- Missing subscription seat data was silently converted into `waiting_for_seat`.

## Changes

- Replaced active onboarding name contracts with `FirstName` and `LastName`; each is required, whitespace-rejected, trimmed before persistence, and limited to 100 characters.
- Added persisted `WorkModeId`, validates it against an active global `work_modes` lookup, and added the documented lookup `is_active` field.
- Removed active `ScheduleId` handling. Legal-entity settings remain the authoritative source for timezone and standard-working-day defaults; no schedule/shift behavior was introduced.
- Seat evaluation remains tenant-wide and position approval is evaluated before seats. Unconfigured entitlement now saves a Draft with `seat_configuration_required`; it is not misreported as a seat shortage.
- Draft saves now validate active tenant-scoped legal entities, departments, positions, their company/department relationship, and active work modes.
- No finalization endpoint/handler exists in this codebase, so no employee/user/invitation transaction was added.

## Migration

`20260810153000_CorrectOnboardingDraftIdentityAndWorkMode` removes `employee_name` and `schedule_id`, adds first/last name and work-mode columns/FK, and adds lookup activity status. It fails before committing if a legacy name has fewer than two parts, because safely deriving a surname would otherwise require guessing.

## Verification

- Passed: focused unit tests `FullyQualifiedName~Onboarding|FullyQualifiedName~SeatEntitlement` (27 tests).
- Passed: API build using an isolated temporary output (normal output is locked by a running `ONEVO.Api` process).
- Skipped: integration tests (not run; Docker-backed suite was not necessary for the source-contract correction).

## Remaining risks and required next work

- Subscription code has no authoritative purchased-seat, included-seat, overage, or pending-increase model. This is why unconfigured entitlement is persisted as `seat_configuration_required`; finalization must remain blocked until a billing policy exists.
- Final employee creation/onboarding finalization endpoint is absent. It must recheck tenant seats transactionally and map draft `FirstName`, `LastName`, and `WorkModeId` to Employee.
- Frontend must send `firstName`, `lastName`, and `workModeId`, and stop sending `employeeName`/`scheduleId`.
