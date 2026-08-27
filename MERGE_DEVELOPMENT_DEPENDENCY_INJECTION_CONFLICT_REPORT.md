# Merge Conflict Resolution Report — `src/ONEVO.Infrastructure/DependencyInjection.cs`

Merge: `git pull origin development` into `local/reporting-manager-run`.
Merge base commit for the conflicted file: stage 1. Current branch (`local/reporting-manager-run`): stage 2. Incoming `origin/development`: stage 3 (`ec459e5653f3269afb6c92c1779a631b2c9097c5`).

## Original conflict cause

Both branches modified the same region of `AddInfrastructure`, immediately after the existing `ILeavePolicyRepository` registration, but in different ways relative to the merge base:

- **Base** (stage 1): registered `IClockInPolicyRepository` and `IAttendanceReadRepository` directly after `ILeavePolicyRepository`, followed by `IPositionAssignmentRepository` / `IEmployeeHierarchyClosureRepository`.
- **`origin/development`** (stage 3): moved the `IClockInPolicyRepository` / `IAttendanceReadRepository` registrations earlier in the file (now non-conflicting, ahead of the `ILeaveTypeRepository` block), and inserted ~25 new Leave Management service/repository/option registrations at the position they previously occupied (Entitlement, BalanceAudit, Request, Calendar, Approval, Cancellation repositories, helpers, hosted job, and four `AddOptions<...>().Bind().Validate().ValidateOnStart()` blocks).
- **`local/reporting-manager-run`** (stage 2): left `IClockInPolicyRepository` / `IAttendanceReadRepository` at their original base position and added one new line, `IExpectedWorkAreaResolver` → `ExpectedWorkAreaResolver`, for the Attendance/Work Area feature.

Because both sides edited the same hunk in incompatible ways, git could not 3-way-merge it and left conflict markers (`<<<<<<< HEAD` / `=======` / `>>>>>>> ec459e5...`) around lines 195–264. All other files touched by the merge applied cleanly (confirmed via `git ls-files -u`, which listed only this one file across three stages).

## Stage 2 (current branch) registrations preserved

- `services.AddScoped<IClockInPolicyRepository, EfClockInPolicyRepository>();`
- `services.AddScoped<IAttendanceReadRepository, EfAttendanceReadRepository>();`
- `services.AddScoped<IExpectedWorkAreaResolver, ExpectedWorkAreaResolver>();`

