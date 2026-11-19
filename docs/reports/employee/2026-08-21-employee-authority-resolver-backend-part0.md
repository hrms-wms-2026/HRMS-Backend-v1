# Backend Part 0 — Generic Employee Authority Resolver

Status: complete. Not committed, not pushed (per task instructions).

## 1. Files changed

### New files

- `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeAuthorityPurpose.cs`
- `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeApprovalRouteSource.cs`
- `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeAuthorityVisibilityRequest.cs`
- `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeAuthorityVisibilityScope.cs`
- `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeApprovalRouteRequest.cs`
- `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeApprovalRoute.cs`
- `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/ServiceInterfaces/IEmployeeAuthorityResolver.cs`
- `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs` — the resolver implementation
- `tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityTestGraph.cs` — hand-rolled in-memory fakes for every repository interface the resolver depends on
- `tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityResolverTests.cs` — the 28 required unit scenarios
- `tests/ONEVO.Tests.Architecture/EmployeeAuthorityResolverArchitectureTests.cs` — scenarios 29, 30, 32, 33

### Extended (existing repositories — no duplicates created)

- `src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeRepository.cs` (+ `EfEmployeeRepository.cs`): added `ListActiveEmployeeIdsAsync` (active employees in a legal entity, optionally restricted to a department-id set; `null` departments = whole legal entity, used only for company-wide coverage) and `ListActiveEmployeeIdsByIdsAsync` (the final tenant/legal-entity/active-status chokepoint filter).
- `src/ONEVO.Application/Features/CoreHr/EmployeeHierarchyClosure/RepositoryInterfaces/IEmployeeHierarchyClosureRepository.cs` (+ `EfEmployeeHierarchyClosureRepository.cs`): added `GetDescendantEmployeeIdsAsync` (transitive descendants of a set of ancestors) and `GetAncestorChainEmployeeIdsAsync` (full upward chain, nearest-manager-first).
- `src/ONEVO.Application/Features/OrgStructure/Department/RepositoryInterfaces/IDepartmentRepository.cs` (+ `EfDepartmentRepository.cs`): added `GetDescendantDepartmentIdsAsync` (recursive CTE, same pattern as the existing `IsDescendantAsync`).
- `src/ONEVO.Application/DependencyInjection.cs`: registered `IEmployeeAuthorityResolver → EmployeeAuthorityResolver`.

**Reused as-is, no changes:** `IPositionRepository.ListCoverageByOwnerPositionAsync` (an equivalent method already existed — my first pass duplicated it as `ListActiveCoverageByOwnerPositionAsync`; found this during the build and reverted it, see §9), `IPositionRepository.ListActiveCoverageByCoveredTargetAsync`, `IPositionAssignmentRepository.GetActivePrimaryAsync` / `GetActiveHoldersAsync`, `IPermissionRepository.UserHasPermissionCodeAsync`, `IEmployeeRepository.GetByIdAsync` / `GetByUserAndLegalEntityAsync`.

No frontend files touched. No commits made.

## 2. Resolver API

```csharp
namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;

public interface IEmployeeAuthorityResolver
{
    Task<EmployeeAuthorityVisibilityScope> ResolveVisibilityAsync(
        EmployeeAuthorityVisibilityRequest request, CancellationToken cancellationToken = default);

    Task<Result<EmployeeApprovalRoute>> ResolveApproverAsync(
        EmployeeApprovalRouteRequest request, CancellationToken cancellationToken = default);
}
```

Request/response records (`EmployeeAuthority.Models` namespace) match the task's suggested shapes exactly, with one deliberate rename: the visibility request/response types are prefixed `EmployeeAuthority*` (`EmployeeAuthorityVisibilityRequest` / `EmployeeAuthorityVisibilityScope`) rather than bare `EmployeeVisibilityRequest`/`EmployeeVisibilityScope`, because an unrelated `EmployeeVisibilityScope` record already exists at `Features.CoreHr.Employee.Models` (unexpanded covered-position/department id sets consumed by `ListEmployeesQueryHandler`'s own SQL). Reusing the bare name would have created a same-name/different-shape collision across namespaces. `EmployeeApprovalRouteRequest`/`EmployeeApprovalRoute` had no such collision and use the task's suggested names verbatim.

