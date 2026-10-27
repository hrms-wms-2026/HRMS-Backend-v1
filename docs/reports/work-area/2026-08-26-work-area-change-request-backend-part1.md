# ONEVO HRMS — Work Area Change Request Backend Part 1 Report

## 1. Scope and worktree safety

This implementation was performed only in `C:\onevoNew\HRMS-Backend-v1`. The frontend repository was not modified. No files were staged, committed, or pushed.

The initial worktree was:

```text
## local/reporting-manager-run
 M src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs
?? dev-server-restart.log
?? docs/superpowers/plans/2026-08-25-attendance-list-pagination.md
```

The existing `PositionsController.cs` modification, `dev-server-restart.log`, and `docs/superpowers/plans/2026-08-25-attendance-list-pagination.md` were preserved and were not used as part of this feature.

## 2. Documents and code inspected

The implementation was checked against the authoritative database inventory, the Time & Attendance schema, the Time & Attendance module overview, and the Agent Gateway work-location evidence end-to-end logic. The closest implementation patterns inspected were the attendance-correction controller, commands, queries, workflow, response DTOs, domain entity, EF configuration, repository, notification dispatcher contract, notification template seeder, notification view-model mapper, `AttendanceTodayStateService`, `AttendanceScheduleResolver`, `EmployeeAuthorityResolver`, `ApplicationDbContext`, dependency-injection registrations, and the existing PostgreSQL RLS migration pattern.

The relevant references are listed at the end of this report.

## 3. Implemented persistence contract

A tenant-owned `work_area_change_requests` entity now exists under the `TimeAttendance` domain feature. It contains `Id`, `TenantId`, `EmployeeId`, `LegalEntityId`, `Date`, `CurrentExpectedWorkArea`, `RequestedWorkArea`, `Reason`, `Status`, `RequestedAt`, `ReviewedById`, `ReviewedAt`, and `ReviewComment`.

The active Part 1 backend intentionally omits `shift_assignment_id`. The repository has no active shift-assignment entity/repository contract available for safe validation in this slice, so the implementation does not accept, store, or fabricate a request-supplied UUID. The authoritative shift field is explicitly documented as deferred rather than represented by a fake relationship.

The EF configuration maps the table with PostgreSQL `date` and `text` columns, restrictive foreign keys to employees, legal entities, and reviewers, ordinary indexes equivalent to `(tenant_id, employee_id, date)`, `(tenant_id, status)`, and `(tenant_id, legal_entity_id, status)`, and a partial unique index on `(tenant_id, employee_id, date)` filtered to `status IN ('pending', 'approved')`. The status column is an optimistic-concurrency token so concurrent approve/reject operations cannot silently overwrite one another.

The migration `20260825081439_AddWorkAreaChangeRequests` creates the table, indexes, restrictive foreign keys, and the forced tenant RLS policy using the existing `app.current_tenant_id` setting convention. The model snapshot was updated to include the entity, indexes, and concurrency metadata.

## 4. Permanent WorkMode versus one-day work-area override

The implementation keeps the distinction explicit. `Employee.WorkModeId` remains the permanent employee-level permission/configuration and is never changed by this workflow. A request is a date-specific approval record only. Approval updates the request status and review metadata; it does not mutate `Employee.WorkModeId`, attendance records, schedule assignments, clock-in policy, or remote-location profiles.

Supported requested values are exactly `onsite` and `remote`. `hybrid`, `either`, and `field` are not accepted as request targets. A Hybrid employee’s active WorkMode resolves to the internal expected-area fallback `either`; because both onsite and remote are already permitted in that fallback, a new request is rejected as not required. A current `field` baseline is rejected as unsupported rather than silently reinterpreted.

## 5. Expected-area sources actually implemented

Part 1 introduces an application-layer `IExpectedWorkAreaResolver`. It uses the existing legal-entity timezone/date resolver and the active WorkMode lookup record, comparing the stable catalog code rather than a display label or numeric ID. The implemented mappings are `onsite`/`on_site` to `onsite`, `remote` to `remote`, `hybrid` to `either`, and `field` to `field`.

Roster entries, shift assignments, schedule-day work areas, and approved work-area requests are not yet present as active resolver sources in this backend slice. The complete five-level documented runtime precedence chain is therefore not claimed here. Applying an approved request as the highest-priority source for Time Tracking is Backend Part 3.

The legal-entity-local date rule is implemented with `IDateTimeProvider` and the existing timezone fallback behavior. Today and future dates are allowed; past legal-entity-local dates are rejected. No arbitrary future-day limit was introduced.

## 6. API endpoints and DTO contracts

The dedicated tenant controller is `WorkAreaChangeRequestsController` at `/api/v1/attendance/work-area-change-requests` and follows the existing TenantPolicy, MediatR, pagination, Result, and `Problem()` conventions.

| Method | Route | Authorization | Behavior |
|---|---|---|---|
| `POST` | `/preview` | Authenticated tenant user | Performs non-mutating context, date, target, baseline, duplicate, and approver validation; returns timezone, current area, requested area, reason, and receiver summary. |
| `POST` | `/` | Authenticated tenant user | Creates a pending request and stages the approver notification in the same transaction. |
| `GET` | `/my` | Authenticated tenant user | Returns only the authenticated employee’s requests with bounded pagination and deterministic newest-first ordering. |
| `GET` | `/approvals` | `attendance:approve` | Returns only pending requests for employees currently visible to the authenticated reviewer through authority resolution. |
| `POST` | `/{id}/approve` | `attendance:approve` | Re-resolves the current approver route and approves only when the reviewer is currently eligible. |
| `POST` | `/{id}/reject` | `attendance:approve` | Requires a nonblank review comment and re-resolves reviewer eligibility. |
| `POST` | `/{id}/cancel` | Authenticated tenant user | Allows only the authenticated requester to cancel a pending request. |