All three concepts are present in the resolved file. The first two already existed, non-conflicting, at lines 175–176 (inherited unchanged from `origin/development`'s reordering — same type pair, same lifetime, same implementation). `IExpectedWorkAreaResolver` was genuinely new and has been added once, immediately after the incoming Leave Management block.

## Stage 3 (development) registrations preserved

All Leave Management registrations from `origin/development` are present, unchanged, fully qualified, in original order:

`ILeaveEntitlementRepository`, `ILeaveBalanceAuditRepository`, `ILeaveWorkingDayCounter`, `LeaveEntitlementCalculator`, `LeaveEntitlementPlanner`, `LeaveYearEndEntitlementJob` (hosted service), `LeaveEntitlementYearOptions` (options+validate+validateOnStart), `LeaveRequestOptions` (options+validate+validateOnStart), `LeaveCalendarOptions` (options+validate+validateOnStart), `ILeaveRequestRepository`, `ILeaveCalendarRepository`, `LeaveRequestDayCalculator` (singleton), `LeaveCalendarRequestProjector` (singleton), `ILeaveHolidayProvider`, `ILeaveCalendarHolidayProvider`, `ILeaveRequestConflictProvider`, `ILeaveApproverResolver`, `ILeaveTeamAbsenceWarningService`, `LeaveRequestSubmissionEvaluator`, `LeaveApprovalOptions` (options+validateOnStart), `ILeaveApprovalRepository`, `LeaveApprovalDecisionService`, `LeaveCancellationOptions` (options+validate+validateOnStart), `LeaveCancellationClassifier` (singleton), `LeaveRequestDayAllocationBuilder` (singleton), `LeaveBusinessDateResolver`, `ILeaveCancellationRepository`.

The pre-existing `ILeavePolicyRepository` registration directly above the conflict block was untouched (rule: do not remove it — confirmed still present at lines 192–194).

## Final registration ordering

Per the requested order (Leave Policy → incoming Leave Management → Attendance/Work Area → Position/Employee hierarchy):

1. `ILeavePolicyRepository` (pre-existing, unchanged)
2. Full incoming Leave Management block from `origin/development` (list above, order preserved as authored)
3. `IExpectedWorkAreaResolver` → `ExpectedWorkAreaResolver` (current branch's new registration)
4. `IPositionAssignmentRepository`, `IEmployeeHierarchyClosureRepository` (pre-existing, unchanged, immediately following)

The stray indentation on the old `IAttendanceReadRepository` line inside the conflict's `HEAD` block (16 spaces instead of 8) no longer exists in the file — see Duplicate-registration review below for why, rather than a reformat.

## Duplicate-registration review

Before resolving, the whole file was searched (`grep -c`) for each type on both sides of the conflict:

- `IClockInPolicyRepository, EfClockInPolicyRepository` — already registered once, non-conflicting, at line 175 (inherited from `origin/development`'s reordering). The `HEAD` block's second copy (with the misindented sibling line) would have created a duplicate `AddScoped` registration, so it was **removed** rather than kept/reformatted.
- `IAttendanceReadRepository, EfAttendanceReadRepository` — same situation, already registered once at line 176. The `HEAD` block's second (misindented) copy was **removed**.
- `IExpectedWorkAreaResolver, ExpectedWorkAreaResolver` — did not exist anywhere else in the file. Kept, once.
- `IPositionAssignmentRepository, EfPositionAssignmentRepository` — appears once, after the conflict, untouched.

Post-resolution verification (`grep -c` on the final file) confirms each of the four registrations above now appears **exactly once**. No other duplicate was introduced.

## Files manually changed

Only `src/ONEVO.Infrastructure/DependencyInjection.cs`. No other file listed by `git status` was edited — those are `origin/development`'s automatically-merged changes (already staged before this session started) and were left untouched per the task instructions.

## Unresolved-conflict check result

```
rg -n "^(<<<<<<<|=======|>>>>>>>)" .        → no matches (repo-wide, and specifically re-checked against src/tests)
git diff --name-only --diff-filter=U        → (empty)
git ls-files -u                             → (empty after `git add`)
git diff --check                            → no output (no whitespace conflict-marker residue)
git diff --cached --check                   → no output
```

`.claude` was never staged — it does not appear in `git diff --cached --name-only`.

## Build result

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --configuration Release --no-restore
```
**Build succeeded.** 1 warning (`CS8602`, `AdminAuthController.cs:62`, possible null dereference) — pre-existing, unrelated to `DependencyInjection.cs` or this merge. 0 errors. This confirms the combined DI registration graph compiles with no duplicate/ambiguous registration errors and no missing-type errors.

## Unit-test result

```
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release --no-restore
```
**Passed: 3382, Failed: 0, Skipped: 0** (39s).

## Architecture-test result

```
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release --no-restore
```
**Passed: 706, Failed: 0, Skipped: 0** (25s).

## Focused Leave/Attendance/WorkArea/TimeTracking test result

```
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Leave|FullyQualifiedName~Attendance|FullyQualifiedName~WorkArea|FullyQualifiedName~TimeTracking"
```
**Passed: 420, Failed: 0, Skipped: 0** (3s).

## Runtime DI / host-boot verification

The integration suite already contains a host-boot test, `tests/ONEVO.Tests.Integration/ApiBootTests.cs`, which boots the real API host (via `WebApplicationFactory`, including all hosted services such as `DevSmokeTestTenantSeeder`, `PermissionSeeder`, and now also `LeaveYearEndEntitlementJob`) against an ephemeral Testcontainers PostgreSQL instance. Docker was confirmed available in this environment, so it was run:

```
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ApiBootTests"
```
**Passed: 2, Failed: 0, Skipped: 0** (56s) — `HealthEndpoint_ReturnsOk` and `SwaggerEndpoint_ReturnsOk_InDevelopment`. This positively proves the full combined DI registration graph (Attendance/Work Area + all 26 Leave Management registrations + every other pre-existing registration) resolves at runtime with no missing-dependency or duplicate-registration exceptions during host startup.

## Skipped checks and exact reasons

- **Broader Leave/Attendance/Work Area integration test classes** (`LeaveRequestsIntegrationTests`, `LeaveCalendarIntegrationTests`, `LeaveEntitlementsAndBalancesIntegrationTests`, `LeaveBalanceAuditEndpointTests`, `LeavePoliciesIntegrationTests`, `LeaveTypesIntegrationTests`, `AttendanceCorrectionsIntegrationTests`, `WorkAreaChangeRequestRuntimeHttpIntegrationTests`, etc.) were **not run**. The task scope specifically called for the `ApiBootTests` host-boot/DI-resolution test "if available" as the runtime DI verification step, which was run and passed; the wider feature-behavior integration suites were out of scope for a DI-registration conflict resolution and were skipped to keep the change surface and verification focused on the actual conflict. They were not run due to scope, not due to any environment limitation (Docker was available and working, as `ApiBootTests` demonstrates).

## Confirmation: no commit or push performed

No `git commit` or `git push` command was run at any point in this session. The merge remains active (`.git/MERGE_HEAD` still present) and unresolved-except-for-the-one-file as required.

## Final `git status --short --branch`

```
## local/reporting-manager-run
M  docs/superpowers/plans/SUMMARY.md
M  docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md
A  docs/superpowers/plans/next/2026-08-21-leave-management/part-3-entitlements-and-balances.md
A  docs/superpowers/plans/next/2026-08-21-leave-management/part-4-request-submission.md
A  docs/superpowers/plans/next/2026-08-21-leave-management/part-5-approval-workflow.md
A  docs/superpowers/plans/next/2026-08-21-leave-management/part-6-cancellation.md
A  docs/superpowers/plans/next/2026-08-21-leave-management/part-7-team-calendar.md
A  docs/superpowers/plans/next/2026-08-21-leave-management/part-8-balance-audit-and-year-end.md
A  docs/superpowers/plans/next/2026-08-21-leave-management/part-9-hardening.md
M  docs/superpowers/plans/next/SUMMARY.md
A  src/ONEVO.Api/Contracts/Leave/Approvals/LeaveApprovalRequests.cs
A  src/ONEVO.Api/Contracts/Leave/Entitlements/AdjustEntitlementRequest.cs
A  src/ONEVO.Api/Contracts/Leave/Entitlements/CreateManualEntitlementRequest.cs
A  src/ONEVO.Api/Contracts/Leave/Entitlements/GenerateEntitlementsRequest.cs
A  src/ONEVO.Api/Contracts/Leave/Entitlements/RecalculateEntitlementRequest.cs
A  src/ONEVO.Api/Contracts/Leave/Requests/CancelLeaveRequestRequest.cs
A  src/ONEVO.Api/Contracts/Leave/Requests/SubmitLeaveRequestRequest.cs
A  src/ONEVO.Api/Controllers/Tenant/Leave/LeaveApprovalsController.cs
A  src/ONEVO.Api/Controllers/Tenant/Leave/LeaveBalanceAuditController.cs
A  src/ONEVO.Api/Controllers/Tenant/Leave/LeaveBalancesController.cs
A  src/ONEVO.Api/Controllers/Tenant/Leave/LeaveCalendarController.cs
A  src/ONEVO.Api/Controllers/Tenant/Leave/LeaveEntitlementsController.cs
A  src/ONEVO.Api/Controllers/Tenant/Leave/LeaveRequestsController.cs
A  src/ONEVO.Api/Filters/RequireAnyPermissionAttribute.cs
M  src/ONEVO.Api/appsettings.Development.json
M  src/ONEVO.Api/appsettings.json
M  src/ONEVO.Application/Common/RepositoryInterfaces/IEmployeeRepository.cs
M  src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs
M  src/ONEVO.Application/DependencyInjection.cs
M  src/ONEVO.Application/Features/CoreHr/EmployeeHierarchyClosure/RepositoryInterfaces/IEmployeeHierarchyClosureRepository.cs
M  src/ONEVO.Application/Features/DevPlatform/Tenancy/Commands/CreateTenant/CreateTenantCommandHandler.cs
A  src/ONEVO.Application/Features/Leave/Approval/Commands/ApproveLeaveRequestCommand.cs
A  src/ONEVO.Application/Features/Leave/Approval/Commands/LeaveApprovalDecisionService.cs
A  src/ONEVO.Application/Features/Leave/Approval/DTOs/Responses/LeaveApprovalResponses.cs
A  src/ONEVO.Application/Features/Leave/Approval/Helpers/LeaveApprovalMessages.cs
A  src/ONEVO.Application/Features/Leave/Approval/Helpers/LeaveApprovalModeEvaluator.cs
A  src/ONEVO.Application/Features/Leave/Approval/Mappers/LeaveApprovalMapper.cs
A  src/ONEVO.Application/Features/Leave/Approval/Options/LeaveApprovalOptions.cs
A  src/ONEVO.Application/Features/Leave/Approval/OutboxHandlers/LeaveApprovalOutboxPayloads.cs
A  src/ONEVO.Application/Features/Leave/Approval/Queries/LeaveApprovalQueries.cs
A  src/ONEVO.Application/Features/Leave/Approval/RepositoryInterfaces/ILeaveApprovalRepository.cs
A  src/ONEVO.Application/Features/Leave/Balance/DTOs/Responses/LeaveBalanceResponse.cs
A  src/ONEVO.Application/Features/Leave/Balance/Helpers/LeaveBalanceMapping.cs
A  src/ONEVO.Application/Features/Leave/Balance/Queries/GetMyBalances/GetMyBalancesQuery.cs
A  src/ONEVO.Application/Features/Leave/Balance/Queries/GetMyBalances/GetMyBalancesQueryHandler.cs
A  src/ONEVO.Application/Features/Leave/Balance/Queries/GetMyBalances/GetMyBalancesQueryValidator.cs
A  src/ONEVO.Application/Features/Leave/Balance/Queries/ListAllBalances/ListAllBalancesQuery.cs
A  src/ONEVO.Application/Features/Leave/Balance/Queries/ListAllBalances/ListAllBalancesQueryHandler.cs
A  src/ONEVO.Application/Features/Leave/Balance/Queries/ListAllBalances/ListAllBalancesQueryValidator.cs
A  src/ONEVO.Application/Features/Leave/Balance/Queries/ListTeamBalances/ListTeamBalancesQuery.cs
A  src/ONEVO.Application/Features/Leave/Balance/Queries/ListTeamBalances/ListTeamBalancesQueryHandler.cs
A  src/ONEVO.Application/Features/Leave/Balance/Queries/ListTeamBalances/ListTeamBalancesQueryValidator.cs
A  src/ONEVO.Application/Features/Leave/BalanceAudit/DTOs/Responses/LeaveBalanceAuditResponse.cs
A  src/ONEVO.Application/Features/Leave/BalanceAudit/DTOs/Responses/LeaveExportFile.cs
A  src/ONEVO.Application/Features/Leave/BalanceAudit/Helpers/LeaveBalanceAuditCsvBuilder.cs
A  src/ONEVO.Application/Features/Leave/BalanceAudit/Mappers/LeaveBalanceAuditMapper.cs
A  src/ONEVO.Application/Features/Leave/BalanceAudit/Queries/ListBalanceAudit/ListBalanceAuditQuery.cs
A  src/ONEVO.Application/Features/Leave/BalanceAudit/Queries/ListBalanceAudit/ListBalanceAuditQueryHandler.cs
A  src/ONEVO.Application/Features/Leave/BalanceAudit/RepositoryInterfaces/ILeaveBalanceAuditRepository.cs
A  src/ONEVO.Application/Features/Leave/Calendar/DTOs/Responses/LeaveCalendarResponses.cs
A  src/ONEVO.Application/Features/Leave/Calendar/Helpers/LeaveCalendarMessages.cs
A  src/ONEVO.Application/Features/Leave/Calendar/Helpers/LeaveCalendarMonthRange.cs
A  src/ONEVO.Application/Features/Leave/Calendar/Helpers/LeaveCalendarRequestProjector.cs
A  src/ONEVO.Application/Features/Leave/Calendar/Mappers/LeaveCalendarMapper.cs
A  src/ONEVO.Application/Features/Leave/Calendar/Options/LeaveCalendarOptions.cs
A  src/ONEVO.Application/Features/Leave/Calendar/Queries/GetLeaveCalendarQuery.cs
A  src/ONEVO.Application/Features/Leave/Calendar/RepositoryInterfaces/ILeaveCalendarRepository.cs
A  src/ONEVO.Application/Features/Leave/Calendar/Services/ILeaveCalendarHolidayProvider.cs
A  src/ONEVO.Application/Features/Leave/Calendar/Services/NoOpLeaveCalendarHolidayProvider.cs
A  src/ONEVO.Application/Features/Leave/Cancellation/Commands/CancelLeaveRequestCommand.cs
A  src/ONEVO.Application/Features/Leave/Cancellation/DTOs/Responses/CancelLeaveRequestResponse.cs
A  src/ONEVO.Application/Features/Leave/Cancellation/Helpers/LeaveBusinessDateResolver.cs
A  src/ONEVO.Application/Features/Leave/Cancellation/Helpers/LeaveCancellationClassifier.cs
A  src/ONEVO.Application/Features/Leave/Cancellation/Helpers/LeaveCancellationMessages.cs
A  src/ONEVO.Application/Features/Leave/Cancellation/Helpers/LeaveRequestDayAllocationBuilder.cs
A  src/ONEVO.Application/Features/Leave/Cancellation/Mappers/LeaveCancellationMapper.cs
A  src/ONEVO.Application/Features/Leave/Cancellation/Options/LeaveCancellationOptions.cs
A  src/ONEVO.Application/Features/Leave/Cancellation/Outbox/LeaveRequestCancelledPayload.cs
A  src/ONEVO.Application/Features/Leave/Cancellation/RepositoryInterfaces/ILeaveCancellationRepository.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Commands/AdjustEntitlement/AdjustEntitlementCommand.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Commands/AdjustEntitlement/AdjustEntitlementCommandHandler.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Commands/AdjustEntitlement/AdjustEntitlementCommandValidator.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Commands/CreateManualEntitlement/CreateManualEntitlementCommand.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Commands/CreateManualEntitlement/CreateManualEntitlementCommandHandler.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Commands/CreateManualEntitlement/CreateManualEntitlementCommandValidator.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Commands/GenerateEntitlements/GenerateEntitlementsCommand.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Commands/GenerateEntitlements/GenerateEntitlementsCommandHandler.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Commands/GenerateEntitlements/GenerateEntitlementsCommandValidator.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Commands/RecalculateEntitlement/RecalculateEntitlementCommand.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Commands/RecalculateEntitlement/RecalculateEntitlementCommandHandler.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/DTOs/Responses/LeaveEntitlementResponse.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Helpers/ILeaveWorkingDayCounter.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveEntitlementCalculator.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveEntitlementGenerationCsvBuilder.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveEntitlementMessages.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveEntitlementPlanner.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveEntitlementYearRules.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveWorkingDayCounter.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Mappers/LeaveEntitlementMapper.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Options/LeaveEntitlementYearOptions.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Queries/ListEntitlements/ListEntitlementsQuery.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Queries/ListEntitlements/ListEntitlementsQueryHandler.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Queries/ListEntitlements/ListEntitlementsQueryValidator.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Queries/PreviewGenerateEntitlements/PreviewGenerateEntitlementsQuery.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Queries/PreviewGenerateEntitlements/PreviewGenerateEntitlementsQueryHandler.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/Queries/PreviewGenerateEntitlements/PreviewGenerateEntitlementsQueryValidator.cs
A  src/ONEVO.Application/Features/Leave/Entitlement/RepositoryInterfaces/ILeaveEntitlementRepository.cs
M  src/ONEVO.Application/Features/Leave/Policy/RepositoryInterfaces/ILeavePolicyRepository.cs
A  src/ONEVO.Application/Features/Leave/Request/Commands/SubmitLeaveRequest/SubmitLeaveRequestCommand.cs
A  src/ONEVO.Application/Features/Leave/Request/Commands/SubmitLeaveRequest/SubmitLeaveRequestCommandHandler.cs
A  src/ONEVO.Application/Features/Leave/Request/Commands/SubmitLeaveRequest/SubmitLeaveRequestCommandValidator.cs
A  src/ONEVO.Application/Features/Leave/Request/DTOs/Responses/LeaveRequestResponse.cs
A  src/ONEVO.Application/Features/Leave/Request/Helpers/LeaveRequestDayCalculator.cs
A  src/ONEVO.Application/Features/Leave/Request/Helpers/LeaveRequestMessages.cs
A  src/ONEVO.Application/Features/Leave/Request/Mappers/LeaveRequestMapper.cs
A  src/ONEVO.Application/Features/Leave/Request/Options/LeaveRequestOptions.cs
A  src/ONEVO.Application/Features/Leave/Request/Queries/ListMyLeaveRequests/ListMyLeaveRequestsQuery.cs
A  src/ONEVO.Application/Features/Leave/Request/Queries/ListMyLeaveRequests/ListMyLeaveRequestsQueryHandler.cs
A  src/ONEVO.Application/Features/Leave/Request/Queries/PreviewSubmitLeaveRequest/PreviewSubmitLeaveRequestQuery.cs
A  src/ONEVO.Application/Features/Leave/Request/Queries/PreviewSubmitLeaveRequest/PreviewSubmitLeaveRequestQueryHandler.cs
A  src/ONEVO.Application/Features/Leave/Request/RepositoryInterfaces/ILeaveRequestRepository.cs
A  src/ONEVO.Application/Features/Leave/Request/Services/ILeaveApproverResolver.cs
A  src/ONEVO.Application/Features/Leave/Request/Services/ILeaveHolidayProvider.cs
A  src/ONEVO.Application/Features/Leave/Request/Services/ILeaveRequestConflictProvider.cs
A  src/ONEVO.Application/Features/Leave/Request/Services/LeaveRequestSubmissionEvaluator.cs
A  src/ONEVO.Application/Features/Leave/Request/Services/LeaveTeamAbsenceWarningService.cs
M  src/ONEVO.Domain/Features/Leave/Common/LeaveVocabularies.cs
A  src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequestDayAllocation.cs
A  src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequestInfoMessage.cs
M  src/ONEVO.Infrastructure/DependencyInjection.cs
A  src/ONEVO.Infrastructure/Migrations/20260822094213_AddLeaveRequestInfoMessages.Designer.cs
A  src/ONEVO.Infrastructure/Migrations/20260822094213_AddLeaveRequestInfoMessages.cs
A  src/ONEVO.Infrastructure/Migrations/20260822101953_AddLeaveRequestDayAllocations.Designer.cs
A  src/ONEVO.Infrastructure/Migrations/20260822101953_AddLeaveRequestDayAllocations.cs
M  src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs
M  src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs
M  src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeaveRequestConfiguration.cs
M  src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeHierarchyClosureRepository.cs
M  src/ONEVO.Infrastructure/Persistence/Repositories/EfEmployeeRepository.cs
A  src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Approval/EfLeaveApprovalRepository.cs
A  src/ONEVO.Infrastructure/Persistence/Repositories/Leave/BalanceAudit/EfLeaveBalanceAuditRepository.cs
A  src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Calendar/EfLeaveCalendarRepository.cs
A  src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Cancellation/EfLeaveCancellationRepository.cs
A  src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Entitlement/EfLeaveEntitlementRepository.cs
M  src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Policy/EfLeavePolicyRepository.cs
A  src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Request/EfLeaveRequestRepository.cs
M  src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs
M  src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs
A  src/ONEVO.Infrastructure/Services/Leave/LeaveYearEndEntitlementJob.cs
A  tests/ONEVO.Tests.Architecture/LeaveApprovalsControllerArchitectureTests.cs
A  tests/ONEVO.Tests.Architecture/LeaveBalanceAuditControllerArchitectureTests.cs
A  tests/ONEVO.Tests.Architecture/LeaveBalancesControllerArchitectureTests.cs
A  tests/ONEVO.Tests.Architecture/LeaveCalendarControllerArchitectureTests.cs
A  tests/ONEVO.Tests.Architecture/LeaveEntitlementsControllerArchitectureTests.cs
A  tests/ONEVO.Tests.Architecture/LeaveRequestsControllerArchitectureTests.cs
A  tests/ONEVO.Tests.Architecture/LeaveTypesControllerArchitectureTests.cs
A  tests/ONEVO.Tests.Integration/Features/Leave/LeaveBalanceAuditEndpointTests.cs
A  tests/ONEVO.Tests.Integration/Features/Leave/LeaveCalendarIntegrationTests.cs
A  tests/ONEVO.Tests.Integration/Features/Leave/LeaveEntitlementsAndBalancesIntegrationTests.cs
A  tests/ONEVO.Tests.Integration/Features/Leave/LeaveRequestsIntegrationTests.cs
A  tests/ONEVO.Tests.Unit/Api/Filters/RequireAnyPermissionAttributeTests.cs
M  tests/ONEVO.Tests.Unit/Features/Auth/NotificationTemplateSeederTests.cs
M  tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityTestGraph.cs
M  tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeHierarchyClosure/EfEmployeeHierarchyClosureRepositoryTests.cs
M  tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Approval/BulkLeaveApprovalCommandHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalDecisionServiceTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalMapperTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalModeEvaluatorTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalOutboxRegistrationTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalQueryHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalsControllerPermissionTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Balance/GetMyBalancesQueryHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Balance/LeaveBalanceMappingPerfTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Balance/LeaveBalancesControllerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Balance/ListAllBalancesQueryHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Balance/ListTeamBalancesQueryHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/BalanceAudit/LeaveBalanceAuditCsvBuilderTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/BalanceAudit/ListBalanceAuditQueryHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Calendar/EfLeaveCalendarRepositoryTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Calendar/GetLeaveCalendarQueryHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveCalendarControllerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveCalendarMapperTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveCalendarMonthRangeTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveCalendarOptionsTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveCalendarRequestProjectorTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Calendar/NoOpLeaveCalendarHolidayProviderTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/CancelLeaveRequestCommandHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/EfLeaveCancellationRepositoryTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveBusinessDateResolverTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationClassifierTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationControllerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationMapperTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationOptionsTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationOutboxTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationVocabularyTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveRequestDayAllocationBuilderTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/AdjustEntitlementCommandHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/CreateManualEntitlementCommandHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/EfLeaveEmployeeLookupTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/EfLeaveEntitlementRepositoryTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/GenerateEntitlementsCommandHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/LeaveEntitlementCalculatorTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/LeaveEntitlementGenerationCsvBuilderTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/LeaveEntitlementsControllerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/ListEntitlementsQueryHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/PreviewGenerateEntitlementsQueryHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/RecalculateEntitlementCommandHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/LeaveYearEndEntitlementJobTests.cs
M  tests/ONEVO.Tests.Unit/Features/Leave/Policy/EfLeavePolicyRepositoryTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveApproverResolverTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveRequestDayCalculatorTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveRequestMapperTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveRequestSubmissionEvaluatorTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveRequestsControllerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveTeamAbsenceWarningServiceTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Request/ListMyLeaveRequestsQueryHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Request/SubmitLeaveRequestCommandHandlerTests.cs
A  tests/ONEVO.Tests.Unit/Features/Leave/Type/GetLeaveTypeQueryHandlerTests.cs
M  tests/ONEVO.Tests.Unit/Features/Tenancy/CreateTenantCommandHandlerTests.cs
M  tests/ONEVO.Tests.Unit/Features/Tenancy/SubscriptionTrialAndGracePeriodTests.cs
```

`git diff --name-only --diff-filter=U` returns nothing — the merge has no remaining unmerged paths. `src/ONEVO.Infrastructure/DependencyInjection.cs` shows as `M` (staged, resolved), not `UU`.
