# Leave Policy Cleanup — Part 1: Model, Versions, Assignments, Resolver (backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Leave types carry no numbers; each leave policy covers one leave type, is versioned with an "apply from" date, and is assigned to company / department / position / employee with per-leave-type most-specific-wins resolution.

**Architecture:** `LeavePolicy` becomes a stable identity (one `LeaveTypeId`) with immutable `LeavePolicyVersion` rows holding every rule. `LeavePolicyAssignment` (target kind + id, unique per target per leave type) replaces `LeavePolicyLegalEntity`. A single `ILeavePolicyResolver` answers "which policy version applies to employee E for leave type T on date D, and from which level" — every later consumer (crediting, requests, employee screen) goes through it.

**Tech Stack:** .NET 9, EF Core + Npgsql (PostgreSQL, RLS), MediatR, FluentValidation, xUnit + FluentAssertions + NSubstitute (match neighbouring tests in `tests/ONEVO.Tests.Unit/Features/Leave`).

**Spec:** `docs/superpowers/specs/next/2026-10-20-leave-type-policy-cleanup-design.md` — read it first.

**Parts:** Part 1 (this) → Part 2 `part-2-crediting-requests.md` (backend) → Part 3 frontend plan in `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-10-20-leave-policy-cleanup-frontend.md`.

## Global Constraints

- Work on the current branch in `C:\onevoNew\HRMS-Backend-v1`; **no git worktrees**. The working tree is shared with other sessions — stage only files you touched (`git add <paths>`), never `git add -A`.
- No `.sln`: build `dotnet build src/ONEVO.Api`, test `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Leave"`. `ONEVO.Api.exe` dev server auto-respawns and locks DLLs — kill it immediately before each build/test.
- Migrations: `dotnet ef migrations add <Name> --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`. Every new tenant table needs RLS coverage the same way recent tables do (look at a recent `*RlsPolicyCoverage` migration and copy its pattern).
- Amounts are **hours** (`decimal`). Notice stays **days** (`int`). No field named `*Days` may describe an amount.
- Vocabulary strings (snake_case, exactly): amount periods `month`, `year`; eligibility `immediately`, `after_probation`, `after_months`; target kinds `legal_entity`, `department`, `position`, `employee`; approval modes `any_one`, `all_must_approve`, `in_order`, `none`.
- Resolution order per leave type: `employee` → `position` → `department` (walk up `ParentDepartmentId`, nearest wins) → `legal_entity` → none.
- Version in force on date D = greatest `EffectiveFrom <= D`. New versions: `EffectiveFrom >= today` (tenant-local "today" via the existing `IClock`/`_clock.Today` used by leave code). Only versions with `EffectiveFrom > today` may be deleted.
- A policy with any assignment cannot be deactivated.

## Review Focus

- **Two policies for the same leave type on one target** — must be impossible (unique index) and the API must answer with a conflict listing the existing policy unless `replace = true`. Test in Task 4.
- **Employee whose primary position has ended / has no department / department has no assignment but its parent does** — resolver must fall through correctly. Tests in Task 5.
- **Version effective today vs tomorrow** — on the day a new version starts, the resolver must return it; the day before, the old one. Test in Task 5.
- **Assigning a policy whose leave type is inactive or a policy that is inactive** — reject with a validation error. Test in Task 4.
- **Migration on a tenant whose old policy covers several leave types and two legal entities** — produces one policy per type, version 1, and legal-entity assignments without unique-index violations. Test in Task 2.

---

### Task 1: Slim leave type