The self-service request body accepts only `date`, `requestedWorkArea`, and `reason`. Tenant, employee, legal-entity, current-area, WorkMode, status, timestamps, reviewer identity, shift, and attachment identifiers are not accepted. Approve accepts an optional review comment; reject requires a nonblank review comment; cancel accepts only the route identifier.

## 7. Authority routing and permission behavior

Approval routing reuses `IEmployeeAuthorityResolver` with `EmployeeAuthorityPurpose.WorkAreaChangeApproval` and the existing `attendance:approve` permission. Approve and reject continue to call `ResolveApproverAsync` at mutation time. The existing resolver remains responsible for position coverage, department coverage, upward reporting fallback, primary owners, backup owners, candidate activity, tenant/legal-entity boundaries, permission checks, and self/subordinate exclusion.

The approval inbox now follows a two-stage exact-eligibility flow: the Work Area repository first returns distinct pending employee candidates for the current tenant, legal entity, and date range; `ResolveApprovalInboxScopeAsync` then returns only candidates whose current exact route resolves to the authenticated reviewer; the repository performs count, deterministic ordering, and pagination only over that eligible set. Broad visibility is no longer used as approval eligibility. Preview and create fail with a clear business conflict when no eligible approver exists. Create does not persist a request or stage a notification in that case. Approve and reject re-resolve the route at decision time and reject a reviewer who is not the currently eligible approver; they do not trust a stored or inferred reviewer identity.

## 8. Notifications and navigation metadata

The existing `INotificationDispatcher` and ambient unit-of-work transaction are reused. No handler inserts notification rows directly and no second notification service was created. The idempotent `NotificationTemplateSeeder` now includes:

| Template | Recipient | Content |
|---|---|---|
| `work_area_change_request_created` | Resolved approver | Requesting employee, requested date, and requested area. |
| `work_area_change_request_decided` | Requester | Approved/rejected decision, requested date, requested area, and safe review comment. |
| `work_area_change_request_cancelled` | Current resolved approver when safely resolvable | Requesting employee and requested date. |

All notifications use related entity type `work_area_change_request`, the request ID, and the existing templated dispatcher path. Notification view-model mapping exposes `WorkAreaChangeRequestId`. Pending approval notifications use the stable destination key `work_area_change_approval` and are navigable. Decision and cancellation notifications retain the request ID but are explicitly non-navigable because no frontend detail route was invented. Existing attendance-correction mappings remain compatible and unrelated mappings are unchanged.

Persistence and notification staging occur inside the same transaction. Validation, no-approver, duplicate, and save failures do not intentionally stage a durable notification.

## 9. RLS, indexes, and concurrency

The new table is covered by a migration-local `TenantTables` array and the repository-standard SQL pattern:

```sql
ALTER TABLE work_area_change_requests ENABLE ROW LEVEL SECURITY;
ALTER TABLE work_area_change_requests FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON work_area_change_requests;
CREATE POLICY tenant_isolation ON work_area_change_requests
    USING (tenant_id::text = current_setting('app.current_tenant_id', true))
    WITH CHECK (tenant_id::text = current_setting('app.current_tenant_id', true));
```

Reads use `AsNoTracking()`. Tracked fetches are used for state mutation. The database partial unique index is the authoritative protection against concurrent pending/approved duplicate submissions; handler-level duplicate checks are only an early friendly failure. The status concurrency token maps competing review transitions to the existing repository concurrency-conflict convention.

## 10. Files changed for this feature

| Area | Files |
|---|---|
| Domain | `src/ONEVO.Domain/Features/TimeAttendance/Entities/WorkAreaChangeRequest.cs` |
| Application commands/workflow | `src/ONEVO.Application/Features/TimeAttendance/Commands/WorkAreaChangeRequests/WorkAreaChangeRequestCommands.cs`; `WorkAreaChangeRequestWorkflow.cs` |
| Application queries/DTOs | `src/ONEVO.Application/Features/TimeAttendance/Queries/WorkAreaChangeRequests/WorkAreaChangeRequestQueries.cs`; `src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/WorkAreaChangeRequestResponses.cs` |
| Application service/repository contracts | `src/ONEVO.Application/Features/TimeAttendance/Services/IExpectedWorkAreaResolver.cs`; `ExpectedWorkAreaResolver.cs`; `src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IWorkAreaChangeRequestRepository.cs` |
| Validation | `src/ONEVO.Application/Features/TimeAttendance/Validators/WorkAreaChangeRequestValidators.cs` |
| Infrastructure persistence | `src/ONEVO.Infrastructure/Persistence/Configurations/TimeAttendance/WorkAreaChangeRequestConfiguration.cs`; `src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfWorkAreaChangeRequestRepository.cs`; `ApplicationDbContext.cs`; `DependencyInjection.cs` |
| Migration | `src/ONEVO.Infrastructure/Migrations/20260825081439_AddWorkAreaChangeRequests.cs`; corresponding `.Designer.cs`; `ApplicationDbContextModelSnapshot.cs` |
| API | `src/ONEVO.Api/Controllers/Tenant/Attendance/WorkAreaChangeRequestsController.cs`; `src/ONEVO.Api/Contracts/Attendance/WorkAreaChangeRequests/WorkAreaChangeRequestRequests.cs` |
| Notifications | `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs`; `src/ONEVO.Api/Contracts/SharedPlatform/Notifications/NotificationContracts.cs` |
| Tests | `tests/ONEVO.Tests.Unit/Features/TimeAttendance/WorkAreaChangeRequestTests.cs`; `WorkAreaChangeRequestWorkflowTests.cs`; `AttendanceCorrectionNotificationNavigationTests.cs`; `tests/ONEVO.Tests.Unit/Controllers/Tenant/Attendance/WorkAreaChangeRequestsControllerTests.cs`; `tests/ONEVO.Tests.Architecture/WorkAreaChangeRequestsArchitectureTests.cs`; `tests/ONEVO.Tests.Integration/Features/TimeAttendance/WorkAreaChangeRequestsIntegrationTests.cs` |