Neither request record has a `TenantId` field. Both resolver methods read `ICurrentUser.TenantId` internally and pass it to every downstream repository call; a foreign-tenant `ActorUserId`/`SubjectEmployeeId` simply resolves to "no visibility" / "not found" rather than being trusted.

`EmployeeAuthorityPurpose` is an in-memory enum (never persisted) matching the task's suggested value set exactly; adding a new purpose never requires a migration.

## 3. Visibility rules implemented

`ResolveVisibilityAsync`:

1. **Self**: if `IncludeSelf` and the actor has an active `Employee` row in the requested legal entity (`GetByUserAndLegalEntityAsync`), the actor's own id is added — with no permission check (self-service is explicitly not gated the same way management visibility is, per task rule 3/4).
2. **Managed visibility** only runs if `UserHasPermissionCodeAsync(actorUserId, RequiredPermission)` is true *and* the actor has an active PrimaryEmployment assignment. From the actor's own position, every active coverage record it owns (`ListCoverageByOwnerPositionAsync`, filtered to `Status == active` in the resolver since the repository method itself returns all statuses — it also backs the coverage-management UI) is expanded by target type:
   - **Position**: active holders of the covered position, plus every transitive descendant employee via `EmployeeHierarchyClosure` (depth ≥ 1) — this is the fix for the "shallow/direct-only" behavior the task called out; the CEO/GM/PM/Engineer example is covered by unit test 4.
   - **Department**: the covered department plus every active descendant department (recursive CTE), then every active employee in that expanded department set.
   - **Company**: every active employee in the legal entity (the one case where loading the whole legal-entity employee set is deliberate and unavoidable).
3. **Chokepoint**: the union of self + all managed candidate ids is passed through `ListActiveEmployeeIdsByIdsAsync`, which re-filters by tenant, legal entity, and active employment status in one query — so a stale/cross-boundary id introduced by any single expansion path (position, department, or company) can never leak into the final result, and `IncludesSelf` in the response reflects whether self actually survived that same filter, not just the request flag.

## 4. Approval routing rules implemented

`ResolveApproverAsync`, for a subject employee:

