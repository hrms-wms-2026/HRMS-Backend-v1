# Employee onboarding access-grant-request list (Position Approver Inbox) endpoint

## Important: an out-of-band commit landed during this session — read before trusting "files changed"

While building/testing, `git status` unexpectedly stopped showing five of this session's source
files as modified/untracked. Investigation (`git log`, `git reflog`, `git show --stat`) found a
new commit, `a559d02 "employee creation"` (2026-08-11 12:14:52 +0530, author `Thivaharan-25`),
that had absorbed this session's in-progress source changes (controller, repository interface,
EF repository, DTOs, query/handler, and the `DependencyInjection.cs` fix below) into the branch
history. **This session never ran `git commit`, `git add`, or any git write command other than
`git stash`/`git stash pop`** (used twice, purely to prove two things were pre-existing — see
below — and popped back immediately both times). The commit was not something this session
initiated.

This is not a new pattern on this branch: an earlier commit, `f4468ba "employee creation"`
(2026-08-11 10:12:26, same author, same message), shows the identical shape — a large,
heterogeneous batch of report `.md` files and unrelated source changes swept into one commit
under a generic reused message. Both commits are consistent with an automatic
checkpoint/snapshot mechanism in this environment that periodically commits outstanding
working-tree state under the branch's original task title, independent of explicit user commit
requests. This session's own instructions said "do not commit or push unless explicitly asked";
no push occurred (`git rev-list --left-right --count origin/...` shows the branch is exactly 1
commit ahead of `origin/feature/employee-management-phase1-foundation`, 0 behind), so nothing
left this machine, but the commit itself was not requested by the user in this session and is
worth their explicit attention before anything is pushed.

**Practical effect on this report:** the source-file diffs below are described as they exist in
the working tree / branch history right now, not as an "uncommitted patch." Only the test files
remain as plain uncommitted changes (`git status` at time of writing):

```
 M tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/AccessGrantRequestsControllerTests.cs
 M tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/OnboardingPersistenceRepositoryTests.cs
?? tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ListOnboardingAccessGrantRequestsQueryHandlerTests.cs
```

No further git action (reset, amend, force-push, etc.) was taken on the strength of this
finding — undoing a commit is itself a history-modifying action this session's instructions
don't authorize unilaterally. Flagging it here instead so the user can decide.

## Endpoint

`GET /api/v1/onboarding/access-grant-requests` — the preferred route from the task, and it did
not collide with the controller's two existing `POST` actions (`approve-and-send-invite`,
`reject`), so no route deviation was needed.

### Query parameters

