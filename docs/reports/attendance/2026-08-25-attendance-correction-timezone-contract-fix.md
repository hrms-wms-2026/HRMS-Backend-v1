**Date:** 2026-08-24  
**Scope:** Attendance correction approval snapshot, response contract, persistence migration, and backend regression coverage  
**Repository:** `HRMS-Backend-v1`

## Summary

The remaining backend contract defect was caused by deriving `AttendanceCorrectionResponse.ApprovalRequired` from workflow status. A pending status happened to imply that approval was required, but approved, rejected, and cancelled statuses could represent either an approval-required request or an automatically approved request. Status therefore cannot preserve the original policy decision.

The correction now persists the creation-time policy snapshot in a required `AttendanceCorrection.ApprovalRequired` property, mapped to the PostgreSQL `approval_required` boolean column. The request workflow sets it from `value.Policy.CorrectionRequiresApproval` while continuing to set the initial workflow status independently. Response mapping now returns the stored property for requests, approval-inbox rows, approvals, rejections, cancellations, and personal history. Approval routing, notifications, auto-approval behavior, RLS policy behavior, endpoint URLs, and request payloads were not changed.

## Root cause and corrected contract

The previous mapper used the equivalent of:

```csharp
approvalRequired = correction.Status == AttendanceCorrection.StatusPending;
```

That expression tests a transient workflow state, not the immutable policy decision made when the correction was created. The resulting contract was incorrect for every approval-required request after it left `pending`: approved, rejected, and cancelled responses incorrectly returned `false`. Auto-approved requests correctly returned `false`, but only by coincidence.

The corrected invariant is:

| Workflow state | Status | `approvalRequired` |
|---|---|---:|
| Auto-approved | `approved` | `false` |
| Waiting for approval | `pending` | `true` |
| Approver approved | `approved` | `true` |
| Approver rejected | `rejected` | `true` |
| Requester cancelled pending request | `cancelled` | `true` |

The value is not recalculated from the current clock-in policy, reviewer metadata, status, or approver presence. This preserves historical truth even if a policy changes after submission.

## Files changed for this fix

| File | Change |
|---|---|
| `src/ONEVO.Domain/Features/TimeAttendance/Entities/AttendanceCorrection.cs` | Added required `bool ApprovalRequired` creation-time snapshot property. |
| `src/ONEVO.Infrastructure/Persistence/Configurations/TimeAttendance/AttendanceCorrectionConfiguration.cs` | Mapped the property explicitly to required PostgreSQL column `approval_required`. |
| `src/ONEVO.Application/Features/TimeAttendance/Commands/AttendanceCorrections/AttendanceCorrectionWorkflow.cs` | Set the snapshot during `BuildCorrection` and returned it from `ToResponse` instead of deriving it from status. |
| `src/ONEVO.Infrastructure/Migrations/20260824154945_AddAttendanceCorrectionApprovalRequired.cs` | Added the additive migration, temporary `false` default, existing-row backfill, and final non-null column state. |
| `src/ONEVO.Infrastructure/Migrations/20260824154945_AddAttendanceCorrectionApprovalRequired.Designer.cs` | Generated EF migration metadata for the updated model. |
| `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` | Reflected the `ApprovalRequired` boolean in the EF model snapshot. |
| `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceCorrectionNotificationTests.cs` | Added assertions for persisted creation values, post-approval/rejection/cancellation responses, auto-approval, and policy-change invariance. |
| `tests/ONEVO.Tests.Architecture/AttendanceCorrectionsArchitectureTests.cs` | Added source-level migration assertions for non-null column creation, status/reviewer backfill, and unchanged RLS scope. |

## Migration and backfill

The repository migration convention uses timestamp-prefixed PascalCase names. The additive migration is `20260824154945_AddAttendanceCorrectionApprovalRequired`. Its SQL was generated and inspected. The relevant sequence is:

```sql
ALTER TABLE attendance_corrections ADD approval_required boolean NOT NULL DEFAULT FALSE;

UPDATE attendance_corrections
SET approval_required = TRUE
WHERE status IN ('pending', 'rejected', 'cancelled')
   OR reviewed_by_id IS NOT NULL
   OR reviewed_at IS NOT NULL;

ALTER TABLE attendance_corrections ALTER COLUMN approval_required DROP DEFAULT;
```

This safely classifies existing rows using the established workflow invariants. It does not inspect or recalculate the current clock-in policy, and it does not alter RLS policies.

## Tests and verification run

| Check | Result |
|---|---|
| `dotnet build src\\ONEVO.Api\\ONEVO.Api.csproj --configuration Release --no-restore` | Passed: 0 errors, 2 existing warnings. |
| `dotnet test tests\\ONEVO.Tests.Unit\\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~AttendanceCorrection"` | Passed: **17 tests**. |
| `dotnet test tests\\ONEVO.Tests.Architecture\\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore` | Passed: **658 tests**. |
| `dotnet ef migrations list --project src\\ONEVO.Infrastructure\\ONEVO.Infrastructure.csproj --startup-project src\\ONEVO.Api\\ONEVO.Api.csproj --configuration Release --no-build` | Discovered `20260824154945_AddAttendanceCorrectionApprovalRequired`; database-applied status could not be read because PostgreSQL authentication failed for `onevo_migrator`. |
| `dotnet ef migrations script --project src\\ONEVO.Infrastructure\\ONEVO.Infrastructure.csproj --startup-project src\\ONEVO.Api\\ONEVO.Api.csproj --configuration Release --no-build` | Passed; generated SQL contains the additive boolean column, backfill, default removal, and migration-history insert. |
| Repository pending-model check with `MigrationConnection` | Executed, but reported `Changes have been made to the model since the last migration. Add a new migration.` The check is recorded as not passing; the repository already exposes model drift around the original attendance-corrections migration, so no unrelated migration rewrite was performed. |
| `git diff --check` | Run as part of final hygiene; no whitespace errors were introduced. |

The focused tests cover approval-required and auto-approved creation, pending/approved/rejected/cancelled response values, policy-change invariance, notification-preserving workflow behavior, and the migration/architecture invariants. The tests use the existing workflow fixture and capture the entity passed to persistence; a live PostgreSQL persistence test was not run because the available Docker daemon was unavailable and the configured local migration role password was not valid.

## Migration application and skipped checks

The migration was not applied to PostgreSQL. Migration discovery and SQL rendering succeeded, but the database reported `28P01: password authentication failed for user "onevo_migrator"`, so no live application is claimed. Docker/Testcontainers integration execution was skipped because the connected Windows Docker daemon was unavailable. The pending-model check was executed and reported model drift rather than being silently treated as successful. The complete backend unit suite was not run; verification used the requested AttendanceCorrection filter and the full architecture suite.

## Remaining risks

A live PostgreSQL run is still required to validate application of the additive migration, row backfill, non-null enforcement, and behavior against production-like RLS and role permissions. The repository pending-model check remains red because of model drift associated with the existing attendance-corrections migration history; resolving that broader drift was outside this task and would violate the instruction not to rewrite an already-discoverable migration.

## Final status

The backend contract correction is complete: the approval requirement is persisted once at creation, preserved through every workflow state, and returned directly from the stored snapshot. The migration is source-correct and SQL-verified but not live-applied. Backend verification completed with **17 focused unit tests**, **658 architecture tests**, and a successful Release build. No notification, approval-routing, authorization, RLS, endpoint, payload, or unrelated Attendance behavior was changed.

**No commit or push was performed.**