**Files:**
- Modify: `src/ONEVO.Domain/Features/Leave/Type/Entities/LeaveType.cs`
- Modify: `src/ONEVO.Domain/Features/Leave/Common/LeaveVocabularies.cs` (delete `LeaveTypeCategories`)
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeaveTypeConfiguration.cs`
- Modify: `src/ONEVO.Application/Features/Leave/Type/**` (Create/Update commands, validators, handlers, `LeaveTypeResponse`, `LeaveTypeMapper`)
- Modify: `src/ONEVO.Api/Contracts/Leave/Types/CreateLeaveTypeRequest.cs`, `UpdateLeaveTypeRequest.cs`
- Modify: `src/ONEVO.Application/Features/Leave/Calendar/**` — calendar colour comes from `LeaveType.Color`; delete `LeaveCalendarOptions.TypeCategoryColors` and its config section.
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Type/*` (update), `tests/ONEVO.Tests.Unit/Features/Leave/Calendar/*` (update)

**Interfaces:**
- Produces: `LeaveType { Id, TenantId, Name, Code, Description, Color, IsPaid, ApplicableGender, RequiresDocument, DocumentRequiredAfterHours (decimal?), AcceptedDocumentTypes, IsActive, CreatedAt, UpdatedAt }`. `LeaveTypeResponse(Id, Name, Code, Description, Color, IsPaid, ApplicableGender, RequiresDocument, DocumentRequiredAfterHours, AcceptedDocumentTypes, IsActive, PolicyCount)`.

- [ ] **Step 1: Update tests first.** In the Create/Update validator tests replace category/day assertions with:
  - `Color_must_be_hex` — `"#12AB9F"` valid, `"red"` and `""` invalid.
  - `DocumentRequiredAfterHours_must_be_positive_when_set` — `0` invalid, `null` valid, `8` valid.
  - Handler test `Create_persists_only_kind_fields` asserting the saved entity has `Color`, `IsPaid`, `ApplicableGender` from the command.
  - Calendar test: an item's colour equals its leave type's `Color`.
- [ ] **Step 2: Run** `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Leave.Type|FullyQualifiedName~Leave.Calendar"` — expect compile failures.
- [ ] **Step 3: Implement.** Remove `Category`, `RequiresApproval`, `MaxConsecutiveDays`, `DefaultDaysPerYear`, `CarryForwardAllowed`, `MaxCarryForwardDays`, `CarryForwardExpiryMonths`, `ProRataForNewJoiners`, `MinimumNoticeDays`, `DocumentRequiredAfterDays`; add `Color` (string, required, max 9, default `"#3B82F6"`) and `DocumentRequiredAfterHours`. `PolicyCount` = count of active `LeavePolicy` rows with that `LeaveTypeId` (fill it once Task 2 exists; until then return 0 — Task 2 Step 5 wires it). Fix every compile error the removed members cause. Inside Entitlement/Request/Approval code that Part 2 rewrites, apply the **stub rule** (below).

> **Stub rule (applies to Tasks 1–2):** where Entitlement, Request or Approval code no longer compiles because of removed members, replace only the broken expression or method body with `throw new NotImplementedException("Leave cleanup Part 2 Task <n>")` and mark the tests that exercise it `[Fact(Skip = "Leave cleanup Part 2 Task <n>")]`. Record each stub in the commit message. Part 2 removes every stub; `grep -rn "Leave cleanup Part 2" src tests` must be empty at the end of Part 2. The migration is generated in Task 2 together with the policy schema — do not add one here.
- [ ] **Step 4: Run** the same tests — PASS.
- [ ] **Step 5: Commit** `feat(leave): leave type holds only the kind of leave`.

### Task 2: Policy schema, versions, assignments, migration

**Files:**
- Modify: `src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicy.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicyVersion.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicyAssignment.cs`
- Modify: `src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicyBlackoutPeriod.cs` (`LeavePolicyId` → `LeavePolicyVersionId`)
- Delete: `LeavePolicyLeaveType.cs`, `LeavePolicyLegalEntity.cs`
- Modify: `src/ONEVO.Domain/Features/Leave/Common/LeaveVocabularies.cs` — delete `LeaveAccrualStarts`, `LeaveAccrualMethods`, `LeaveProrationMethods`; add `LeaveAmountPeriods`, `LeaveEligibility`, `LeavePolicyTargetKinds`; add `None = "none"` to `LeaveApprovalModes` (and to `All`).
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeavePolicyConfiguration.cs`
- Create: migration `LeavePolicyPerTypeVersionsAssignments` (+ RLS coverage migration for the two new tables)
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/DapiLeaveSampleSeeder.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Common/LeaveVocabulariesTests.cs`, `tests/ONEVO.Tests.Integration/Features/Leave/LeavePolicyMigrationTests.cs` (create)

**Interfaces:**
- Produces:
  - `LeavePolicy { Id, TenantId, LeaveTypeId, Name, Description, IsActive, CreatedAt, UpdatedAt }`
  - `LeavePolicyVersion { Id, TenantId, LeavePolicyId, VersionNumber (int), EffectiveFrom (DateOnly), AmountHours (decimal), AmountPeriod (string), CarryForwardCapHours (decimal?), CarryForwardExpiryMonths (int?), EligibleFrom (string), EligibleAfterMonths (int?), MinimumNoticeDays (int), MinRequestHours (decimal), MaxRequestHours (decimal?), MaxTeamAbsencePercent (decimal?), ApprovalMode (string), CreatedAt, CreatedBy (Guid?) }` — unique (`LeavePolicyId`, `VersionNumber`) and (`LeavePolicyId`, `EffectiveFrom`).
  - `LeavePolicyAssignment { Id, TenantId, LeavePolicyId, LeaveTypeId, TargetKind, TargetId, CreatedAt, CreatedBy }` — unique (`TenantId`, `TargetKind`, `TargetId`, `LeaveTypeId`).
  - Table names: `leave_policy_versions`, `leave_policy_assignments`.

- [ ] **Step 1: Write failing tests.**
  - `LeaveVocabulariesTests`: `LeaveApprovalModes.All` contains `"none"`; `LeaveAmountPeriods.All == ["month","year"]`; `LeaveEligibility.All == ["immediately","after_probation","after_months"]`; `LeavePolicyTargetKinds.All == ["legal_entity","department","position","employee"]`.
  - Integration `Migration_splits_multi_type_policy`: seed (via raw SQL before the new migration, using the integration test DB helper the other `Features/Leave` integration tests use) one old policy "SL Policy" with leave types Annual (14 days, carry max 5, expiry 3) and Sick (7 days), `accrual_method = 'monthly'`, linked to two legal entities whose work window is 09:00–17:00 with 60 min break (7 h). After migrating, assert: policies "SL Policy · Annual Leave" and "SL Policy · Sick Leave" exist; Annual v1 `AmountPeriod = "month"`, `AmountHours = 14*7/12` rounded to 2 dp (`8.17`), `CarryForwardCapHours = 35`, `CarryForwardExpiryMonths = 3`; each policy has two `legal_entity` assignments; v1 `EffectiveFrom` = old `effective_from`. If the integration harness cannot run a migration from a mid-point, write this as a unit test of the static SQL-builder/converter instead (see Step 3) and say so in the commit message.
- [ ] **Step 2: Run** — fail.
- [ ] **Step 3: Implement entities + configuration + migration.** Data conversion runs in the migration `Up` as SQL, in this order: create new tables → insert one `leave_policies` row per old `leave_policy_leave_types` row (new id, name `"<policy> · <type>"`, `leave_type_id`) → insert v1 version per new policy (hours factor = work-day hours of the first linked legal entity, computed in SQL from `work_start_time`, `work_end_time`, `break_duration_minutes`, fallback 8; `amount_period = 'month'` and amount ÷ 12 when old `accrual_method = 'monthly'`, else `'year'`; `eligible_from` = `after_probation` if old `probation_restriction` or `accrual_start = 'after_probation'`, `after_months` with `max(accrual_after_n_months, minimum_tenure_months)` if either > 0, else `immediately`; `min_request_hours = min_days_per_request × factor`; `max_request_hours = max_consecutive_days × factor`; copy notice/team %/approval mode) → re-point blackout periods to the new versions (duplicate per split policy) → insert `legal_entity` assignments with `ON CONFLICT DO NOTHING` → drop old columns/tables. `Down` throws `NotSupportedException` (one-way; data is dev/seed only). Current-year `leave_entitlements` are left untouched here (Part 2 Task 2 adds the policy columns there). Update `DapiLeaveSampleSeeder` to create single-type policies + v1 + `legal_entity` assignment. Apply the stub rule to `LeaveEntitlementPlanner`, `RecalculateEntitlementCommandHandler`, `LeaveRequestSubmissionEvaluator`, `ILeaveApprovalRepository`/`EfLeaveApprovalRepository` approval-mode lookup, and any other consumer of the deleted policy types.
- [ ] **Step 4: Apply locally** `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`, then run the tests — PASS.
- [ ] **Step 5: Wire `LeaveTypeResponse.PolicyCount`** from Task 1 (repository count query) and commit `feat(leave): one policy per leave type with versions and assignments`.

### Task 3: Policy rules validation + create policy

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Policy/Commands/Common/LeavePolicyRulesInput.cs`, `LeavePolicyRulesValidator.cs`
- Rewrite: `src/ONEVO.Application/Features/Leave/Policy/Commands/CreateLeavePolicy/*`
- Rewrite: `src/ONEVO.Application/Features/Leave/Policy/RepositoryInterfaces/ILeavePolicyRepository.cs`, `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Policy/EfLeavePolicyRepository.cs`
- Modify: `src/ONEVO.Api/Contracts/Leave/Policies/CreateLeavePolicyRequest.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Policy/CreateLeavePolicyCommandValidatorTests.cs`, `CreateLeavePolicyCommandHandlerTests.cs` (rewrite)

**Interfaces:**
- Produces:
  - `record LeavePolicyRulesInput(decimal AmountHours, string AmountPeriod, decimal? CarryForwardCapHours, int? CarryForwardExpiryMonths, string EligibleFrom, int? EligibleAfterMonths, int MinimumNoticeDays, decimal MinRequestHours, decimal? MaxRequestHours, decimal? MaxTeamAbsencePercent, string ApprovalMode, IReadOnlyList<BlackoutInput> Blackouts)`; `record BlackoutInput(string Name, DateOnly StartDate, DateOnly EndDate)`.
  - `record LeavePolicyAssignmentInput(string TargetKind, Guid TargetId)`.
  - `CreateLeavePolicyCommand(Guid LeaveTypeId, string Name, string? Description, LeavePolicyRulesInput Rules, IReadOnlyList<LeavePolicyAssignmentInput> Assignments, bool ReplaceConflicts) : IRequest<Result<Guid>>` — v1 `EffectiveFrom = today`.
  - Conflict result: `Result.Failure` with error code `leave_policy.assignment_conflict` and payload `IReadOnlyList<LeavePolicyAssignmentConflict(string TargetKind, Guid TargetId, string TargetName, Guid ExistingPolicyId, string ExistingPolicyName)>` (follow how `LeavePolicyConflictMessages` + controller currently return the legal-entity conflict, and keep the HTTP status it uses).
  - Repository: `AddPolicyAsync(LeavePolicy, LeavePolicyVersion, IReadOnlyList<LeavePolicyBlackoutPeriod>, IReadOnlyList<LeavePolicyAssignment> add, IReadOnlyList<Guid> assignmentIdsToRemove, ct)`, `ListAssignmentConflictsAsync(tenantId, leaveTypeId, IReadOnlyList<LeavePolicyAssignmentInput>, Guid? excludingPolicyId, ct)`, `TargetsExistAsync(tenantId, IReadOnlyList<LeavePolicyAssignmentInput>, ct) -> IReadOnlyList<LeavePolicyAssignmentInput> missing`.

- [ ] **Step 1: Failing validator tests** (`LeavePolicyRulesValidator`): `AmountHours > 0` and `<= 8784`; `AmountPeriod` in vocabulary; `CarryForwardCapHours >= 0` when set; `CarryForwardExpiryMonths` 1–24 when set; `EligibleAfterMonths` required 1–60 iff `after_months`, null otherwise; `MinimumNoticeDays >= 0`; `MinRequestHours > 0`; `MaxRequestHours >= MinRequestHours` when set; `MaxTeamAbsencePercent` 1–100 when set; `ApprovalMode` in vocabulary incl. `none`; blackout `EndDate >= StartDate`. Command validator: `Name` required ≤ 150, assignments have valid kind, no duplicate (kind,id) pairs.
- [ ] **Step 2: Failing handler tests:** `Create_writes_policy_v1_and_assignments` (v1 `VersionNumber = 1`, `EffectiveFrom = today`); `Create_rejects_inactive_leave_type`; `Create_returns_conflict_when_target_already_has_policy_for_type` (no writes); `Create_with_replace_removes_existing_assignment_and_adds_new`; `Create_rejects_unknown_target`; `Create_rejects_duplicate_name` (existing behaviour, keep).
- [ ] **Step 3: Run** — fail. **Step 4: Implement**, update controller `Create` to map the new request. **Step 5: Run** — PASS. **Step 6: Commit** `feat(leave): create single-type policy with assignments`.

### Task 4: Versions, assignments, activation commands

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Policy/Commands/AddLeavePolicyVersion/*`, `DeleteLeavePolicyVersion/*`, `SetLeavePolicyAssignments/*`, `SetLeavePolicyActive/*`
- Modify: `CloneLeavePolicy/*` (clone = new policy, same leave type, copy of the version in force today as v1, no assignments)
- Modify: `src/ONEVO.Api/Controllers/Tenant/Leave/LeavePoliciesController.cs` + contracts
- Test: one test class per command under `tests/ONEVO.Tests.Unit/Features/Leave/Policy/`

**Interfaces:**
- Produces:
  - `AddLeavePolicyVersionCommand(Guid PolicyId, DateOnly ApplyFrom, LeavePolicyRulesInput Rules) : IRequest<Result<Guid>>` → `POST /leave-policies/{id}/versions`. Rejects `ApplyFrom < today` and an existing version with the same `EffectiveFrom` (if that version is still future, it is replaced instead — handler deletes then inserts). `VersionNumber = max + 1`.
  - `DeleteLeavePolicyVersionCommand(Guid PolicyId, Guid VersionId)` → `DELETE /leave-policies/{id}/versions/{versionId}`; only `EffectiveFrom > today`; never the only version.
  - `SetLeavePolicyAssignmentsCommand(Guid PolicyId, IReadOnlyList<LeavePolicyAssignmentInput> Assignments, bool ReplaceConflicts)` → `PUT /leave-policies/{id}/assignments`; replaces this policy's whole set; same conflict result as Task 3; rejects when policy inactive.
  - `SetLeavePolicyActiveCommand(Guid PolicyId, bool IsActive)` → `POST /leave-policies/{id}/activate|deactivate`; deactivate fails with `leave_policy.has_assignments` when any assignment exists.
  - All write endpoints `[RequirePermission("leave:manage")]`.

- [ ] **Step 1: Failing tests** — `AddVersion_rejects_past_apply_from`; `AddVersion_increments_version_number`; `AddVersion_replaces_future_version_with_same_date`; `AddVersion_copies_blackouts_from_rules_input`; `DeleteVersion_rejects_effective_version`; `DeleteVersion_rejects_only_version`; `SetAssignments_conflict_without_replace_writes_nothing`; `SetAssignments_replace_moves_target_to_this_policy`; `SetAssignments_rejects_inactive_policy`; `Deactivate_rejects_when_assigned`; `Clone_copies_version_in_force_without_assignments`.
- [ ] **Step 2: Run** — fail. **Step 3: Implement.** **Step 4: Run** — PASS. **Step 5: Commit** `feat(leave): policy versions, assignments and activation`.

### Task 5: Policy resolver

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Policy/Services/ILeavePolicyResolver.cs`, `LeavePolicyResolver.cs`
- Create: `src/ONEVO.Application/Features/Leave/Policy/RepositoryInterfaces/ILeavePolicyResolutionRepository.cs` + `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Policy/EfLeavePolicyResolutionRepository.cs`
- Register both in the Application/Infrastructure DI where other leave services are registered.
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Policy/LeavePolicyResolverTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record ResolvedLeavePolicy(
      Guid LeaveTypeId, Guid PolicyId, string PolicyName,
      LeavePolicyVersion Version, IReadOnlyList<LeavePolicyBlackoutPeriod> Blackouts,
      string SourceKind, Guid SourceTargetId, string SourceTargetName);

  public interface ILeavePolicyResolver
  {
      Task<ResolvedLeavePolicy?> ResolveAsync(Guid tenantId, Guid employeeId, Guid leaveTypeId, DateOnly asOf, CancellationToken ct);
      Task<IReadOnlyList<ResolvedLeavePolicy>> ResolveAllAsync(Guid tenantId, Guid employeeId, DateOnly asOf, CancellationToken ct);
      Task<IReadOnlyDictionary<Guid, IReadOnlyList<ResolvedLeavePolicy>>> ResolveForEmployeesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly asOf, CancellationToken ct);
  }
  ```
  Batch method loads assignments/versions/department tree once (no per-employee queries) — used by Part 2 jobs. Only active policies and active leave types resolve. Position = the employee's `PositionAssignment` with `AssignmentKind = PrimaryEmployment`, status active, `EffectiveFrom <= asOf` and (`EffectiveTo` null or `>= asOf`).

- [ ] **Step 1: Failing tests** (in-memory fake of `ILeavePolicyResolutionRepository`):
  - `Company_has_annual_and_sick_position_has_casual_and_sick` → Annual from `legal_entity`, Casual and Sick from `position` (the user's example, verbatim).
  - `Employee_override_beats_position`.
  - `Department_parent_assignment_applies_to_child_department`; `Nearest_department_wins_over_parent`.
  - `Ended_position_falls_through_to_department`.
  - `No_assignment_returns_null`.
  - `Version_switches_on_effective_date` (v2 effective 2026-11-01: asOf 2026-10-31 → v1, 2026-11-01 → v2).
  - `Inactive_leave_type_does_not_resolve` (inactive policies can't hold assignments, so they never reach the resolver).
  - `Batch_resolution_matches_single_resolution` for 3 employees.
- [ ] **Step 2: Run** — fail. **Step 3: Implement.** **Step 4: Run** — PASS. **Step 5: Commit** `feat(leave): per-leave-type policy resolver`.

### Task 6: Policy queries + employee policy endpoints

**Files:**
- Rewrite: `Policy/Queries/ListLeavePolicies/*`, `Policy/Queries/GetLeavePolicy/*`, `Policy/DTOs/Responses/LeavePolicyResponse.cs`, `Policy/Mappers/LeavePolicyMapper.cs`
- Create: `src/ONEVO.Application/Features/Leave/Policy/Queries/GetEmployeeLeavePolicies/*`, `Commands/SetEmployeeLeavePolicy/*`, `Commands/ResetEmployeeLeavePolicy/*`
- Create: `src/ONEVO.Api/Controllers/Tenant/Leave/EmployeeLeavePoliciesController.cs` (route `employees/{employeeId:guid}/leave-policies`)
- Test: matching test classes under `tests/ONEVO.Tests.Unit/Features/Leave/Policy/`, update `LeavePoliciesControllerTests.cs`

**Interfaces:**
- Produces:
  - `LeavePolicyListItem(Guid Id, string Name, Guid LeaveTypeId, string LeaveTypeName, string LeaveTypeColor, decimal AmountHours, string AmountPeriod, int CurrentVersionNumber, DateOnly? NextVersionEffectiveFrom, int AssignmentCount, IReadOnlyList<string> AssignedToSummary, bool IsActive)` — `AssignedToSummary` like `["Whole company: Dapi Technologies", "2 departments", "1 position", "3 employees"]`.
  - `LeavePolicyResponse(Guid Id, string Name, string? Description, Guid LeaveTypeId, string LeaveTypeName, bool IsActive, LeavePolicyVersionResponse Current, IReadOnlyList<LeavePolicyVersionResponse> Versions, IReadOnlyList<LeavePolicyAssignmentResponse> Assignments)`; `LeavePolicyVersionResponse(Guid Id, int VersionNumber, DateOnly EffectiveFrom, bool IsFuture, <all rule fields>, IReadOnlyList<BlackoutResponse> Blackouts, DateTimeOffset CreatedAt, string? CreatedByName)`; `LeavePolicyAssignmentResponse(Guid Id, string TargetKind, Guid TargetId, string TargetName)`. Employee assignments are listed too.
  - `GET employees/{id}/leave-policies` → `IReadOnlyList<EmployeeLeavePolicyRow(Guid LeaveTypeId, string LeaveTypeName, string LeaveTypeColor, Guid? PolicyId, string? PolicyName, string? SourceKind, string? SourceName, decimal? AmountHours, string? AmountPeriod, bool IsOverride)>` — one row per **active** leave type (rows with null policy = "No policy"). Permission: `leave:read` or the employee themself (follow the existing `RequireVisibleEmployee` filter used by employee endpoints).
  - `PUT employees/{id}/leave-policies/{leaveTypeId}` body `{ policyId }` → upserts the `employee` assignment (policy must be active and for that leave type). `DELETE …/{leaveTypeId}` removes the employee assignment. Both require `leave:manage` **or** `leave:create` — use whatever multi-permission form `RequirePermission` supports (check its attribute; if it only takes one, add an `AnyPermission` overload following its existing pattern) and `RequireVisibleEmployee`.

- [ ] **Step 1: Failing tests** — `List_shows_amount_and_assignment_summary`; `Get_returns_versions_newest_first_with_is_future`; `EmployeePolicies_returns_row_per_active_type_with_source`; `SetEmployeePolicy_rejects_policy_for_other_leave_type`; `SetEmployeePolicy_upserts_override`; `ResetEmployeePolicy_removes_override_only`; controller test that `PUT` accepts `leave:create`.
- [ ] **Step 2: Run** — fail. **Step 3: Implement.** **Step 4: Run** full leave filter — PASS (fix any remaining Part-1 compile stubs that are now resolvable). **Step 5: Commit** `feat(leave): policy queries and employee policy overrides`.