The pre-existing `src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs` change remained untouched and is not part of this implementation.

## 11. Tests added

The focused unit suite now covers preview context resolution and non-persistence, legal-entity-local date rules, supported-target and baseline validation, duplicate and no-approver failures, create transaction/notification behavior, exact approval-inbox eligibility before paging, re-resolution on approve, rejection-comment validation, cancellation ownership, and preservation of permanent WorkMode. The tests deliberately assert stable work-mode codes and do not treat numeric IDs as semantic business rules.

Dedicated notification regression tests prove typed Work Area metadata, null Attendance Correction metadata for Work Area events, preserved Attendance Correction routing, navigability rules, and unrelated-notification no-op behavior. Dedicated controller tests cover route-to-command mapping, success/failure status conventions, and server-owned request-body boundaries. Dedicated Work Area architecture tests cover tenant ownership, module placement, Application layering, provider/authority usage, controller permissions, DTO restrictions, migration RLS/index/FK safeguards, and notification metadata. Generic EmployeeAuthority tests remain green after the new candidate-scope contract was added.

## 12. Verification commands and results

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore` | Passed. |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkAreaChange"` | Passed. |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore --filter "FullyQualifiedName~TenantIsolationArchitectureTests"` | Passed. |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore` | 676 passed, 1 failed of 677. The remaining failure is the pre-existing `TimeTrackingMutationArchitectureTests.AttendanceRepository_UsesTrackedFetchForMutation`; its source-string assertion searches for a signature that is not present in the untouched `EfAttendanceReadRepository.cs`, producing `ArgumentOutOfRangeException` before evaluating the intended assertion. The dedicated Work Area architecture filter passed 9/9. |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorkAreaChange" --logger trx --results-directory TestResults --blame-hang --blame-hang-timeout 10m` | Passed: 4 Work Area PostgreSQL/Testcontainers tests discovered and executed. Coverage includes migrated schema columns/FKs/indexes, RLS enabled/forced and policy presence, partial unique predicate, restricted-role own-tenant reads, cross-tenant reads, cross-tenant update/delete filtering, cross-tenant insert rejection, and missing-context non-bypass. |
| `dotnet ef migrations list --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api --no-build --configuration Release` | Passed discovery; `20260825081439_AddWorkAreaChangeRequests` was listed. Applied-migration status could not be determined because the supplied design-time placeholder connection could not access PostgreSQL. |
| `dotnet ef migrations has-pending-model-changes --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api --no-build --configuration Release` | Could not determine applied-model status outside the integration container because no reachable design-time `MigrationConnection` was configured. The PostgreSQL integration suite migrated an empty disposable database and validated the resulting Work Area schema. |
| `dotnet ef migrations script --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api --no-build --configuration Release --output C:\temp\onevo-work-area-migrations.sql` | Passed. The generated script was inspected for table creation, restrictive foreign keys, required indexes, partial unique index, RLS enable/force, and tenant policy clauses. |
| `git diff --check` | Exit code 0. Git emitted only normal Windows line-ending normalization warnings. |
| `git diff --cached --name-only` | 0 staged files. |

The focused PostgreSQL migration and RLS checks passed through Docker Desktop/Testcontainers. The separate design-time `dotnet ef migrations has-pending-model-changes` check could not determine applied-migration status outside the integration container because no reachable `MigrationConnection` was configured; this is recorded as blocked rather than treated as a pass.

## 12A. Dated correction pass — 2026-08-25

The following corrections were applied in this pass. The approval inbox no longer uses broad `ResolveVisibilityAsync` results as approval eligibility. It obtains pending employee candidates in the current tenant/legal entity/date range, resolves the exact current approver scope through the generic authority contract, and only then performs final count, deterministic ordering, and pagination. Approve and reject continue to re-resolve the exact route at mutation time. The scope method is generic and accepts reviewer, legal entity, permission, purpose, and candidate IDs; it contains no Work-Area-specific routing rules.

Notification destination metadata is now additive and typed: Attendance Correction events retain `AttendanceCorrectionId`, Work Area events set `WorkAreaChangeRequestId`, and Work Area events never set `AttendanceCorrectionId` to `Guid.Empty`. Approval-request notifications remain navigable through `work_area_change_approval`; decision and cancellation notifications retain the Work Area request ID but are non-navigable. The arbitrary 255-character reason limit was removed from FluentValidation and handler-level validation because the authoritative persistence type is PostgreSQL `text` and no authoritative Work Area requirement or shared request-reason convention established that limit. Reasons remain required and trimmed. The duplicate ordinary EF index declaration was removed while preserving the ordinary tenant/employee/date index and the filtered active-request unique index.

Workflow tests now cover preview, create, my-history behavior, exact approval-inbox filtering, approve/reject/cancel transitions, notification transaction coupling, no-approver and duplicate failures, date/timezone rules, and preservation of permanent WorkMode. Controller tests cover routes, authorization attributes, DTO boundaries, MediatR mapping, and HTTP Result conventions. Dedicated architecture tests cover tenant ownership, layering, provider usage, permission requirements, migration RLS/index/FK safeguards, and notification metadata. PostgreSQL/Testcontainers integration tests now discover and execute four Work Area tests covering migration from empty, schema/index/FK/RLS policy state, restricted-role tenant reads, cross-tenant read/update/delete filtering, cross-tenant insert rejection, and missing tenant-context non-bypass.