| Param | Default | Notes |
|---|---|---|
| `status` | `pending` | `pending`\|`approved`\|`rejected`\|`cancelled`, case-insensitive. Maps to the stored `Pending`/`Approved`/`Rejected`/`Cancelled` literals. `cancelled` is accepted for schema-completeness (the `access_grant_requests.approval_status` column is a free `varchar(20)`, and `phase1-table-inventory.md` documents `Cancelled` as a valid value) even though **no code path in this repository currently writes `Cancelled`** — confirmed by grepping every `ApprovalStatus =` assignment. Any other value is a `400`. |
| `actionType` | `onboarding` | Only `onboarding` is accepted today, mapped to the stored literal `AccessGrantActionType.EmployeeOnboarding` = `"onboarding_position_access"` (verified against the exact literal `FinalizeOnboardingDraftCommandHandler` writes, not just the constant name — the two could plausibly have drifted). Anything else is a `400`, per the task's "must not accidentally list unrelated future access grant request types" requirement — there is no other action type wired in this codebase to accept yet. |
| `page` | `1` | Clamped to ≥ 1. |
| `pageSize` | `25` | Clamped to 1–100. Chosen to match the nearest sibling (`OnboardingDraftsController.List`'s own default of 25), not the `PagedRequest` class's 20 or the activity-snapshot handler's 100 — this project has no single project-wide default; 25 was picked for consistency within the onboarding feature area, not because a project standard mandates it. |
| `search` | `null` | Case-insensitive substring match (`.ToLower().Contains(...)`, not `EF.Functions.ILike`) against: draft `FirstName + " " + LastName`, draft `WorkEmail`, target position `Name`, requested role `Name`. `ILike` was deliberately avoided because it does not translate under the EF Core InMemory provider this repository's own tests use — `.ToLower().Contains()` translates on both InMemory and Npgsql. |
| `legalEntityId` | `null` | Filters on the **draft's** `LegalEntityId` (the only place a legal entity id lives on this data — `AccessGrantRequest` itself has no legal-entity column). |
| `requestedRoleId` | `null` | Filters on `AccessGrantRequest.RequestedRoleId` directly. |

Invalid `status` or `actionType` → `400` via `Result.Failure(..., 400)`, mapped by the existing
`Problem(result.Error, statusCode: result.StatusCode ?? 400)` pattern every other action on this
controller already uses.

## Response shape

```csharp
OnboardingAccessGrantRequestListPageResponse(
    IReadOnlyList<OnboardingAccessGrantRequestListItemResponse> Items,
    int TotalCount, int Page, int PageSize)

OnboardingAccessGrantRequestListItemResponse(
    Guid AccessGrantRequestId, Guid OnboardingDraftId, string Status,
    DateTimeOffset RequestedAt, Guid RequestedByUserId, string? RequestedByName,
    DateTimeOffset? DecidedAt, Guid? DecidedByUserId, string? DecidedByName, string? DecisionNote,
    Guid LegalEntityId, string? LegalEntityName, Guid? DepartmentId, string? DepartmentName,
    Guid TargetPositionId, string? TargetPositionName, Guid PositionAccessTemplateId,
    Guid RequestedRoleId, string? RequestedRoleName,
    string DisplayName, string WorkEmail, DateOnly StartDate,
    string DraftStatus, string? DraftReason, string LastSavedStep)
```

Design decisions on ambiguous fields the task left open:

- **`legalEntityId`/`legalEntityName`** are resolved from the correlated `OnboardingDraft`, not
  from the request itself (the request has no legal-entity column).
- **`departmentId`/`departmentName`** use `AccessGrantRequest.TargetDepartmentId` (the request's
  own, non-nullable, authoritative-for-approver-routing field — set from the position's
  department at finalize time), not the draft's own nullable `DepartmentId`. `departmentId` is
  still typed nullable on the DTO per the task's explicit ask, even though the underlying column
  is not nullable.
- **`decidedByName`/`decidedAt`** are `null` (not `"Unknown"`) when the request is still pending
  — deliberately different from the sibling `DraftListItemResponse.StartedByName`'s `"Unknown"`
  fallback, since here `null` means "not decided yet" and a resolved-but-missing user is a
  different (and here, unreachable) case worth distinguishing.
- No raw invitation token, password, hash, or other security field is included — confirmed by a
  reflection test (below), not just by omission.

## Permission

`[RequirePermission("employees:write")]`, same as `approve-and-send-invite` and `reject` on this
controller and consistent with the class-level doc comment already on this controller: no
permission finer than `employees:write` exists for position-access approval in this codebase (the
userflow doc's `position:approve`/`org:manage` references are not backed by a seeded permission
for this purpose — `PermissionSeeder.cs` has nothing finer). Sibling **read** endpoints in this
codebase (e.g. `OnboardingDraftsController.List`) use `employees:read`, but this is the approver
queue specifically, not a general list view, so `employees:write` was kept per the task's own
instruction rather than silently downgraded to `employees:read`.

## Filtering / correlation behavior

- The draft join is **inner** (`AccessGrantRequests` → `OnboardingDrafts` on `OnboardingDraftId`),
  which is exactly what enforces "only requests correlated to an onboarding draft" — a request
  with `OnboardingDraftId == null` cannot appear in the result set at all, proven by a dedicated
  test (`ListOnboardingRequests_ExcludesRequestsWithoutOnboardingDraftId`).
- Every other join (position, department, legal entity, role, requester, decider) is a **left**
  join via `DefaultIfEmpty()` — a display name failing to resolve degrades to `null`, it never
  drops the row.
- Every joined `DbSet` is pre-filtered to `TenantId == tenantId` before the join (not relying on
  FK-implied scoping), matching the task's explicit tenant-isolation requirement for joined data.
- `page`/`pageSize` are applied at the database level (`Skip`/`Take` before materialization), not
  in memory, and the whole query is `AsNoTracking()`.

## Files changed

**Application**
- `src/ONEVO.Application/Features/CoreHr/Onboarding/DTOs/Responses/OnboardingAccessGrantRequestListItemResponse.cs` (new) — the two response records above.
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/ListOnboardingAccessGrantRequests/ListOnboardingAccessGrantRequestsQuery.cs` (new).
- `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/ListOnboardingAccessGrantRequests/ListOnboardingAccessGrantRequestsQueryHandler.cs` (new) — status/actionType normalization + 400 mapping, page/pageSize clamping, delegates to the repository.
- `src/ONEVO.Application/Features/CoreHr/Onboarding/RepositoryInterfaces/IOnboardingPersistenceRepositories.cs` — added `IAccessGrantRequestRepository.ListOnboardingRequestsAsync(...)`.

**Infrastructure**
- `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfOnboardingPersistenceRepositories.cs` — `EfAccessGrantRequestRepository.ListOnboardingRequestsAsync` (tenant-scoped joins, filters, search, pagination, projection).
- `src/ONEVO.Infrastructure/DependencyInjection.cs` — **pre-existing build break fixed to enable verification** (see next section), unrelated to this feature's own DI (both `IAccessGrantRequestRepository` and the new query were already correctly wired; no new DI registration was needed for this task).

**Api**
- `src/ONEVO.Api/Controllers/Tenant/CoreHr/AccessGrantRequestsController.cs` — new `[HttpGet]` `List` action.

**Tests**
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/OnboardingPersistenceRepositoryTests.cs` — 7 new test methods (12 test cases with `[Theory]` expansion) covering the repository method.
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ListOnboardingAccessGrantRequestsQueryHandlerTests.cs` (new) — 9 test methods (14 test cases) covering the handler, including 2 reflection-based security tests.
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/AccessGrantRequestsControllerTests.cs` — 5 new test methods covering the controller action, including a permission-attribute reflection test.

**No migration.** This is a read-only query against existing tables/columns; no schema changed.

## Pre-existing build break fixed to enable verification

`ONEVO.Infrastructure` did not build at the start of this session's verification pass —
independent of any of this session's own changes. Proven by stashing this session's changes
(`git stash -u`) and rebuilding on bare HEAD: the identical 5 errors reproduced:

```
DependencyInjection.cs(158,28): error CS0104: 'IEmployeeRepository' is an ambiguous reference between
  'ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository' and
  'ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository'
DependencyInjection.cs(158,49): error CS0104: 'EfEmployeeRepository' is an ambiguous reference between
  'ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository' and
  'ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository'
(...and the same pair again at lines 195/196)
```

Root cause: two unrelated features on this branch each independently added a type named
`IEmployeeRepository` (`ONEVO.Application.Common.RepositoryInterfaces` — a lightweight
`GetByUserIdAsync`/`GetByUserIdsAsync` lookup interface used by Work Management for
milestone/achievement owner-name resolution — versus
`ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces` — the richer Core HR employee
CRUD interface with `AddAsync`/`SaveChangesAsync`, added for onboarding finalize's employee
creation), each with its own same-named `EfEmployeeRepository` implementation in a different
folder. `DependencyInjection.cs` has `using` directives for both interface namespaces and both
implementation namespaces, so the two short-named registrations became ambiguous.

Fix: fully-qualified the two ambiguous registration sites (no aliasing, no behavior change). The
pairing was verified by reading which interface each `EfEmployeeRepository` class actually
implements (not inferred from folder names, which would have been unsafe — a wrong pairing would
still compile but silently wire the wrong implementation):
- `ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository` implements
  `ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository` (confirmed
  via its own `using` statement and its `AddAsync`/`SaveChangesAsync` members).
- `ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository` (root, no `CoreHr`
  subfolder) implements `ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository`
  (confirmed the same way; its doc comment on `GetByUserIdsAsync` explicitly references Work
  Management milestone owner-name resolution, matching where it's registered in the DI file).

No repository source file was touched, no type was renamed or deleted, and the underlying
duplicate-interface-name design is left exactly as found — it works correctly once
disambiguated, but two independently-evolved `IEmployeeRepository` interfaces/implementations
existing side-by-side is a design smell an owner should decide whether to consolidate. Out of
scope to fix further here.

After this fix: `ONEVO.Domain` → `ONEVO.Application` → `ONEVO.Infrastructure` → `ONEVO.Api` →
`ONEVO.Tests.Unit` → `ONEVO.Tests.Architecture` → `ONEVO.Tests.Integration` all build with 0
errors. A separate, unrelated stray `ONEVO.Api.exe` process (a live dev-server instance, not a
leftover build artifact) was locking the normal `bin\Debug` output for `ONEVO.Api`; the user was
asked and approved stopping it before the normal-output build was retried successfully.

## Verification

All commands from `C:\onevoNew\HRMS-Backend-v1`. `ONEVO.sln` does not exist in this repo
(consistent with every prior report on this branch); built each project individually.

- `dotnet build src\ONEVO.Domain\ONEVO.Domain.csproj --no-restore --verbosity minimal` → 0 errors.
- `dotnet build src\ONEVO.Application\ONEVO.Application.csproj --no-restore --verbosity minimal` → 0 errors.
- `dotnet build src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --no-restore --verbosity minimal` → 0 errors (after the DI fix above).
- `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` → 0 errors (after stopping the locking dev-server process, with the user's confirmation).
- `dotnet build tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal` → 0 errors, only pre-existing unrelated warnings (`TenantRlsInterceptorTests`, `GetPositionTreeQueryHandlerTests`, `PermissionSeederTests`, `SQLitePCLRaw` NU1903 advisory).
- `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-build --filter "FullyQualifiedName~AccessGrant|FullyQualifiedName~Onboarding|FullyQualifiedName~Approval" --verbosity minimal` → **126/126 passed.**
- Non-stale check, filtered to exactly this session's new test classes/methods
  (`FullyQualifiedName~ListOnboardingAccessGrantRequestsQueryHandlerTests|FullyQualifiedName~ListOnboardingRequests`) with per-test names logged → **26/26 passed, all individually listed by name** (12 repository test cases + 14 handler test cases). A separate run filtered to `AccessGrantRequestsControllerTests.List` → **5/5 passed.** (31 new test cases total; a stale-DLL run would have matched 0, as prior sessions on this branch have caught before.)
- Full `ONEVO.Tests.Unit` suite (no filter) → **1923/1923 passed**, 0 regressions.
- `dotnet build tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal` → 0 errors.
- `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-build --verbosity minimal` → **555/555 passed, 0 failed.** No new architecture-guardrail violations; no brittle "latest migration" assertion was added (none was needed — this task added no migration).
- `git diff --check` → exit 0; only pre-existing LF→CRLF line-ending warnings (Windows checkout artifact) on files this session touched, no real whitespace/conflict errors.
- `dotnet build tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --verbosity minimal` → 0 errors (only pre-existing `Testcontainers.PostgreSqlBuilder` obsolete-constructor warnings).
- Docker: **available** in this environment (`docker info` succeeded) — unlike every prior report on this branch.
  `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-build --filter "FullyQualifiedName~AccessGrant|FullyQualifiedName~Onboarding" --verbosity minimal` → **3 passed, 1 failed** (all 4 are pre-existing `OnboardingDraftsIntegrationTests`; none exercise the new list endpoint — no integration test file targets `AccessGrantRequestsController` today). The 1 failure,
  `Handle_AlwaysResultsInADraftStatus_NeverFinalized`, is **pre-existing and unrelated**: reproduced identically (same assertion, same expected/actual values) by stashing this session's changes and re-running against bare HEAD. Root cause (not investigated further — out of scope): the test's own comment says it assumes "the seat service always returns Undetermined today," but the live seat-entitlement service now apparently returns a different result in this environment, an unrelated pre-existing test/implementation drift on `SaveOnboardingDraftCommandHandler`, a file this session never touched.
- `dotnet ef migrations list` / `has-pending-model-changes` — not run; no migration was added or needed (read-only query against existing schema), so there is nothing for either check to catch.

## Skipped / not added

- No new `ONEVO.Tests.Integration` test file for the list endpoint itself. The task's verification
  step asked to *run* the existing focused integration-test filter if Docker is available (done,
  above) — it did not separately require a new Testcontainers-backed test for this endpoint, and
  the repository method is already exercised against a real EF Core provider translation (though
  InMemory, not Npgsql) by the 7 new repository unit tests, matching this codebase's own
  established pattern for repository-level coverage in `OnboardingPersistenceRepositoryTests.cs`.
  If deeper Postgres-specific coverage (RLS on the joined tables, `ILIKE`-vs-`.ToLower()` search
  behavior under real Postgres collation) is wanted, that would be a reasonable follow-up.
- No new architecture-test file. The task's architecture-test checklist items (repository
  interface in Application, EF implementation in Infrastructure, no Application→Infrastructure
  dependency, no brittle latest-migration assertion) are all satisfied by construction and by the
  existing project-wide architecture-test suite passing (555/555, 0 new failures) — a
  feature-specific architecture test would have been redundant with guardrails that already exist
  and already cover this dependency direction.

## Frontend contract for Part 5 approval queue UI

```
GET /api/v1/onboarding/access-grant-requests?status=pending&actionType=onboarding&page=1&pageSize=25
  &search=...&legalEntityId=...&requestedRoleId=...
```
- Auth: tenant session + `employees:write`. No `tenantId` accepted anywhere in the request —
  confirmed by two reflection tests (`Query_HasNoTenantIdProperty`,
  `List_HasNoTenantIdParameter`), not just by inspection.
- Success `200`: `{ items: [...], totalCount, page, pageSize }`, items shaped per the DTO above.
  `decidedAt`/`decidedByUserId`/`decidedByName`/`decisionNote` are all `null` for a still-pending
  row.
- `400` (`ProblemDetails`) for an unrecognized `status` or `actionType` — the error message lists
  the allowed values.
- No other error shapes are reachable from this endpoint (it does no writes, so none of the
  404/409/422 cases the approve/reject endpoints have apply here).
- No raw invitation token or other security-sensitive field is ever present in the response.
- Approving/rejecting a row still goes through the existing
  `POST .../approve-and-send-invite` / `POST .../reject` endpoints (unchanged by this task) —
  this list endpoint is read-only and does not itself change any request's state.

## Remaining risks

1. **The out-of-band commit described at the top of this report.** Not something this session
   caused directly, but the user should look at `a559d02` and the branch's commit hygiene before
   pushing anything.
2. **The duplicate `IEmployeeRepository` design** (two same-named interfaces, two same-named EF
   implementations) still exists; this session only disambiguated the two call sites that failed
   to compile. A future consolidation decision is owed.
3. **The pre-existing `Handle_AlwaysResultsInADraftStatus_NeverFinalized` integration-test
   failure** is real, reproducible, and unrelated to this task, but still unfixed — flagged above,
   not silently left implied-fixed.
4. **No Postgres-specific integration coverage for the new query** (RLS across the five joined
   tenant-scoped tables, real-Postgres search collation) — the unit tests cover translation
   correctness against EF Core InMemory only, consistent with this codebase's existing pattern for
   this repository, but not equivalent to a live-Postgres check.
5. **`cancelled` status filtering is accepted but currently always empty** in production, since no
   code path sets `ApprovalStatus = "Cancelled"` — this is intentional forward-compatibility, not
   a bug, but worth the frontend knowing before building a "Cancelled" tab that will always be
   blank today.
