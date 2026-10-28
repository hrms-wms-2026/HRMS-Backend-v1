# Employee Management — Phase 1 Foundation: Backend Implementation Report

**Scope delivered:** position assignments + reporting-manager derivation, employee read API (list + detail) with coverage-based visibility, and a pre-invite onboarding-draft save/resume flow. Final employee creation, invitation sending, and account provisioning are **not implemented** — see [Blockers and unresolved dependencies](#blockers-and-unresolved-dependencies).

**Branch:** `feature/employee-management-phase1-foundation`, commits `cd5e56f..dad387d` (16 commits).

---

## Blockers and unresolved dependencies (read this first)

These were investigated, not guessed around. Each is the reason a corresponding piece of the original spec is deliberately absent or stubbed.

1. **No purchased-seat source exists in the codebase.** `tenant_subscriptions` / `subscription_plans` carry `company_size_range`, pricing, and module selection, but no numeric "purchased seats" or "included seats" field. The onboarding-doc rule ("no seat → block as Draft") and the billing-doc rule ("monthly tenants may add users beyond purchased seats, extra seats billed next invoice") cannot be reconciled without that field — reconciling them would require guessing which rule wins. Per instruction, this was **not guessed**. `ISeatEntitlementService`/`SeatEntitlementService` always returns `SeatDecisionStatus.Undetermined`, never infers from `CompanySizeRange` or any other proxy. Every draft created in this slice is saved with `draftReason: waiting_for_seat` as a result. **Missing decision to escalate:** a product/backend decision defining purchased seats, active count, pending-reservation count, and overage policy per billing model.
2. **No generic invitation-sending or account-provisioning service exists** that this feature could safely reuse. The only invitation code in the repository (`TenantOwnerInvitationService`, `InviteTenantAdminCommandHandler`, platform-manager invite) is narrowly scoped to tenant owner/admin bootstrap, not general employee invitation. Building a new one was out of scope for this slice. Consequently: no invite email is ever sent, no `users` row is ever created from this feature, and final employee creation (the transition from draft → real `employees` row + user account) is **not implemented**. `OnboardingDraftsController` only ever produces/updates `onboarding_drafts` rows.
3. **`work_schedules` and `checklist_templates` tables do not exist.** `OnboardingDraft.ScheduleId` / `SelectedTemplateId` are stored as unconstrained `Guid?` columns with no FK (documented in the entity's XML doc) — the frontend never populates them (see frontend report) and the backend never validates them against anything, since there's nothing to validate against.
4. **Employee visibility for pre-existing employees has a real gap.** `EmployeeVisibilityScopeResolver` resolves scope from the caller's own active `PositionAssignment` (`PrimaryEmployment`, `Active`) walked through `management_coverage_records`. Any employee who has **no** active primary position assignment (e.g., pre-existing rows seeded before this feature, or created by any future path that doesn't assign a position) is invisible to coverage-based scoping — they still appear to callers with `CanViewAllTenantEmployees` (e.g. `org:manage`), and to themselves, but not to a manager whose coverage would otherwise include them. This is a consequence of reporting-manager/coverage now being derived strictly from `position_assignments` (per the "never use `manager_id`" constraint) rather than any denormalized field. Documented, not silently patched.
5. **Docker/Testcontainers was unavailable for most of this implementation** and only became available near the end. All three integration test suites (`PositionAssignmentRlsIntegrationTests`, `EmployeesListIntegrationTests`, `OnboardingDraftsIntegrationTests`) were written and compiled throughout, but only executed against real PostgreSQL once Docker became available. That run caught a real, previously-masked bug — see next point.
6. **EF Core query-translation bug caught only by the real-Postgres integration run.** `EfEmployeeRepository`'s original implementation passed all 8 unit tests (EF InMemory provider) but failed 6 of 7 `EmployeesListIntegrationTests` against real Npgsql with a query-translation error. Root cause and fix are documented in the code comment on `EfEmployeeRepository` and in commit `dad387d`: EF Core (real provider) refuses to translate anything chained after a `.Select()` into a user-defined record/DTO type, and C# forbids tuple literals in expression trees (`CS8143`), so a shared private query-building method returning either type doesn't work. The fix keeps the entire join→filter→order→project pipeline as one unbroken anonymous-type LINQ chain inside each public method independently (duplicated between `ListVisibleAsync` and `GetVisibleByIdAsync`), with the DTO projection as the last operation before materialization. **Lesson for future work in this repo:** EF InMemory-based unit tests are not sufficient proof that a LINQ query is correct against Npgsql — always confirm with the real-Postgres integration suite before trusting a repository implementation.

---

## Schema / migrations

Reconciled against `OneVo-HR/database/phase1-table-inventory.md` before creation — `position_assignments` and `employee_hierarchy_closure` are the tables that inventory names for this milestone; `onboarding_drafts` is a new addition not in that inventory (flagged, not blocked, since it only persists pre-invite draft state and creates no employee/user rows).

| Migration | Purpose |
|---|---|
| `20260810063250_AddPositionAssignmentsAndHierarchyClosure` | Creates `position_assignments` (RLS-enabled) and `employee_hierarchy_closure` (RLS-enabled, composite PK). Hand-edited to add both tables to the migration's `TenantTables` RLS loop, matching the existing convention (not auto-generated by `dotnet ef`). |
| `20260810071627_AddOnboardingDrafts` | Creates `onboarding_drafts` (RLS-enabled). |
| `20260810072915_AddOnboardingDraftXminConcurrencyToken` | Hand-edited to a **complete no-op**. `xmin` is a PostgreSQL system column that always exists; the Npgsql provider pinned in this repo (10.0.2) has no `UseXminAsConcurrencyToken()` helper (confirmed by grepping the installed package DLL), so the concurrency token is mapped manually as an EF shadow property in `OnboardingDraftConfiguration` instead of via migration DDL. |

### New tables

- **`position_assignments`** — `EmployeeId`, `PositionId`, `AssignmentKind` (`PrimaryEmployment` / `AdditionalAuthority`), `EffectiveFrom`/`EffectiveTo`, `AssignmentStatus` (`Active`/`Planned`/`Ended`/`Cancelled`). Partial unique index `ix_position_assignments_one_active_primary_per_employee` enforces **exactly one active Primary Employment assignment per employee** at the database level (filtered on `assignment_kind = 'PrimaryEmployment' AND assignment_status = 'active'`). No `ManagerId` or `JobTitleId` column — enforced by `PositionAssignmentArchitectureTests`.
- **`employee_hierarchy_closure`** — ancestor/descendant closure table over the reporting chain, keyed `(TenantId, AncestorEmployeeId, DescendantEmployeeId)`, with `Depth`. Rebuilt by walking `positions.reports_to_position_id` chains (`EfEmployeeHierarchyClosureRepository.RebuildAsync`) with a cycle guard (`HashSet<Guid> visited`) — never trusts the position graph to be acyclic.
- **`onboarding_drafts`** — the entire pre-invite draft record: employee/company/department/position selections, `ScheduleId`/`SelectedTemplateId` (unconstrained, see blocker #3), `Status` (`waiting_for_seat` / `waiting_for_position_approval` / `saved_manually`... — see `OnboardingDraftStatus`), `DraftReason`, `LastSavedStep` (`OnboardingWizardStep`), `StartedById`. xmin-based optimistic concurrency.

No table marked deferred/prohibited in `phase1-table-inventory.md` was created or modified.

---

## Files changed (65 files, +24,719/-17 across commits `cd5e56f..dad387d`)

**Domain entities:** `PositionAssignment.cs`, `EmployeeHierarchyClosure.cs`, `OnboardingDraft.cs`.

**EF configuration:** `PositionAssignmentConfiguration.cs`, `EmployeeHierarchyClosureConfiguration.cs`, `OnboardingDraftConfiguration.cs`, `ApplicationDbContext.cs` (3 new `DbSet`s), 3 migrations + designer/snapshot files.

**Application layer — CoreHr/PositionAssignment & EmployeeHierarchyClosure:** `IPositionAssignmentRepository.cs`, `IEmployeeHierarchyClosureRepository.cs`.

**Application layer — CoreHr/Employee:** `EmployeeListItemResponse.cs`, `EmployeeListPageResponse.cs`, `EmployeeVisibilityScope.cs`, `IEmployeeVisibilityScopeResolver.cs`, `IEmployeeRepository.cs`, `ListEmployeesQuery(Handler/Validator).cs`, `GetEmployeeQuery(Handler).cs`.

**Application layer — CoreHr/OnboardingDrafts:** `SaveOnboardingDraftCommand(Handler/Validator).cs`, `OnboardingDraftResponse.cs`, `DraftListItemResponse.cs`, `IOnboardingDraftRepository.cs`, `GetOnboardingDraftQuery(Handler).cs`, `ListOnboardingDraftsQuery(Handler).cs`.

**Application/Common:** `ISeatEntitlementService.cs`, `ConcurrencyConflictException.cs`.

**Infrastructure — repositories:** `EfPositionAssignmentRepository.cs`, `EfEmployeeHierarchyClosureRepository.cs`, `EfEmployeeRepository.cs` (rewritten, see blocker #6), `EfOnboardingDraftRepository.cs`, `EmployeeVisibilityScopeResolver.cs`, `SeatEntitlementService.cs`, `DependencyInjection.cs` (registrations).

**API:** `EmployeesController.cs`, `OnboardingDraftsController.cs`, `SaveOnboardingDraftRequest.cs`.

**Tests:** `PositionAssignmentArchitectureTests.cs`, `EmployeesControllerArchitectureTests.cs`, `EmployeeLegacyFieldRetirementArchitectureTests.cs` (regex fix), `PositionPart2AArchitectureTests.cs` (removed stale deferred-table guard), `EfPositionAssignmentRepositoryTests.cs`, `EfEmployeeHierarchyClosureRepositoryTests.cs`, `EfEmployeeRepositoryTests.cs`, `GetEmployeeQueryHandlerTests.cs`, `ListEmployeesQueryHandlerTests.cs`, `EmployeesControllerTests.cs`, `SaveOnboardingDraftCommandHandlerTests.cs`, `GetAndListOnboardingDraftQueryHandlerTests.cs`, `OnboardingDraftsControllerTests.cs`, `SeatEntitlementServiceTests.cs`, `PositionAssignmentRlsIntegrationTests.cs`, `EmployeesListIntegrationTests.cs`, `OnboardingDraftsIntegrationTests.cs`.

---

## Endpoints

| Method | Route | Permission | Notes |
|---|---|---|---|
| `GET` | `/api/v1/employees` | `employees:read` | Paginated, search + `departmentId` + `legalEntityId` filters, stable `LastName, Id` order. |
| `GET` | `/api/v1/employees/{id}` | `employees:read` | 404 if not in caller's tenant; 403 if in-tenant but outside visibility scope. |
| `POST` | `/api/v1/onboarding/drafts` | `employees:write` | `[Idempotent]` via `Idempotency-Key` header. Creates a new draft. |
| `PUT` | `/api/v1/onboarding/drafts/{id}` | `employees:write` | Requires `If-Match` header (xmin version); 409 on stale write via `ConcurrencyConflictException`. |
| `GET` | `/api/v1/onboarding/drafts/{id}` | `employees:write` | Resume-draft fetch. |
| `GET` | `/api/v1/onboarding/drafts` | `employees:write` | Paginated draft list for the list-page "Onboarding drafts" section. |

Tenant ID is resolved from the trusted `TenantRlsInterceptor`/authenticated context in every handler — never accepted from the request body. `core_hr` module gating is enforced by the frontend route guard (see frontend report); the backend enforces permission + RLS regardless.

## Permissions and module gating

Only `employees:read` and `employees:write` are used — no new permission codes were introduced (`employees:delete` exists in the permission catalog but is unused by this slice, since delete/offboarding is out of scope). No role name is used as an authorization check anywhere in this code.

## Visibility rules

`EmployeeVisibilityScopeResolver` resolves an `EmployeeVisibilityScope` per caller:
- `CanViewAllTenantEmployees = true` when the caller holds `org:manage` (or an equivalent tenant-wide grant already present in the codebase) — unrestricted.
- Otherwise: caller's own `OwnEmployeeId` (self always visible) + `CoveredPositionIds`/`CoveredDepartmentIds`/`CompanyWideLegalEntityIds` derived from the caller's own active Primary Employment assignment's position, resolved through `management_coverage_records`.
- **Not** "every tenant employee merely because the caller has `employees:read`" — confirmed by `List_WithoutOrgManage_ReturnsOnlySelf_WhenCallerHasNoResolvableCoverage`.
- Known gap: employees with no active primary assignment are invisible to coverage-scoped callers (blocker #4).

## Assignment / reporting-manager behavior

- Exactly one active `PrimaryEmployment` assignment per employee, enforced by a partial unique index (not just application logic).
- No two active employment assignments in the same company is a consequence of the per-employee (not per-company) uniqueness plus the domain rule that an employee has one Primary Employment; cross-company reporting is prevented because `employee_hierarchy_closure` is rebuilt strictly from the position graph within `positions.reports_to_position_id`, which does not span company boundaries in this schema.
- Reporting manager is **always** derived — `EmployeeListItemResponse.ReportingManagerId/Name` comes from `employee_hierarchy_closure` at `Depth = 1` joined to the ancestor's current Primary Employment assignment. No `ManagerId` field exists anywhere; `EmployeeLegacyFieldRetirementArchitectureTests` guards this with a compiled word-boundary regex.

## Draft / final-creation behavior

- `SaveOnboardingDraftCommandHandler` routes `DraftReason`:
  - `waiting_for_position_approval` when the selected `Position`'s `PositionAccessTemplate.RequiresApproval` is true (verified as a real, existing field before use).
  - `waiting_for_seat` in every other case, since `ISeatEntitlementService` always reports `Undetermined` (blocker #1).
- Save Draft **never**: sends an invitation, creates a `users` row, activates an onboarding checklist, or assigns policies. Confirmed by `SaveOnboardingDraftCommandHandlerTests` and `OnboardingDraftsIntegrationTests` — the only side effect is an `onboarding_drafts` upsert.
- Final employee creation is **not implemented** in this slice (blocker #2) — there is no endpoint that transitions a draft into a real `employees` + `users` row. The seat-decision transaction-level re-check requirement from the spec is therefore currently moot and is documented here rather than implemented against a nonexistent final-creation path.

## Tests and results

All commands run from `HRMS-Backend-v1/` root.

- Build: `dotnet build` — clean.
- Unit: `dotnet test --filter "FullyQualifiedName~CoreHr"` → 61/61; full unit suite → 1554/1554.
- Architecture: full architecture suite → 548/548.
- Integration (real PostgreSQL via Testcontainers, Docker confirmed available):
  - `PositionAssignmentRlsIntegrationTests` — 4/4 (restricted-role RLS enforcement + one-active-primary-assignment uniqueness under real Postgres).
  - `EmployeesListIntegrationTests` — 7/7 after the `EfEmployeeRepository` rewrite (blocker #6); includes tenant isolation, stable pagination, search, department filter, no-coverage-self-only visibility, cross-tenant 404, in-scope 200.
  - `OnboardingDraftsIntegrationTests` — 4/4, including a real concurrent-write 409 test (two writers racing on the same draft's `If-Match` version).
- `git diff --check main...HEAD` — clean for all files touched by this feature (pre-existing whitespace issues exist elsewhere in the repo, untouched by this work).

## Skipped checks / known limitations

- Final employee creation, invitation sending, and account provisioning: not implemented (blockers #1, #2).
- Seat entitlement: always `Undetermined`; no numeric seat math exists anywhere in this feature (blocker #1).
- Schedule/Shift and Checklist Template selection: stored but never populated or validated — no backend source exists (blocker #3).
- Coverage-based visibility for employees without an active primary assignment: documented gap, not fixed (blocker #4).
- No `DELETE`/offboarding endpoint — out of scope per spec.