The exact verification commands run were the requested Release/no-restore API build; focused Work Area, EmployeeAuthority, notification, and controller unit filters; the dedicated and full architecture suites; the focused PostgreSQL/Testcontainers filter; EF migration discovery; EF generated SQL; and `git diff --check`. The Work Area unit filter passed 39 tests, the notification/controller filter passed 66 tests, the EmployeeAuthority filter passed 35 tests, the dedicated Work Area architecture filter passed 9 tests, and the focused PostgreSQL filter passed 4 tests. The full architecture suite passed 676 tests and retained one pre-existing failure in `TimeTrackingMutationArchitectureTests.AttendanceRepository_UsesTrackedFetchForMutation`, whose source-string assertion throws `ArgumentOutOfRangeException` against untouched `EfAttendanceReadRepository.cs`. Migration discovery listed `20260825081439_AddWorkAreaChangeRequests`. Generated SQL was inspected for the Work Area table, restrictive foreign keys, ordinary indexes, filtered unique index, RLS enable/force, and tenant policy. The separate design-time pending-model command could not determine applied-model status outside the integration container because no reachable `MigrationConnection` was configured; it was not reported as passed.

Remaining risks are limited to the existing full-suite architecture failure and the scope-method implementation's reuse of the exact route resolver over the bounded candidate set; the Work Area workflow itself never pages broad visibility or filters a page afterward. Runtime application into Today, Clock In/Clock Out, history, and employee-list attention status remains explicitly deferred. No frontend files were changed, and no files were staged, committed, or pushed.

## 13. Known limitations and explicit Part 1 boundary

This is not described as end-to-end complete. Part 1 persists and routes the approved date-specific request, but it does not yet apply approved overrides to Time Tracking Today state, Clock In/Clock Out validation, attendance history display, attendance-record `expected_work_area`, clock-in source validation, or employee-list attendance attention status. Those runtime integrations are Backend Part 3.

The following remain deferred: frontend work; runtime Today/Clock-In/history integration; generic manual escalation; optional evidence attachments; shifts and schedules; permanent WorkMode changes; and Clock-in Policy Field cleanup. No feature-specific escalation endpoint was added. No upload endpoint, file ownership/link table, fake shift entity, fake shift endpoint, or request-supplied unvalidated shift UUID was introduced.

## 14. Final confirmation

The implementation was kept in the backend repository only. Nothing was staged, committed, or pushed. The existing unrelated worktree entries were preserved. The feature remains a Backend Part 1 persistence, request workflow, approval-routing, and notification slice, not a claim of complete runtime attendance integration.

## References

[1]: `C:\onevoNew\OneVo-HR\database\phase1-table-inventory.md` — Phase 1 table inventory and authoritative `work_area_change_requests` definition.
[2]: `C:\onevoNew\OneVo-HR\database\schemas\time-attendance.md` — Time & Attendance schema and work-area-change contract.
[3]: `C:\onevoNew\OneVo-HR\modules\time-attendance\overview.md` — Time & Attendance product rules and expected-area resolution boundary.
[4]: `C:\onevoNew\OneVo-HR\modules\agent-gateway\work-location-evidence\end-to-end-logic.md` — Work-location evidence and one-day work-area flow.
[5]: `src/ONEVO.Application/Features/TimeAttendance/Commands/AttendanceCorrections/AttendanceCorrectionWorkflow.cs` — Existing approval workflow and transaction pattern.
[6]: `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs` — Existing authority routing and eligibility rules.
[7]: `src/ONEVO.Infrastructure/Persistence/Migrations/20260515022320_AddRlsPolicies.cs` — Repository-standard tenant RLS migration pattern.
[8]: `src/ONEVO.Api/Controllers/Tenant/Attendance/AttendanceCorrectionsController.cs` — Existing tenant attendance controller conventions.

## Final authority, multi-company, scalability, and PostgreSQL hardening

This is a bounded correction pass over the Part 1 implementation above. It does not redesign the feature and does not implement runtime Time Tracking integration. Nothing was staged, committed, or pushed; work stayed inside `C:\onevoNew\HRMS-Backend-v1`; the frontend repository was not touched.

### Original problems and exact root causes

1. **Caller-supplied reviewer identity.** `EmployeeApprovalInboxScopeRequest` carried a `ReviewerUserId` field. The one production caller (`WorkAreaChangeRequestWorkflow.ListApprovalsAsync`) always passed `currentUser.UserId`, but nothing in `EmployeeAuthorityResolver.ResolveApprovalInboxScopeAsync` stopped a future caller from passing a different identifier, since the resolver never cross-checked the request against `ICurrentUser`.
2. **Wrong-company approval inbox for multi-company users.** `WorkAreaChangeRequestWorkflow.ResolveEmployeeContextAsync` resolved "the current employee/company" via `IEmployeeRepository.GetDefaultForUserAsync`, a login-time "most recent active PrimaryEmployment" heuristic. It never consulted `Session.ActiveEmployeeId`, the field `SwitchActiveCompanyCommandHandler` actually writes when a user switches companies via the topbar switcher — so a user who switched companies could see (or be filtered out of) the wrong legal entity's approval inbox.
3. **Unbounded per-candidate authority resolution.** `ResolveApprovalInboxScopeAsync` looped over every candidate employee id and called the full `ResolveApproverAsync` routing walk (position coverage → department coverage → reporting line, each step making its own repository round trips) once per candidate — a database call volume proportional to the number of pending requests in the legal entity, not a bounded constant.
4. **Missing direct/PostgreSQL test evidence.** The 35 pre-existing `EmployeeAuthorityResolverTests` covered `ResolveVisibilityAsync`/`ResolveApproverAsync` only; `ResolveApprovalInboxScopeAsync` was exercised solely through a mocked assertion in `WorkAreaChangeRequestWorkflowTests`, which proves the workflow calls the method but not that the method itself is correct. The PostgreSQL integration suite proved migration shape and RLS but never drove an insert through the filtered active-request unique index.

