# Backend Part 1 — Apply IEmployeeAuthorityResolver to the People employee list

Status: complete for the List endpoint. Not committed, not pushed (per task instructions).

## 1. Endpoint discovered

`GET /api/v1/employees` — `EmployeesController.List` ([EmployeesController.cs](src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs):43-59), routed to `ListEmployeesQuery` → `ListEmployeesQueryHandler`. This is the only People employee list endpoint; no new endpoint was created.

## 2. Old behavior

- Gated by `[RequirePermission("employees:read")]` at the action level (unchanged by this task).
- `ListEmployeesQueryHandler` called `IEmployeeVisibilityScopeResolver.ResolveAsync(tenantId, userId)`, which computed a **direct-only** `EmployeeVisibilityScope` (own employee id, directly-covered position ids, directly-covered department ids, company-wide legal-entity ids) purely from `management_coverage_records` owned by the caller's own active primary position — **no permission check at all** inside the resolver; the route's `[RequirePermission("employees:read")]` was the only gate.
- `EfEmployeeRepository.ListVisibleAsync` applied that scope as an `OR` filter directly inside the paginated SQL query (not "load all, filter in memory" — it was already a proper scoped query, just not resolver-backed and not transitive).
- No `legalEntityId` scoping of visibility itself — `legalEntityId` was only a result filter; the underlying coverage sets were computed once per caller, tenant-wide, and could (in principle) mix data from more than one legal entity if the caller's own Employee row didn't match the legal entity of the results (it doesn't in practice — see §5).
- Transitive coverage (CEO → GM → PM → Engineer through a position several reporting levels down) was **not** supported — only direct holders of a covered position/department were visible, a gap this task's parent (Part 0) documented explicitly as the reason the resolver replaces this handler.
- A pending-invited-by-me merge ran after the coverage query, unconditionally, without any legal-entity filter.

## 3. New behavior

- `ListEmployeesQueryHandler` now depends on `IEmployeeAuthorityResolver` instead of `IEmployeeVisibilityScopeResolver` (the latter interface/implementation is untouched — `GetEmployeeQueryHandler`, `GetEmployeeDetailQueryHandler`, `GetMyProfileQueryHandler`, `ListOffboardingOverviewQueryHandler`, and `EmployeeOffboardingCoverageGuard` still use it; only the List handler was migrated in this task).
- **Legal entity resolution**: `EmployeeAuthorityVisibilityRequest.LegalEntityId` is required (not nullable) — the resolver is inherently single-legal-entity. When the query supplies `legalEntityId`, that value is used directly. When it doesn't (the People list's default "all companies" load state — confirmed via the frontend's `people.store.spec.ts` default of `legalEntityId: null`), the handler falls back to the actor's own default Employee row's legal entity via `IEmployeeRepository.GetDefaultForUserAsync` (the same primitive the company switcher uses to default a session). If the actor has no Employee row anywhere, the handler returns an empty page without calling the resolver.
  - **Rule 5's "or selected company context" clause, reconciled**: the session *does* carry an explicit selected-company signal — `SwitchActiveCompanyCommandHandler` writes `session.ActiveEmployeeId`, and `TenantDatabaseTicketStore.CreateTicketAsync` reads it back into a per-request `activeLegalEntityId` used to scope permission resolution. However, that value is **not** exposed to the Application layer anywhere: it's computed transiently inside `TenantDatabaseTicketStore` (Infrastructure) and consumed only by the permission resolver — it is never written to a claim, and `ICurrentUser` (`UserId`, `TenantId`, `Email`, `Permissions`, `IsAuthenticated`, `SessionBinding`, `SessionExpiresAt`, `SessionId`) has no property carrying it forward. Adding one would mean threading a new claim through the auth ticket and `ICurrentUser`/its Infrastructure implementation - real, but genuinely out of scope for a resolver-wiring task and risky to get right blind (touches every request's auth ticket, not just this endpoint). `GetDefaultForUserAsync` is the closest *reachable* proxy for "the session's selected company": it is the exact same helper `TenantDatabaseTicketStore.CreateAsync` uses to seed `session.ActiveEmployeeId` in the first place when a session is created with no explicit selection yet (`ActiveEmployeeId = defaultEmployee?.Id`, same repository method). So this is "the resolver's available legal entity context" the task's fallback instruction points to - not the literal live session selection, but the same rule that seeds it. Flagged here as a deliberate, reasoned choice, not an oversight: if a future task exposes the session's active legal entity on `ICurrentUser`, this handler should prefer it over `GetDefaultForUserAsync`.
- This collapses to **one code path**, not two. I initially planned to keep `IEmployeeVisibilityScopeResolver` alive as a parallel "tenant-wide" branch when `legalEntityId` was omitted, reasoning from rule 5's "legal-entity-scoped **where** route/query has legalEntityId." An advisor review caught that this would leave the frontend's default load state on the legacy resolver — the exact thing Part 0 says this task should replace — and asked me to verify a specific fact before deciding: whether `ManagementCoverageRecord.LegalEntityId` can differ from its owner position's legal entity. It cannot — `AddManualCoverageRecordCommandHandler` looks up both the owner position and the covered position/department via `GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, ...)`, so a coverage record's `LegalEntityId` is always the same legal entity as both ends. That means the legacy tenant-wide coverage scope was already effectively single-legal-entity in outcome for managed visibility; defaulting to the actor's own legal entity is behavior-preserving, not a narrowing, so the legacy branch was removed entirely.
- **Resolver call**: `ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(currentUser.UserId, legalEntityId, "employees:read", IncludeSelf: true, EmployeeAuthorityPurpose.EmployeeListRead))`.
- **Empty visibility**: if `visibility.EmployeeIds.Count == 0`, the handler returns an empty page **without calling `ListVisibleAsync`** (requirement 7) — it does not fall back to a broad/unscoped query.
- **Repository**: `EmployeeListFilter` gained a new optional `RestrictToEmployeeIds` field. `EfEmployeeRepository.ListVisibleAsync` now branches: if `RestrictToEmployeeIds` is set, it filters `WHERE employee.Id IN (@ids)` and ignores the legacy `EmployeeVisibilityScope` coverage filter entirely; otherwise it falls back to the original scope-based filter unchanged (still used by `GetVisibleByIdAsync` and the offboarding feature, which were not touched). Search/department/legal-entity filters, ordering (`LastName`, then `Id`), and pagination (`Skip`/`Take`) all apply **after** and **within** the restriction, unchanged from before. `AsNoTracking()` was already in place and remains.
- **Transitive coverage now works**: position coverage expands through `EmployeeHierarchyClosure` (any depth), department coverage expands through descendant departments, matching Part 0's resolver behavior exactly — this closes the gap Part 0's report flagged as the reason for this task.
- **Pending-invited merge**: unchanged in spirit (still merges anyone the caller invited who hasn't accepted, regardless of coverage/visibility), but now additionally filtered to the resolved `legalEntityId` — closing a latent cross-legal-entity leak the legacy tenant-wide merge had (an invitee in a different legal entity than the one being viewed would previously have been merged in unconditionally).

## 4. Files changed

### Production code
- [src/ONEVO.Application/Features/CoreHr/Employee/Queries/ListEmployees/ListEmployeesQueryHandler.cs](src/ONEVO.Application/Features/CoreHr/Employee/Queries/ListEmployees/ListEmployeesQueryHandler.cs) — rewritten: `IEmployeeAuthorityResolver` instead of `IEmployeeVisibilityScopeResolver`, legal-entity resolution, empty-visibility short-circuit, legal-entity-filtered pending-invited merge.
- [src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeRepository.cs](src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeRepository.cs) — `EmployeeListFilter` gained `RestrictToEmployeeIds` (optional, trailing, default `null` — no other caller's construction site needed updating).
- [src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs](src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs) — `ListVisibleAsync` branches on `RestrictToEmployeeIds` vs. the legacy scope filter. `GetVisibleByIdAsync` (a different method, scope-only, no `filter` parameter) was **not** touched.

### Tests
- [tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeRepositoryTests.cs](tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeRepositoryTests.cs) — 3 new EF-InMemory repository-level tests for `RestrictToEmployeeIds` (restricts and ignores scope; empty set → empty result; search filter still applies within the restriction).
- [tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ListEmployeesQueryHandlerTests.cs](tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ListEmployeesQueryHandlerTests.cs) — rewritten for the new constructor/dependency; 14 tests covering legal-entity default resolution, explicit `legalEntityId` bypassing the default lookup, resolver call shape (purpose/permission/IncludeSelf), empty-visibility short-circuit, no-employee-row short-circuit, `RestrictToEmployeeIds` wiring, page/size clamping, and pending-invited merge (including the new legal-entity filter and its cross-legal-entity exclusion).
- [tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ListEmployeesQueryHandlerAuthorityResolverIntegrationTests.cs](tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ListEmployeesQueryHandlerAuthorityResolverIntegrationTests.cs) — new. Runs the handler against a **real** `EmployeeAuthorityResolver` (built from Part 0's `EmployeeAuthorityTestGraph` fakes, not mocked) paired with a small purpose-built `IEmployeeRepository` fake that mirrors `EfEmployeeRepository.ListVisibleAsync`'s `RestrictToEmployeeIds` handling in-memory. 7 tests: transitive position-coverage visibility (CEO→GM→PM→Engineer), manual department coverage outside the reporting line, exclusion of an employee outside the resolved visible-id set, exclusion when the actor has neither permission nor self, cross-legal-entity exclusion (even under company-wide coverage), cross-tenant exclusion, and search filtering within the resolver's visible ids.
- [tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs](tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs) — 2 new tests: the `List` action still carries `[RequirePermission("employees:read")]`, and its signature accepts no `tenantId` parameter.
- [tests/ONEVO.Tests.Architecture/ListEmployeesAuthorityResolverArchitectureTests.cs](tests/ONEVO.Tests.Architecture/ListEmployeesAuthorityResolverArchitectureTests.cs) — new. Asserts the handler's constructor depends on `IEmployeeAuthorityResolver` and nothing from `ONEVO.Infrastructure` or the legacy `IEmployeeVisibilityScopeResolver`; asserts the handler source uses `EmployeeAuthorityPurpose.EmployeeListRead` and `"employees:read"`, never `"employees:write"`.
- [tests/ONEVO.Tests.Integration/CoreHr/Employee/EmployeesListIntegrationTests.cs](tests/ONEVO.Tests.Integration/CoreHr/Employee/EmployeesListIntegrationTests.cs) — updated to compile and to reflect the new resolver's semantics (see §9 — **not executed**, Docker unavailable in this environment).

## 5. Resolver request parameters used

```
EmployeeAuthorityVisibilityRequest(
    ActorUserId:        currentUser.UserId,
    LegalEntityId:       request.LegalEntityId ?? actor's default Employee row's LegalEntityId,
    RequiredPermission:  "employees:read",
    IncludeSelf:         true,
    Purpose:             EmployeeAuthorityPurpose.EmployeeListRead)
```

No new permission string was introduced — `"employees:read"` is the same literal already on the route's `[RequirePermission]` attribute and in `PermissionSeeder.cs`.

## 6. Permission behavior

- Route-level gate is unchanged: `[RequirePermission("employees:read")]` on `EmployeesController.List`.
- `employees:write` was already, and remains, scoped to `ChangePosition`/`UpdateMyPayroll` — never used for list read.
- Inside the resolver, managed (non-self) visibility additionally requires the actor to hold `employees:read` via `IPermissionRepository.UserHasPermissionCodeAsync` — this is Part 0's resolver behavior, reused as-is, not something this task added.

## 7. Self visibility decision

**Discovered conflict, pre-existing, not introduced by this task**: `ModuleAutoGrants.cs` auto-grants ordinary employees `employees:read-own` on the `core_hr`/`employees` module, **not** `employees:read`. Since the whole `GET /api/v1/employees` route already requires `employees:read`, an ordinary employee with only `employees:read-own` could never reach this endpoint at all — before or after this task. This is not a regression Part 1 caused; it was true of the handler's previous implementation as well (the route attribute is unchanged).

Per the task's explicit guidance for this exact conflict, I took the "smallest safe behavior" path:
- Kept the route permission exactly as it was (did not loosen or remove it).
- Implemented the resolver call with `IncludeSelf = true` inside the handler regardless, so that if/when a future decision grants `employees:read` (or a routing change) to ordinary employees, self-visibility on this endpoint works correctly without further handler changes.
- **Follow-up flagged, not implemented here**: either (a) grant `employees:read` more broadly (a permission/role decision, out of scope for a resolver-wiring task), or (b) add a self-service-scoped variant of this route gated on `employees:read-own` that only ever returns the caller's own row. Task rule 10 explicitly excludes inventing new permissions or new endpoints in Part 1, so this is deliberately left as a decision for whoever owns permission/routing policy next, not solved here.

## 8. Legal entity / tenant scoping behavior

- **Tenant-scoped**: unchanged — `ICurrentUser.TenantId` is the only tenant source; no controller/query parameter accepts a tenant id (see architecture test `EmployeesController_List_DoesNotAcceptATenantIdParameter`).
- **Legal-entity-scoped**: now always scoped to exactly one legal entity per call — either the one the query names, or the actor's own default one. This is a genuine behavior change from "coverage computed once, tenant-wide, then optionally filtered by `legalEntityId`" to "coverage resolved per legal entity" — justified in §3 as behavior-preserving for the coverage/company-wide case (coverage records cannot span legal entities) and as a deliberate, documented narrowing only for the one case where it differs: a user with an Employee row in more than one legal entity previously always appeared in their own tenant-wide "self" row regardless of which legal entity's data was being viewed; now they only appear when viewing their *default* legal entity (or a legal entity they explicitly request and have an active Employee row in). This is treated as a fix, not a bug — a "tenant-wide" list with no legal-entity concept doesn't make sense once the list is meant to represent "who is visible within a company," which is what the resolver purpose (`EmployeeListRead`) is for.
- **No cross-legal-entity leakage**: proven by `Handle_Excludes_CrossLegalEntityEmployee_EvenWithCompanyWideCoverage` (unit) and `ListVisibleAsync_RestrictsToGivenIds_IgnoringScope_WhenRestrictToEmployeeIdsIsSet` (repository) — the resolver's own `ListActiveEmployeeIdsByIdsAsync` chokepoint (Part 0) re-filters by legal entity before any id reaches the repository, and the repository additionally restricts to exactly the given id set.
- **No cross-tenant leakage**: proven by `Handle_Excludes_CrossTenantEmployee` (unit, via the resolver's tenant-scoped chokepoint) and the pre-existing `EmployeesListIntegrationTests.List_OnlyReturnsEmployeesBelongingToCallersTenant` (updated for the new resolver, not executed — see §9).

## 8a. Review fixes applied after an advisor pass

An advisor review (using the same tool the systematic-debugging/TDD skills recommend for a design check before declaring done) caught two real issues, one thing worth an explicit verification, and asked me to reconcile rule 5's second clause (folded into §8 above). Both code issues are fixed and covered by new tests; the verification confirmed no bug.

1. **Fail-open degenerate case (fixed).** The handler originally passed `EmployeeVisibilityScope.Unrestricted()` alongside `EmployeeListFilter.RestrictToEmployeeIds` when calling `ListVisibleAsync`. `EfEmployeeRepository.ListVisibleAsync`'s branch is `if (RestrictToEmployeeIds is not null) {...} else if (!scope.CanViewAllTenantEmployees) {...}`, so today's code path is correct (the `RestrictToEmployeeIds` branch always wins when it's non-null) - but if a future refactor ever drops that branch or clears `RestrictToEmployeeIds` while leaving the scope argument as `Unrestricted()`, the query would silently widen to "every tenant employee" instead of failing closed. Changed the handler to pass an explicit non-unrestricted, empty-coverage `EmployeeVisibilityScope` instead - behavior is identical today, but the degenerate case now returns nothing rather than everything. New test: `Handle_NeverPassesUnrestrictedScope_EvenAlongsideRestrictToEmployeeIds` in `ListEmployeesQueryHandlerTests.cs`.
2. **Missing regression guard for the "org:manage never bypasses coverage" rule (fixed).** The rewritten `ListEmployeesQueryHandlerTests.cs` dropped the two mock-based tests that asserted this (the handler no longer branches on `org:manage` at all after the resolver migration, so there was nothing left to mock two ways) - which meant the 2026-08-18 product decision had no test protecting it anywhere. Added `ListEmployeesQueryHandler_NeverChecksOrgManageOrCallsHasPermission` to `ListEmployeesAuthorityResolverArchitectureTests.cs`, asserting the handler source contains neither `"org:manage"` nor `HasPermission` - a stronger guard than the removed mock tests, since it fails the moment anyone reintroduces either, rather than only when a specific scenario is exercised.
3. **`GetDefaultForUserAsync`'s multi-row-no-active-assignment fallback (verified, no change needed).** Confirmed by reading `EfEmployeeRepository.cs:256-278` in full: when a user has 2+ Employee rows and none has an active PrimaryEmployment assignment, `latestEmployeeId` is `Guid.Empty` and the method falls back to `employees[0]` - it never returns `null` when the user has at least one Employee row. So the handler's `legalEntityId is null` short-circuit only triggers for a user with genuinely zero Employee rows anywhere, matching what I'd already documented in §3/§11 - no regression, no code change required.

## 8b. Pre-existing architecture failure fixed (post-report follow-up)

The user asked to fix the one remaining architecture failure documented in §9/§10 below
(`TenantIsolationArchitectureTests.IgnoreQueryFilters_UsageIsExplicitlyAllowlisted`, pre-existing
before Part 0 per that report's §9). Fixed by adding `"EfEmployeeRepository.cs"` to the test's
`allowlistedFileNames` array in
[TenantIsolationArchitectureTests.cs](tests/ONEVO.Tests.Architecture/TenantIsolationArchitectureTests.cs),
with an explanatory comment matching the existing `EfWorkTaskRepository.cs` entry's style: both
`.IgnoreQueryFilters()` call sites in `EfEmployeeRepository.cs`
(`EmployeeNumberExistsAsync`/`GetNextEmployeeNumberSequenceAsync`) already had their own inline
comments explaining why the soft-delete filter is bypassed while tenant scoping is preserved via
an explicit `e.TenantId == tenantId` predicate - this was a missing allowlist entry, not a real
tenant-isolation gap. Architecture suite is now 635/635 passing.

While re-running the full suites to confirm this, one unrelated **flaky** unit test surfaced and
was also fixed: `EfEmployeeRepositoryTests.ListVisibleAsync_AppliesSearchFilter_WithinRestrictToEmployeeIds`
(added in this task, §4) used `NewEmployee`'s default random email
(`{Guid.NewGuid():N}@test.dev`, a 32-character hex string) for both employees; "ada" is a valid
hex substring (a/d are hex digits), so Bob's random email had a small but real chance of
containing "ada" and matching the search filter meant to isolate Ada. Fixed by giving both
employees explicit, non-colliding emails (`ada@test.dev`/`bob@test.dev`). Full unit suite
re-verified green after the fix (2754/2754).

## 9. Tests run

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --configuration Release
  -> 0 Warning(s), 0 Error(s)

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release \
  --filter "EmployeeList|ListEmployees|EmployeeAuthority|EmployeesController"
  -> Passed: 62, Failed: 0   (61 before the advisor-review fix in §8a added one test)

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release   (full suite)
  -> Passed: 2754, Failed: 0

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release
  -> Passed: 635, Failed: 0, Total: 635 (after the §8b allowlist fix - was 634/1/635 before it,
     the same pre-existing failure documented in EMPLOYEE_AUTHORITY_RESOLVER_BACKEND_PART0_REPORT.md
     §9, now resolved)

git diff --check
  -> exit 0. Only pre-existing LF/CRLF advisory warnings on files already modified before this
     task started (per the initial `git status --short --branch`); no errors, nothing in the
     files this task actually changed.
```

TDD notes:
- `EfEmployeeRepositoryTests.cs`'s 2 new restriction tests were run and observed failing (`Expected: 1, Actual: 2` / `Expected: 0, Actual: 1`) against the unmodified repository before `EfEmployeeRepository.ListVisibleAsync` was changed, then observed passing after.
- `ListEmployeesQueryHandlerTests.cs` was rewritten against the old 3-argument constructor first and observed failing to **compile** (`CS1503: cannot convert IEmployeeAuthorityResolver to IEmployeeVisibilityScopeResolver`) before the handler was rewritten, then observed passing (14/14) after.
- `ListEmployeesQueryHandlerAuthorityResolverIntegrationTests.cs` was written after the handler/repository changes were already GREEN under TDD via the two files above; it exercises already-implemented, already-unit-tested logic (the resolver's own rules from Part 0; the repository's `RestrictToEmployeeIds` branch from this task) through real composition rather than mocks, as the task's "at least one integration-style test" requirement — it is a wiring/regression safety net, not itself the driver of new production code, so it was not run RED-first.

## 10. Skipped checks

- **`dotnet test tests/ONEVO.Tests.Integration/...` could not be run.** Two blockers, both re-verified in this session, both identical to Part 0's documented state:
  1. `docker version` → `Error response from daemon: Docker Desktop is unable to start` (Testcontainers has no engine).
  2. Independently, `tests/ONEVO.Tests.Integration.csproj` still fails to compile for a reason unrelated to this task: `CS1503` in `BulkOnboardingValidateTests.cs`/`BulkOnboardingCreateDraftsTests.cs` (`BulkOnboardingRowValidator` vs `IBulkOnboardingValidationRunner`), pre-existing uncommitted work this task did not touch.
- **`EmployeesListIntegrationTests.cs` was updated but not executed.** This file directly constructs `ListEmployeesQueryHandler` and previously passed a hand-built `EmployeeVisibilityScopeResolver`; after this task's constructor change it needed a real `EmployeeAuthorityResolver` instead. I rebuilt it with real EF-backed dependencies (`EfPositionAssignmentRepository`, `EfPositionRepository`, `EfEmployeeHierarchyClosureRepository`, `EfDepartmentRepository`, `EfAuthRepository` for `IPermissionRepository`) and:
  - Seeded an `employment_statuses` row (`Id=1, Code="active"`) — the resolver's `GetByUserAndLegalEntityAsync`/`ListActiveEmployeeIdsAsync`/`ListActiveEmployeeIdsByIdsAsync` all inner-join this lookup table, which `LookupDataSeeder` (an `IHostedService`, not run by this fixture's `MigrateAsync`-only setup) previously never needed to be present because the legacy scope resolver's self-lookup didn't join it and `EfEmployeeRepository.ListVisibleAsync`'s own join is a `LEFT JOIN`.
  - Seeded a real `Role`/`RolePermission`/`UserRole` grant of `employees:read` for the "company-wide caller" fixtures — the legacy `EmployeeVisibilityScopeResolver` never checked permissions at all (only the route did, which this fixture bypasses by calling the handler directly), but `EmployeeAuthorityResolver` gates all managed/company-wide visibility on `IPermissionRepository.UserHasPermissionCodeAsync`.
  - Extended the restricted-role's `GRANT SELECT` to include `roles, role_permissions, user_roles, permissions` (previously ungranted, since nothing in the old code path queried them under the RLS-restricted connection).
  - Traced through each of the 7 existing test scenarios by hand against the new resolver's documented rules (Part 0 report + source read in this session) and left their assertions unchanged where the reasoning showed the same counts should still hold (`List_OnlyReturnsEmployeesBelongingToCallersTenant`, `List_RespectsPageSize...`, `List_SearchFiltersByEmployeeNumber`, `List_FiltersByDepartmentId`, `List_WithoutOrgManage_ReturnsOnlySelf...`) — I could not execute any of them to confirm. **This file compiles cleanly (verified) but is unverified behaviorally.** It is the single highest-priority thing to run the moment Docker/Testcontainers is available in this environment, before trusting its assertions.
  - `GetById_Returns404_ForEmployeeInAnotherTenant` / `GetById_Returns200_ForVisibleEmployeeInCallersTenant` use `BuildGetHandler`, which still uses the legacy `EmployeeVisibilityScopeResolver` unchanged — `GetEmployeeQueryHandler` was not touched by this task, so these two should be unaffected by anything in this task.
  - **Concrete prediction for the first failure to expect**: RLS on `permissions`/`role_permissions`/`user_roles` for the restricted, non-superuser, non-BYPASSRLS test role. This fixture's whole point is exercising real RLS, and I added a plain `GRANT SELECT` for those three tables to the restricted role - but if any of them have a row-level security policy enabled (the way `employees`/`position_assignments` etc. already do, per the fixture's existing pattern), a bare `GRANT` is not sufficient by itself and `UserHasPermissionCodeAsync` would return `false` for every caller even with the correct rows present, making every "company-wide" scenario collapse to self-only. Check RLS policies on those three tables first if `EmployeesListIntegrationTests` fails once Docker is available.

## 11. Remaining risks

- **`EmployeesListIntegrationTests.cs` is unverified against real PostgreSQL/RLS** (§10) — the compile-time fix is sound, but the resolver's actual SQL translation (in particular the new `employment_statuses` inner join and the recursive-CTE department expansion, which cannot run on the EF InMemory provider used by unit tests — see Part 0 report §10/§12), **and** the new `RestrictToEmployeeIds` `WHERE employee.Id IN (@ids)` clause added to `ListVisibleAsync` in this task, have never executed against real Postgres for this handler's code path — the `EfEmployeeRepositoryTests.cs` coverage for it is EF InMemory only. Re-run this file first once Docker is available.
- **New scaling characteristic worth knowing about**: for company-wide (`TargetCompany`) coverage, the resolver's `ListActiveEmployeeIdsAsync(tenantId, legalEntityId, null)` now materializes every active employee id in the legal entity into an in-memory `HashSet<Guid>` and passes the whole set back into `ListVisibleAsync`'s `WHERE Id IN (...)` clause for the paginated query. This mirrors what the legacy scope-based `companyWideLegalEntityIds.Contains(row.legalEntity.Id)` filter already did in terms of "must consider every employee in the legal entity" — it's not a new class of query, just a different shape (an explicit id list instead of a legal-entity-id membership check) — but a legal entity with a very large employee count now pays for materializing that id set on every company-wide-covered caller's list request. Not treated as a blocker (no Phase 1 employee-count target was given), but worth knowing if company-wide coverage is used on a very large tenant.
- **Self visibility is currently unreachable in production for ordinary employees** (§7) — pre-existing, not new, but now formally documented against this endpoint. A permission/routing decision is needed to close it; not attempted here per task rule 10 (no new permissions, no new endpoints).
- **Multi-legal-entity self visibility narrowed** (§8) — a user with Employee rows in more than one legal entity now only sees themselves when viewing their *default* legal entity (or one they explicitly request and belong to), not unconditionally as before. Judged to be correct/intended given the resolver's per-legal-entity design, but flagged explicitly since it is a real behavior change, however narrow.
- **Pending-invited total-count-under-pagination quirk is pre-existing and unchanged**: `toAdd` is appended to whichever page was fetched and `totalCount` is incremented by the same amount for every page, so a second page could double count an invitee already surfaced on page one. This task did not introduce or worsen it; flagging so it isn't mistaken for new.
- **`GetEmployeeQueryHandler`/`GetEmployeeDetailQueryHandler`/`GetMyProfileQueryHandler`/`ListOffboardingOverviewQueryHandler`/`EmployeeOffboardingCoverageGuard` still use the legacy `IEmployeeVisibilityScopeResolver`** — untouched, as instructed (Part 1 is List-only). They retain the legacy direct-only coverage behavior and the "no legal-entity scoping, no permission gate inside the resolver" characteristics described in §2. `EmployeeOffboardingCoverageGuard` in particular was explicitly flagged in Part 0 §11 as duplicated logic that should eventually move onto this resolver too — still true, still out of scope here.

## 12. Next recommended task

**Backend Part 2 — Time Tracking read model**, as specified by the parent task.