1. Loads the subject and rejects (404) if it doesn't exist in the requested legal entity.
2. Computes two things from `EmployeeHierarchyClosure`: the subject's full upward reporting chain (`GetAncestorChainEmployeeIdsAsync`, nearest-manager-first — the walk order for tier 3 only) and the subject's full downward set of subordinates (`GetDescendantEmployeeIdsAsync`). **The subordinate set is the only hierarchy-derived guard applied to coverage-based candidates (tiers 1 and 2) as of the "Manual coverage owner eligibility correction" below — coverage owners are *not* required to be reporting-line ancestors.** A coverage-tier candidate is rejected only if it is the subject themselves or one of the subject's subordinates; an owner who is a sibling, a skip-level relation, or entirely outside the org chart (e.g. an HR business partner) is eligible as long as every other guard passes.
3. **Tier 1 — Position coverage**: if the subject has an active primary position, `ListActiveCoverageByCoveredTargetAsync(..., TargetPosition, subjectPositionId, ...)` returns records ordered by `OwnerOrder`. For each, in order: the owner position must be active; holders are resolved via `GetActiveHoldersAsync` (one holder → automatic; multiple → `ResponsibleEmployeeId` must pick one; otherwise the level is *unresolved and skipped*, not a dead end — mirroring `GetCoverageResolutionQueryHandler`'s exact disambiguation, per the task's explicit instruction not to invent a "first holder with permission" rule); the resolved holder must not be the subject or a subordinate of the subject, must be in the requested legal entity, must currently be an active employee (`ListActiveEmployeeIdsByIdsAsync` chokepoint), and must hold the required permission. First level to satisfy everything wins.
4. **Tier 2 — Department coverage**: same algorithm against `TargetDepartment`/subject's `DepartmentId`, only reached if tier 1 found nothing.
5. **Tier 3 — Reporting-line fallback**: walks the ancestor chain nearest-first; for each ancestor, checks legal entity, permission, and that they have an active primary assignment (to supply `ApproverPositionId`); first match wins. This tier remains strictly upward-only - it can structurally never reach a sibling or subordinate, since it only ever iterates the ancestor chain.
6. If all three tiers are exhausted: `Result<EmployeeApprovalRoute>.UnprocessableEntity("No eligible approver was found for this employee and action.")` — never `Forbidden` (the actor isn't being denied; the org configuration has no eligible approver, which is a 422, not a 403), never a silent fallback to an admin/owner, never cross-legal-entity.

**Deliberate scope decision**: `TargetCompany` coverage is *not* a fourth routing tier. The task's routing priority list (position → department → reporting-line) doesn't mention company-wide coverage, so it participates only in visibility, not in approval routing. Documented here so Part 1+ callers don't assume otherwise.

## 5. Permission behavior

- `RequiredPermission` is always caller-supplied (`attendance:read`, `attendance:approve`, `time_off:approve`, `employees:read` are the seeded codes confirmed in `PermissionSeeder.cs` — no new permission string was invented; see architecture test 33, which greps the resolver source for any hardcoded `"resource:action"` literal and asserts there is none).
- Permission is checked with the existing `IPermissionRepository.UserHasPermissionCodeAsync(userId, code, now)` — the same primitive already used by `ChangeEmployeePositionCommandHandler` and `OnboardingDraftWriteService` for "does this specific user have this permission" checks. It is **not** tenant-scoped internally (it filters by `UserId` only); tenant/legal-entity isolation for the resolver comes entirely from the fact that every candidate id it is ever called against was produced by a tenant-scoped enumeration/chokepoint query first (see §6). Unit tests 27/28 (`Approval_NeverSelectsCrossTenantApprover`/`Approval_NeverSelectsCrossLegalEntityApprover`) construct a candidate who *does* hold the permission but is cross-tenant/cross-legal-entity via the **reporting-line (tier 3)** guard specifically; the added `Approval_CoverageOwner_InDifferentLegalEntity_IsRejected` (see §12) covers the same claim for the **coverage-tier (tiers 1/2) chokepoint**, which is a materially different code path after the manual-coverage correction.
- Self-visibility is deliberately **not** permission-gated (task rule 3/4: self-service is authenticated-self-service, not a management view).
- `ListUserIdsWithPermissionCodeAsync` (the "who holds this permission, tenant-wide" query used by `ApproveAccessGrantRequestCommandHandler`'s static-permission approach) is never used by this resolver — approver candidates only ever come from coverage records or the reporting line, never from "everyone with permission X."

## 6. Tenant / legal-entity isolation

- Both public methods take **no `TenantId` parameter**. Tenant context is `ICurrentUser.TenantId`, read once per call and threaded through every repository call.
- Every repository method the resolver calls is tenant-scoped by construction (existing methods already were; the new ones — `ListActiveEmployeeIdsAsync`, `ListActiveEmployeeIdsByIdsAsync`, `GetDescendantEmployeeIdsAsync`, `GetAncestorChainEmployeeIdsAsync`, `GetDescendantDepartmentIdsAsync` — all take `tenantId` as their first parameter and filter on it).
- Legal entity scoping is explicit at every boundary that matters: coverage lookups take `legalEntityId`; department expansion takes `legalEntityId`; the final visibility chokepoint filters on `legalEntityId`. Approval-routing candidates are legal-entity-checked differently per tier since the §12 correction: tier 3 (reporting-line) still does an explicit `ancestorEmployee.LegalEntityId == request.LegalEntityId` comparison after fetching the candidate; tiers 1/2 (coverage) now go through the `ListActiveEmployeeIdsByIdsAsync` chokepoint (tenant + legal entity + active, in one call) before the candidate is even considered further.
- A cross-tenant or cross-legal-entity id can enter a candidate *set* only through a place where I deliberately did not filter (there are none in the resolver's own logic — every expansion path is already scoped); the chokepoint/per-candidate re-fetch exists specifically so that even a hypothetically buggy expansion path can't leak through, per the advisor review during design.

## 7. Caching decision

**No caching added.** Rule 12 explicitly permits deferring this, and there is no existing invalidation hook for role/permission or coverage-record changes in this codebase to hang a cache off safely. `IEmployeeAuthorityResolver` is the stable public API; a cache can be added behind it later (e.g. inside `EmployeeAuthorityResolver` or a decorator) once an invalidation story exists (role change, coverage record change, position reporting-line change would all need to bust it). Adding caching now would risk stale authorization surviving a permission or role change — the wrong thing to be clever about in an authorization foundation.

## 8. Tests run

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --configuration Release
  → 0 Error(s)

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release \
  --filter "EmployeeAuthority|ManagementCoverage|ApprovalRoute|Coverage"
  → Passed: 81, Failed: 0 (includes all 34 EmployeeAuthorityResolverTests scenarios after the
     manual-coverage correction below - the original 28 plus 6 net new/rewritten; verified
     individually with --filter "FullyQualifiedName~EmployeeAuthorityResolverTests" → 34/34 passed)

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release
  → Passed: 629, Failed: 1, Total: 630 (see §9 — the one failure is pre-existing and unrelated;
     identical failing test and identical 629/1/630 counts before and after this correction)

git diff --check
  → only pre-existing LF/CRLF advisory warnings on files this task did not touch; no errors
     (`git diff --check` only inspects tracked-file diffs against HEAD - every file this task
     created is new/untracked, so it was never examined by this command; nothing found wrong in
     them by manual inspection, but that check specifically does not cover them)
```

All 28 unit scenarios map 1:1 to the task's numbered list (11 visibility + 17 approval routing); each `[Fact]` is commented with its scenario number.

## 9. Pre-existing failures — proven unrelated

**`TenantIsolationArchitectureTests.IgnoreQueryFilters_UsageIsExplicitlyAllowlisted`** fails because `EfEmployeeRepository.cs` calls `.IgnoreQueryFilters()` in `EmployeeNumberExistsAsync` and `GetNextEmployeeNumberSequenceAsync`, and the file isn't on that test's allowlist. Evidence this is unrelated to this task (Part 0 delivery):
- `EfEmployeeRepository.cs` was already listed as modified (` M`) in `git status --short --branch` **before any tool call in the Part 0 session** (captured at the very start of that task, per the required first step).
- The Part 0 session's own `Read` of the file (before making any edits to it) already showed both `.IgnoreQueryFilters()` calls, in methods (`EmployeeNumberExistsAsync`, `GetNextEmployeeNumberSequenceAsync`) it never touched — its only edit to this file was appending `ListActiveEmployeeIdsAsync`/`ListActiveEmployeeIdsByIdsAsync` after the unrelated `CountActiveAsync` method.
- `git diff --unified=0` for this file shows the `IgnoreQueryFilters` lines only because the diff is against the last **commit**, not against that session's starting point — the working tree already had ~60 modified/untracked files from prior uncommitted work before Part 0 began (per that task's own git status output), and this file's `IgnoreQueryFilters` calls are part of that pre-existing work, not this task's.

**Confirmed still true after the §12 correction**: this correction session never opened `EfEmployeeRepository.cs` at all — its diff touches only `EmployeeAuthorityResolver.cs`, `IDepartmentRepository.cs` (a doc comment), and `EmployeeAuthorityResolverTests.cs`. The failing test names the exact same offending file (`["EfEmployeeRepository.cs"]`) and the exact same counts (629 passed / 1 failed / 630 total) both before and after this correction — the strongest available evidence that nothing in this correction changed this failure's cause or outcome.

Nobody in either session modified the allowlist or the failing test — that's a call for whoever owns the pre-existing employee-number-generation work, not this task.

## 10. Skipped checks

- **Integration tests were attempted, not merely assumed unavailable.** Two independent blockers, both with real captured errors:
  1. `docker version` → `Error response from daemon: Docker Desktop is unable to start` (Testcontainers has no engine to talk to).
  2. Independently of Docker, `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj` **fails to compile** on this branch: `CS1503: Argument 2: cannot convert from 'BulkOnboardingRowValidator' to 'IBulkOnboardingValidationRunner'` in `BulkOnboardingValidateTests.cs` and `BulkOnboardingCreateDraftsTests.cs` — pre-existing broken state from the same uncommitted BulkOnboarding work visible in the starting `git status`, not something this task touched.
  - Net result: no integration tests for this feature were run, and none are claimed to have passed. If it's useful later, the transitive-visibility and upward-only-routing unit tests in `EmployeeAuthorityResolverTests.cs` cover the same business rules an integration test would, just against fakes instead of a real Postgres/RLS boundary.
- **The three new EF-backed repository methods have zero executed coverage against a real database.** `IEmployeeRepository.ListActiveEmployeeIdsAsync`/`ListActiveEmployeeIdsByIdsAsync` and `IDepartmentRepository.GetDescendantDepartmentIdsAsync` are only exercised through the unit-test fakes (`EmployeeAuthorityTestGraph`), which model their intended behavior but do not run their actual EF/SQL translations. `GetDescendantDepartmentIdsAsync` in particular uses a raw `WITH RECURSIVE` CTE via `_db.Database.SqlQuery<Guid>` (matching `IsDescendantAsync`'s existing convention) — this cannot run at all against the EF InMemory provider and has never executed against real PostgreSQL in this task. The fakes also encode "active employee" as `EmploymentStatusId == 1`, mirroring but not proving the real query's `join employment_statuses ... where Code == "active"`. This should be the first thing verified once Docker/Testcontainers is available (see §12).

## 11. Known limitations

- **No repository query builds the CEO/GM/PM tree from `Position.ReportsToPositionId` directly for visibility.** Transitive position-coverage visibility relies on `EmployeeHierarchyClosure`, a materialized cache that is itself derived from `ReportsToPositionId` + active PrimaryEmployment assignments and is documented as "not source of truth, safe to rebuild." If that cache is stale (rebuild not yet triggered after a reporting change), visibility will be stale too, for exactly as long as `EfEmployeeRepository.ListVisibleAsync` already tolerates today — this task did not change that tradeoff, only reused it.
- **Company-wide (`TargetCompany`) coverage is visibility-only**, not a routing tier — see §4. If a future product decision wants company-wide coverage owners to be eligible approvers, that's a new rule, not a bug in this task.
- **Vacant/ambiguous coverage owner positions are skipped, not escalated or logged.** Matches `GetCoverageResolutionQueryHandler`'s existing behavior exactly (single holder resolves; multiple holders need `ResponsibleEmployeeId`; otherwise the level is silently unresolved and the next `OwnerOrder` is tried). A future task may want to surface "coverage is misconfigured" as a distinct signal to admins; this task deliberately did not invent that.
- **`UserHasPermissionCodeAsync` doesn't check `UserPermissionOverride` revocations** (only role-derived permissions via `UserRoles` → `RolePermissions`) — this is a pre-existing characteristic of the repository method being reused, not something this task introduced or could safely change without affecting every other caller of that method.
- **Duplicated approval-routing logic that should eventually be replaced by this resolver** (flagged per task rule 15, not touched in this task):
  - `EmployeeOffboardingCoverageGuard` (`src/ONEVO.Infrastructure/Services/CoreHr/Offboarding/EmployeeOffboardingCoverageGuard.cs`) re-derives a coverage check inline via `IEmployeeVisibilityScopeResolver`, independently of this resolver.
  - `ApproveAccessGrantRequestCommandHandler` / `AccessGrantRequestsController` route purely on the static `roles:manage` permission — no coverage or reporting-line routing at all today, despite a controller comment ("Position Approver Inbox") implying it was always intended to be coverage-based.
  - `ApproveBypassRequestCommandHandler` (Offboarding) and `ApproveObjectiveChangeRequestCommandHandler` each implement their own bespoke "who can decide" logic.
  - `ListEmployeesQueryHandler` + `EfEmployeeRepository.ListVisibleAsync` implement direct-only (non-transitive) coverage visibility via `IEmployeeVisibilityScopeResolver` — this is the People employee list Part 1 is expected to replace with this resolver.

## 12. Manual coverage owner eligibility correction

Follow-up correction applied after the initial Part 0 delivery. No public API change, no repository contract change beyond reusing an existing method (`GetDescendantEmployeeIdsAsync`, already added in Part 0 for visibility); only `EmployeeAuthorityResolver.ResolveApproverAsync`'s internal guard changed.

### Old behavior

Approval routing applied a single "upward only" guard to **every** candidate in **all three** tiers: a coverage-tier candidate (position or department owner) had to be a member of the subject's reporting-line ancestor set, computed via `GetAncestorChainEmployeeIdsAsync`, or it was skipped regardless of permission. This meant a manually configured coverage owner who was not literally the subject's manager, skip-level manager, etc. — for example an HR business partner given position or department coverage over a team they don't formally manage in the org chart — was silently never eligible, and routing fell through to the reporting line (or failed entirely) instead. This was flagged as an explicit known limitation in the original §11.

### Corrected behavior

Manual management coverage is now authoritative for tiers 1 and 2. A coverage-tier candidate is eligible as soon as it passes:

- resolved from an active coverage record, active owner position, and (for multi-holder positions) an unambiguous or `ResponsibleEmployeeId`-disambiguated holder (unchanged from before),
- **is not the subject employee themselves**,
- **is not a subordinate of the subject** (computed via `GetDescendantEmployeeIdsAsync(tenantId, [subject.Id])` — reused verbatim from the visibility path, no new repository method needed, per the task's "reuse if it exists" instruction),
- is an active employee in the same tenant and the same legal entity as the request (`ListActiveEmployeeIdsByIdsAsync` chokepoint — this also newly enforces "active employee/user behind the owner position" explicitly, which the original implementation did not check separately from the position assignment being active),
- holds the required permission.

Being a reporting-line ancestor of the subject is **no longer required** for tiers 1/2. Owner order (Primary → Backup 1 → Backup 2 → …) is still evaluated strictly in sequence regardless of whether any given owner is inside or outside the reporting line — order is authoritative, not hierarchy position.

Tier 3 (reporting-line fallback) is **unchanged**: it only ever walks the ancestor chain, so it remains structurally upward-only and can never reach a subordinate or a sibling.

### Why manual coverage is authoritative

`management_coverage_records.source = 'Manual'` is a deliberately first-class concept with its own admin-facing CRUD (`AddManualCoverageRecordCommandHandler`/`UpdateManualCoverageRecordCommandHandler`/`RemoveManualCoverageRecordCommandHandler`). An admin explicitly configuring "HR Manager is the Primary Manager coverage owner for Project Manager's position" is expressing organizational intent that is independent of, and can legitimately override, the reporting-line shape — that is the entire product reason manual coverage exists as distinct from the reporting-line-derived coverage the system also generates automatically. Requiring the manually configured owner to *also* happen to be a reporting-line ancestor made the manual-coverage feature unable to express the exact cross-functional-approver scenarios it was built for (HR, compliance, finance approvers who cover a team without managing it). The one guard that must survive regardless is the subordinate check: allowing a subject's own report to approve the subject's request would be a reverse-approval hole no coverage configuration should be able to create, so that check stays absolute.

### Tests added/changed

- **Rewrote** `Approval_NeverSelectsSibling` → `Approval_ReportingLineFallback_NeverSelectsSibling`: now a pure reporting-line scenario with no coverage record at all (a coverage record naming the sibling as owner would now make the sibling a *correctly* eligible candidate, which would contradict the old test's premise). Confirms tier 3 still cannot structurally reach a sibling.
- **Added** `Approval_PositionCoverageOwner_OutsideReportingLine_IsSelected` — position coverage owner unrelated to the subject's hierarchy, with permission, is selected.
- **Added** `Approval_DepartmentCoverageOwner_OutsideReportingLine_IsSelected` — same for department coverage.
- **Added** `Approval_BackupOwner_OutsideReportingLine_IsSelected_WhenPrimaryLacksPermission` — backup owner outside the reporting line is selected when the (also outside-hierarchy) primary lacks permission.
- **Added** `Approval_OwnersOutsideReportingLine_StillRespectOwnerOrder` — three unrelated owners at orders 1/2/3, orders 1 and 3 both have permission; order 1 must still win.
- **Added** `Approval_CoverageOwner_WhoIsSubjectThemselves_IsRejected` — a coverage record naming the subject's own position as owner of their own department is rejected even though every other guard (tenant, legal entity, active, permission) would pass.
- **Added** `Approval_CoverageOwner_InDifferentLegalEntity_IsRejected` — a coverage-tier owner (not a reporting-line ancestor - irrelevant now) who holds the required permission but belongs to a different legal entity is still rejected. This specifically exercises the `ListActiveEmployeeIdsByIdsAsync` chokepoint that now guards tiers 1/2, as distinct from tier 3's `ancestorEmployee.LegalEntityId == request.LegalEntityId` comparison that tests 27/28 already covered.
- **Added** `Visibility_ManualCoverage_OutsideReportingLine_Works` — explicit visibility scenario with zero reporting-line relationship between actor and covered employee (visibility never had the restrictive guard to begin with, so this is a documentation/regression test, not a behavior change).
- **Kept unchanged**: `Approval_NeverSelectsSubordinate` (still correctly named and still passes — subordinate rejection is the one hierarchy guard that survives), `Approval_ReportingLineFallback_WalksUpward`, `Approval_ContinuesUpward_WhenImmediateManagerLacksPermission`, `Approval_ReturnsBusinessFailure_WhenNoEligibleApproverExists`, `Approval_NeverRoutesToSubjectItself`, `Approval_NeverSelectsCrossTenantApprover`, `Approval_NeverSelectsCrossLegalEntityApprover`, and all owner-order/priority/inactive-record tests (12–17, 24–26) — all of these already wired their coverage owners as reporting-line ancestors, so the looser guard doesn't change their outcome; they now additionally prove the correction didn't regress the cases that used to work.
- Net: 28 → 35 scenarios in `EmployeeAuthorityResolverTests.cs`.

### Verification results

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --configuration Release
  → 0 Error(s)

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release \
  --filter "EmployeeAuthority|ManagementCoverage|ApprovalRoute|Coverage"
  → Passed: 82, Failed: 0
  --filter "FullyQualifiedName~EmployeeAuthorityResolverTests"
  → Passed: 35, Failed: 0

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release
  → Passed: 629, Failed: 1, Total: 630 - same single failure
     (TenantIsolationArchitectureTests.IgnoreQueryFilters_UsageIsExplicitlyAllowlisted, same
     offending file "EfEmployeeRepository.cs", same 629/1/630 counts as the pre-correction run) -
     this correction touched EmployeeAuthorityResolver.cs, a doc comment in IDepartmentRepository.cs,
     and the test file only; EfEmployeeRepository.cs was not touched in this correction, so this
     remains the same pre-existing, unrelated failure documented in §9.

git diff --check
  → clean on the tracked files this correction modified (EmployeeAuthorityResolver.cs,
     IDepartmentRepository.cs); `EmployeeAuthorityResolverTests.cs` lives inside the untracked
     tests/.../EmployeeAuthority/ directory, so the entire file - not merely this correction's
     edits to it - is invisible to `git diff`/`git diff --check` regardless. Same caveat as §8.
```

### Remaining risks

- **The new active-employee chokepoint applies to coverage tiers (1/2) only.** Tier 3 (reporting-line fallback) still selects an ancestor after checking legal entity, permission, and an active primary assignment — it does not separately re-verify the ancestor's own employment status is active the way `ListActiveEmployeeIdsByIdsAsync` now does for coverage owners. This asymmetry is deliberate under "don't change what wasn't asked" (tier 3's behavior wasn't part of this correction's scope), but it means the two tiers aren't perfectly symmetric today - worth resolving explicitly, not by accident, when Part 1 or a later task next touches tier 3.
- The subordinate-exclusion guard depends on `EmployeeHierarchyClosure` being up to date (same materialized-cache staleness caveat as §11's first bullet). If the closure hasn't been rebuilt after a reporting-line change, a genuine subordinate could theoretically be missed by `GetDescendantEmployeeIdsAsync` and incorrectly treated as eligible. This is an existing, documented characteristic of the closure table, not something this correction introduced.
- "Owner is not a subordinate" is checked at the **employee** level (is the resolved holder anywhere in the subject's downward closure), not restricted to direct reports — this is intentional and matches the task's "never subordinate" wording (not "never direct report"), but is worth calling out explicitly since it's a stricter reading than some might expect.
- No integration test exercises this correction against real PostgreSQL, for the same reasons documented in §10 (Docker unavailable; integration test project doesn't compile for unrelated reasons).

## 13. Next recommended task

**Backend Part 1 — Apply `EmployeeAuthorityResolver` to People employee list**: replace `ListEmployeesQueryHandler`'s use of `IEmployeeVisibilityScopeResolver` + `EfEmployeeRepository.ListVisibleAsync`'s direct-only coverage filtering with `IEmployeeAuthorityResolver.ResolveVisibilityAsync(purpose: EmployeeListRead)`, which will also fix the transitive-visibility gap `ListVisibleAsync` currently has (§3 above) as a side effect.