### Reviewer identity: before and after

**Before:** `EmployeeApprovalInboxScopeRequest(Guid ReviewerUserId, Guid LegalEntityId, string RequiredPermission, EmployeeAuthorityPurpose Purpose, IReadOnlyCollection<Guid> CandidateEmployeeIds)` — reviewer identity was a caller-supplied parameter, trusted as-is.

**After:** `EmployeeApprovalInboxScopeRequest(Guid LegalEntityId, string RequiredPermission, EmployeeAuthorityPurpose Purpose, IReadOnlyCollection<Guid> CandidateEmployeeIds)` — no reviewer field exists on the request at all. `EmployeeAuthorityResolver.ResolveApprovalInboxScopeAsync` derives the reviewer exclusively from the `ICurrentUser` already injected into the resolver's constructor, and fails closed (returns an empty collection, since the method's return type has no `Result<T>`/error channel) when:

- the caller is unauthenticated (`!ICurrentUser.IsAuthenticated`);
- `ICurrentUser.UserId == Guid.Empty`;
- `ICurrentUser.TenantId == Guid.Empty` (tenant context missing);
- the reviewer has no active employee row in the requested legal entity — checked via `IEmployeeRepository.GetByUserAndLegalEntityAsync`, which already joins to `employment_statuses` and filters `status.Code == "active"`, so this single call satisfies the "active employee" precondition without a new repository method;
- the reviewer lacks `RequiredPermission` (`IPermissionRepository.UserHasPermissionCodeAsync`);
- the candidate collection is empty.

`IEmployeeAuthorityResolver.cs` gained an XML doc comment on `ResolveApprovalInboxScopeAsync` stating this explicitly. `WorkAreaChangeRequestWorkflow.ListApprovalsAsync` (the one production caller) and `WorkAreaChangeRequestWorkflowTests.ApprovalInbox_UsesExactApproverScopeBeforeFinalPaging` (the one test caller) were both updated to the 4-argument constructor.

### Multi-company legal-entity context: evidence and final contract

**Investigation.** `ICurrentUser` (`src/ONEVO.Application/Common/ServiceInterfaces/ICurrentUser.cs`) exposes `UserId`, `TenantId`, `Email`, `Permissions`, `IsAuthenticated`, and an optional `SessionId`/`SessionExpiresAt`/`SessionBinding` — no legal-entity or company concept. The authoritative "which company is the user currently acting in" signal is `Session.ActiveEmployeeId` (`src/ONEVO.Domain/Features/Auth/Entities/Session.cs`), read today inside `TenantDatabaseTicketStore.RetrieveAsync` purely to scope permission grants, and written by two places: `TenantDatabaseTicketStore.StoreAsync` at login (seeds it via `GetDefaultForUserAsync`) and `SwitchActiveCompanyCommandHandler.Handle` (`src/ONEVO.Application/Features/Auth/ActiveCompany/Commands/SwitchActiveCompany/SwitchActiveCompanyCommandHandler.cs`) when the user explicitly switches companies via the topbar switcher.

Critically, `SwitchActiveCompanyCommandHandler` is itself an **Application-layer MediatR handler** that reads the session directly via `ICurrentUser.SessionId` + `ISessionRepository.GetByIdAsync` (`ISessionRepository` lives in `ONEVO.Application.Features.Auth.Login.RepositoryInterfaces`, already registered in DI as a facet of `EfAuthRepository`). This is the existing precedent proving the authoritative selected-company signal **is** reachable from an Application-layer handler today — so the task's "Preferred: existing server-side selected-company context" branch applies, not the "explicit route-scoped `legalEntityId`" fallback branch. No new route, query parameter, or claim was added.

**Final contract.** `WorkAreaChangeRequestWorkflow` gained one new constructor dependency, `ISessionRepository sessions`, inserted after `employees`. Its shared private `ResolveEmployeeContextAsync` — the single method behind `PreviewAsync`, `CreateAsync`, `CancelAsync`, `ListMyAsync`, and `ListApprovalsAsync` — now calls a new `ResolveActiveEmployeeAsync` helper that:

1. If `ICurrentUser.SessionId` is set, loads the session and, when it is not revoked, belongs to the same tenant and user, and has a non-null `ActiveEmployeeId`, loads that employee via `IEmployeeRepository.GetByIdAsync` and returns it (after confirming the employee's `UserId` matches the caller — defense against stale/corrupted data).
2. Otherwise falls back to the pre-existing `GetDefaultForUserAsync` heuristic, unchanged.

Because `Session.ActiveEmployeeId` is populated at login for every user with at least one employee row (`TenantDatabaseTicketStore.StoreAsync`) and updated immediately on every company switch, the fallback path is only reached for the narrow edge case of a session predating the `ActiveEmployeeId` migration — a self-resolving case since sessions expire. This was a deliberate scope decision to avoid adding a second, more invasive repository method (e.g. "list all employee rows for a user") purely to harden an edge case that already matches pre-existing (non-regressing) behavior.

**Scope of the fix.** The spec allows fixing a shared root cause rather than expanding scope silently, and explicitly calls out that self-service preview/create/my/cancel share the defect only if code proves it. `ResolveEmployeeContextAsync` is provably shared by all five endpoints (single private method, no per-endpoint branching), so this fix corrects the approval inbox **and** self-service preview/create/my/cancel together. This is documented here explicitly per that requirement — it is a consequence of fixing one shared method, not a separately-scoped expansion.

### Approval-inbox eligibility algorithm (exact, five phases)

`EmployeeAuthorityResolver.ResolveApprovalInboxScopeAsync` now runs in five phases after the guard checks above:

1. **Subjects.** Batch-load every candidate employee by id (`IEmployeeRepository.ListByIdsAsync`, unfiltered by active status — mirrors `GetByIdAsync`'s lack of a filter), keep only those whose `LegalEntityId` matches the request, then batch-load their active PrimaryEmployment assignments (`IPositionAssignmentRepository.GetActivePrimaryByEmployeeIdsAsync`).
2. **Coverage.** Batch-load active Position-target coverage for every subject's position (`IPositionRepository.ListActivePositionCoverageByCoveredPositionIdsAsync`) and active Department-target coverage for every subject's department (`ListActiveDepartmentCoverageByCoveredDepartmentIdsAsync`).
3. **Owners, holders, ancestors.** Batch-load the owner positions referenced by any coverage row (`IPositionRepository.GetByIdsAsync`, pre-existing), their active holders (`IPositionAssignmentRepository.GetActiveHoldersByPositionIdsAsync`), and the ancestor chains (`IEmployeeHierarchyClosureRepository.GetAncestorChainsAsync`, ordered nearest-manager-first per chain) for every subject **and** every holder discovered — the holder ancestor chains let the subordinate guard be evaluated as `subject ∈ ancestors(holder)` instead of computing `descendants(subject)` separately.
4. **Resolvability.** Batch-load active-employee-in-legal-entity status for every holder (`IEmployeeRepository.ListActiveEmployeeIdsByIdsAsync`), unfiltered employee rows for every holder and ancestor (`ListByIdsAsync`), active-primary-assignment status for every ancestor (`GetActivePrimaryByEmployeeIdsAsync`, a second call to the same method used in phase 1, over the ancestor id set), and permission grants for every resolvable user (`IPermissionRepository.ListUserIdsHoldingPermissionAsync`).
5. **In-memory replay.** For each subject, walk position coverage, then department coverage, then the subject's own ancestor chain — in that exact priority order — replaying `TryResolveFromCoverageAsync`'s pooled-holder disambiguation (single holder auto-resolves; multiple holders require a matching `ResponsibleEmployeeId`; an unresolved level is skipped, not a dead end) and every guard `ResolveApproverAsync` applies (self-rejection, subordinate-of-subject rejection, active-in-legal-entity, permission) purely against the already-loaded dictionaries — no further database calls. A subject is eligible only if the resolved approver's `UserId` equals the reviewer's.

This makes `result == { c : ResolveApproverAsync(c).ApproverUserId == currentUser.UserId }` for every candidate `c`, verified directly (see Direct tests, below).

### How batching avoids per-candidate database calls

Every read in phases 1-4 above is a single batched query over an id set, not a loop. The `InboxScope_RepositoryCallCountIsConstant_RegardlessOfCandidateCount` theory (50 and 100 candidates) asserts every one of the six new/extended repository methods is called at most twice regardless of `N` — `GetActivePrimaryByEmployeeIdsAsync` is the only one called twice (once for subjects in phase 1, once for ancestors in phase 4); every other method is called once. No method is called once per candidate.

### Repository interfaces added

| Interface | New method |
|---|---|
| `IEmployeeRepository` | `ListByIdsAsync(tenantId, employeeIds, ct) : IReadOnlyDictionary<Guid, Employee>` |
| `IPositionAssignmentRepository` | `GetActivePrimaryByEmployeeIdsAsync(tenantId, employeeIds, ct) : IReadOnlyDictionary<Guid, PositionAssignment>` |
| `IPositionAssignmentRepository` | `GetActiveHoldersByPositionIdsAsync(tenantId, positionIds, ct) : IReadOnlyDictionary<Guid, IReadOnlyList<PositionActiveHolder>>` |
| `IPositionRepository` | `ListActivePositionCoverageByCoveredPositionIdsAsync(tenantId, legalEntityId, coveredPositionIds, ct) : IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>` |
| `IPositionRepository` | `ListActiveDepartmentCoverageByCoveredDepartmentIdsAsync(tenantId, legalEntityId, coveredDepartmentIds, ct) : IReadOnlyDictionary<Guid, IReadOnlyList<ManagementCoverageRecord>>` |
| `IEmployeeHierarchyClosureRepository` | `GetAncestorChainsAsync(tenantId, employeeIds, ct) : IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>` |
| `IPermissionRepository` | `ListUserIdsHoldingPermissionAsync(userIds, permissionCode, now, ct) : IReadOnlySet<Guid>` |

Each has a real (non-`IgnoreQueryFilters`, `AsNoTracking`, tenant- and legal-entity-scoped where applicable) EF implementation, and each of the six `Fake*Repository` classes in `EmployeeAuthorityTestGraph.cs` (plus the unrelated `FakeListEmployeesRepository` in `ListEmployeesQueryHandlerAuthorityResolverIntegrationTests.cs`, which also implements `IEmployeeRepository` and needed a matching stub to keep compiling) gained matching real implementations — not `NotImplementedException` stubs — so the equivalence and performance tests exercise real batching logic, not a second, divergent fake data path.

### Direct EmployeeAuthority tests added

28 new tests in `EmployeeAuthorityResolverTests.cs`: 3 fail-closed guard tests (unauthenticated, reviewer not an active employee in the legal entity, reviewer lacking the required permission), 1 equivalence test over a 12-candidate mixed fixture (position coverage to reviewer, position coverage to someone else, department coverage to reviewer, unroutable — asserted against the literal per-candidate `ResolveApproverAsync` loop), 21 scenario tests covering every item in the required list (reviewer identity from `ICurrentUser` not the request; position/department/backup/manual/reporting-line eligibility; exact-vs-visible exclusion; self/subordinate/cross-tenant/cross-legal-entity/inactive-candidate/inactive-reviewer exclusion; duplicate-id dedup; position-over-department and department-over-reporting-line priority; pooled-owner-position disambiguation with and without `ResponsibleEmployeeId`), and a 2-case performance `[Theory]` (50/100 candidates) asserting call counts stay ≤ 2 per batch method. `FakeCurrentUser`/`BuildResolver` in `EmployeeAuthorityTestGraph.cs` gained `currentUserId`/`isAuthenticated` parameters (previously hardcoded to `Guid.Empty`/`true`, since the resolver never read `UserId` before this correction) to make these tests possible without disturbing any of the 35 pre-existing tests, all of which still pass unchanged.

The 28 tests were written and passed against the **original, still-unbatched** per-candidate loop first (with only the fail-closed guards added), then re-run — with zero test-code changes — against the batched rewrite, all still green. This is the direct evidence that the batch refactor is behaviorally identical to the loop it replaced.

### PostgreSQL: partial unique-index tests

9 new tests in `WorkAreaChangeRequestsIntegrationTests.cs`, inserting directly via raw SQL on an admin connection (with `session_replication_role = replica` to bypass FK triggers against synthetic ids — this does **not** suspend unique-index enforcement, only ordinary/FK triggers) so the tests prove the database constraint itself, not an application-level pre-check: first pending succeeds; second pending same employee/date throws `PostgresException` with `SqlState == UniqueViolation` and `ConstraintName == "ux_work_area_change_requests_active_employee_date"`; approved-then-pending and pending-then-approved both throw; rejected-then-new-pending and cancelled-then-new-pending both succeed; same date/different employee succeeds; same employee/date/different tenant succeeds; same employee/different date succeeds. The pre-existing RLS tests (own-tenant read, cross-tenant read returns no rows, missing tenant context returns no rows, cross-tenant update/delete cannot mutate rows, cross-tenant insert rejected) were not modified and still pass under the restricted `onevo_app` role.

### Notification metadata and Parts F/G

No code changes were needed. Re-verification confirmed: `NotificationContracts.cs` already sets `AttendanceCorrectionId`/`WorkAreaChangeRequestId` typed and mutually exclusive, never assigns `Guid.Empty` to either, returns `null` destination metadata for unrelated notifications, and already restricts `IsNavigable`/`DestinationKey` to the `*_created` template only (decision/cancellation notifications retain the request id but are non-navigable, matching the "no invented frontend route" requirement). Validation/persistence rules (onsite/remote-only, required trimmed reason with no arbitrary length cap, 2000-character review-comment cap, server-derived current-expected-area/employee/legal-entity/tenant, `Employee.WorkModeId` never written, `IDateTimeProvider` used throughout, no EF import in the Application-layer workflow file) were re-read and confirmed unchanged.

### Files changed in this pass

Interfaces: `IEmployeeRepository.cs`, `IPositionAssignmentRepository.cs`, `IPositionRepository.cs`, `IEmployeeHierarchyClosureRepository.cs`, `IPermissionRepository.cs`, `IEmployeeAuthorityResolver.cs`, `EmployeeApprovalInboxScopeRequest.cs`. Implementations: `EmployeeAuthorityResolver.cs`, `EfEmployeeRepository.cs`, `EfPositionAssignmentRepository.cs`, `EfPositionRepository.cs`, `EfEmployeeHierarchyClosureRepository.cs`, `EfAuthRepository.cs`, `WorkAreaChangeRequestWorkflow.cs`. Tests: `EmployeeAuthorityResolverTests.cs`, `EmployeeAuthorityTestGraph.cs`, `WorkAreaChangeRequestWorkflowTests.cs`, `WorkAreaChangeRequestsIntegrationTests.cs`, `ListEmployeesQueryHandlerAuthorityResolverIntegrationTests.cs` (unrelated fake, updated only to keep compiling against the widened `IEmployeeRepository` interface).

### Commands run and exact test counts

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj -c Release --no-restore` | Passed, 0 errors. |
| `dotnet test tests\ONEVO.Tests.Unit -c Release --no-restore --filter "EmployeeAuthority\|WorkAreaChangeRequest"` | 95/95 passed. |
| `dotnet test tests\ONEVO.Tests.Unit -c Release --no-restore --filter "FullyQualifiedName~WorkAreaChangeRequest"` | 32/32 passed. |
| `dotnet test tests\ONEVO.Tests.Unit -c Release --no-restore --filter "FullyQualifiedName~EmployeeAuthority"` | 63/63 passed. |
| `dotnet test tests\ONEVO.Tests.Unit -c Release --no-restore --filter "FullyQualifiedName~WorkAreaChangeRequest\|FullyQualifiedName~NotificationNavigation"` | 43/43 passed. |
| `dotnet test tests\ONEVO.Tests.Unit -c Release --no-restore --filter "InboxScope"` | 28/28 passed (both before and after the Task 7 batch rewrite, unchanged). |
| `dotnet test tests\ONEVO.Tests.Unit -c Release --no-restore` (full suite) | 3168/3168 passed. |
| `dotnet test tests\ONEVO.Tests.Architecture -c Release --no-restore --filter "WorkAreaChangeRequests\|EmployeeAuthority"` | 14/14 passed. |
| `dotnet test tests\ONEVO.Tests.Architecture -c Release --no-restore` (full suite) | 676/677 passed. The 1 failure is the pre-existing `TimeTrackingMutationArchitectureTests.AttendanceRepository_UsesTrackedFetchForMutation`, same test/file/line as the original Part 1 report, same class of exception (`System.ArgumentOutOfRangeException`/`String.Substring`) against untouched `EfAttendanceReadRepository.cs` — confirmed unrelated to this correction. |
| `dotnet test tests\ONEVO.Tests.Integration --filter "WorkAreaChangeRequestsIntegrationTests"` (Testcontainers/Docker) | 13/13 passed (4 original + 9 new partial-unique-index tests). |
| `dotnet ef migrations list` | Confirmed `20260825081439_AddWorkAreaChangeRequests` is the latest Work Area migration; no new migration was created (Tasks 1-3 in this pass touch only Application-layer interfaces and Infrastructure repository method bodies, never the EF model). |
| `dotnet ef migrations has-pending-model-changes` | "No changes have been made to the model since the last migration." |
| `dotnet ef migrations script 0 20260825081439_AddWorkAreaChangeRequests --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api -c Release` | Generated the full from-scratch SQL script (7630 lines) through the Work Area migration and confirmed it contains: `CREATE TABLE work_area_change_requests`, the three restrictive FKs (`fk_work_area_change_requests_employees_employee_id`, `..._legal_entities_legal_entity_id`, `..._users_reviewed_by_id`), the five ordinary indexes, the filtered unique index `ux_work_area_change_requests_active_employee_date ON work_area_change_requests (tenant_id, employee_id, date) WHERE status IN ('pending', 'approved')`, `ENABLE`/`FORCE ROW LEVEL SECURITY`, and the `tenant_isolation` policy. |
| Both `dotnet ef` commands above required setting `ConnectionStrings__MigrationConnection` to a syntactically-valid placeholder connection string. This is the repository's own supported override mechanism, not an ad hoc workaround: `DotEnvLoader.MigrationConnectionProcessOverrideActive` (`src\ONEVO.Api\Configuration\DotEnvLoader.cs:24,30-31`) explicitly detects and honors a process-level `ConnectionStrings__MigrationConnection` override, and neither EF command needs a reachable database — `has-pending-model-changes` only compares the current model against the snapshot, and `migrations script` only renders SQL from migration metadata. |
| `git diff --check` / `git status --short` / `git diff --stat` | No new whitespace errors beyond the pre-existing CRLF-normalization warnings; only files listed above changed; nothing staged. |

### Skipped or blocked checks

- Part E's optional HTTP-layer integration coverage (approval-inbox legal-entity isolation, eligible-vs-visible approve/reject, re-resolution-after-routing-change, all exercised through the real ASP.NET pipeline rather than direct repository/Testcontainers access) was not added in this pass. A tenant-authenticated `WebApplicationFactory` fixture pattern does exist elsewhere in the suite (e.g. `tests/ONEVO.Tests.Integration/Billing/TenantSubscriptionAdminApiIntegrationTests.cs`, via the shared `WebApplicationFactoryCollection`), so this is a fillable gap, not a structurally blocked one — it was deferred to keep this pass bounded to the 8 parts specified, not because the fixture is unavailable. As a result, controller-level concerns (route authorization, model binding, DI-container resolution of the workflow's 12-argument constructor, HTTP status-code mapping) remain proven only indirectly: by the unit tests against the handler/resolver directly, by the Testcontainers tests against the repository and RLS/unique-index layer, and by the full architecture-test suite (which does not include a live HTTP round trip for this feature). This should be the first item picked up if HTTP-level coverage for Work Area Change Requests is prioritized next.

### Remaining risks

- The full architecture suite's one pre-existing failure (`TimeTrackingMutationArchitectureTests`) remains open; it predates and is unrelated to this correction.
- `WorkAreaChangeRequestWorkflow.ResolveActiveEmployeeAsync`'s fallback path (session present but `ActiveEmployeeId` null) still uses the `GetDefaultForUserAsync` heuristic rather than failing closed for multi-employee users, on the reasoning that `Session.ActiveEmployeeId` is populated for essentially all authenticated sessions with at least one employee row and the edge case is self-resolving via session expiry. If a future audit finds this assumption violated in production, the next step is a dedicated `ListByUserIdAsync`-style repository method to detect and fail closed on the ambiguous multi-employee/no-active-employee case, deliberately not added here to keep this pass bounded.
- Runtime application of approved Work Area overrides to Today state, Clock In/Clock Out validation, attendance history, expected-work-area display, clock-in source validation, and employee-list attention status remains the next backend part, exactly as stated in the original report's §13 — this pass does not change that boundary.
- `WorkAreaChangeRequestWorkflow` gained a 12th constructor dependency (`ISessionRepository`) in this pass. `ISessionRepository` is already registered in the DI container (`Infrastructure` DI, scoped, same lifetime as the workflow's other dependencies — no captive-dependency risk), and unit tests construct the workflow by hand rather than through the container. No test in this pass boots the real ASP.NET host and resolves `WorkAreaChangeRequestWorkflow` through it (`ApiBootTests` boots the host but does not call `ValidateOnBuild`/`ValidateScopes`, and no Work-Area HTTP integration test exists — see "Skipped or blocked checks" above). The full architecture-suite pass (676/677, only the pre-existing unrelated failure) does not include a live DI-resolution check for this specific class either. This is assessed as low risk given the dependency is already registered and used elsewhere, but it is not empirically proven end-to-end in this pass.

### Confirmations

Frontend was untouched. Permanent `Employee.WorkModeId` was never written to by any code changed in this pass. Nothing was staged, committed, or pushed at any point during this correction.
