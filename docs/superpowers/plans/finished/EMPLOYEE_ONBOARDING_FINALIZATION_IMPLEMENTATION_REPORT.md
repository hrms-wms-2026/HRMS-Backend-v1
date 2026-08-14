# Employee onboarding finalization implementation

## Endpoint added

`POST api/v1/onboarding/drafts/{id}/finalize` on `OnboardingDraftsController`, gated by
`[RequirePermission("employees:write")]` under the existing `[Authorize(Policy = "TenantPolicy")]`
class-level policy. No request body; `id` comes from the route, tenant from `ICurrentUser`.

The route deviates from the task's suggested `api/v1/core-hr/onboarding-drafts/{draftId}/finalize`
because the controller's actual existing base route is `api/v1/onboarding/drafts` (not
`api/v1/core-hr/onboarding-drafts`) — the task said to prefer the suggested route "if consistent",
and it wasn't, so existing convention won.

## A load-bearing decision made with you mid-task

Partway through, I found the task's own §3 (create the employee lifecycle eagerly, defer only the
role) contradicts the authoritative userflow doc `employee-onboarding.md`'s "Sensitive Position
Approval" section (defer *everything* — user, employee, checklist, invite — until a separate,
not-yet-built approval-acceptance flow runs). I asked; you chose **defer everything**. Every design
decision below follows from that.

One consequence you should know about: `AccessGrantRequest` was schema-shaped for the *other*
reading (non-nullable `EmployeeId`/`UserId` FKs). Making the request record something coherent
under "defer everything" required a real schema change — see below — not just handler logic.

## Files changed

**Schema / entities**
- `UserRole.cs` + `UserRoleConfiguration.cs` — added `SourcePositionId`, `SourcePositionAccessTemplateId` (both nullable). New migration `20260810161000_AddUserRoleSourcePositionTracking` (the `user_roles` table predates this session's uncommitted migrations, so this had to be a new migration, not an edit).
- `AccessGrantRequest.cs` + `AccessGrantRequestConfiguration.cs` — `EmployeeId`/`UserId` changed to nullable; added nullable `OnboardingDraftId` (the correlation key while a request is pending, since the employee/user don't exist yet). The pending-request partial unique index moved from `(tenant_id, employee_id, ...)` to `(tenant_id, onboarding_draft_id, ...)`. Also added `AccessGrantActionType.EmployeeOnboarding` constant.
- `OnboardingDraft.cs` — added `OnboardingDraftReason.InvitationSent`.
- `20260810160000_AddEmployeeOnboardingPhase1PersistenceContracts.cs` — edited in place (it's uncommitted and untracked on this branch, same rationale as the prior session's own edits to earlier same-day migrations) to make `employee_id`/`user_id` nullable, add `onboarding_draft_id`, and repoint the unique index.
- `ApplicationDbContextModelSnapshot.cs` — updated for both entities.

**Application layer**
- `IEmploymentTypeRepository` (new) + `EfEmploymentTypeRepository` (new) — resolves `OnboardingDraft.EmploymentType` (a free-text code) to `Employee.EmploymentTypeId` (int). Nothing like this existed; `SaveOnboardingDraftCommandValidator` never validated the code against the lookup table.
- `IEmployeeRepository` — added `AddAsync`/`SaveChangesAsync` (it only had read/existence methods before).
- `IAccessGrantRequestRepository.GetPendingAsync` renamed to `GetPendingByDraftAsync`, re-keyed on `onboardingDraftId` instead of `employeeId`.
- `FinalizeOnboardingDraftCommand`, `FinalizeOnboardingDraftCommandHandler`, `FinalizeOnboardingDraftResponse` (all new) under `Features/CoreHr/OnboardingDraft/Commands/FinalizeOnboardingDraft/`.

**Infrastructure**
- `EfEmployeeRepository` — `AddAsync`/`SaveChangesAsync` implementations.
- `EfOnboardingPersistenceRepositories.cs` — `EfAccessGrantRequestRepository.GetPendingByDraftAsync`.
- `DependencyInjection.cs` — registered `IEmploymentTypeRepository`.

**Api**
- `OnboardingDraftsController.Finalize`.

**Tests**
- `FinalizeOnboardingDraftCommandHandlerTests.cs` (new, 20 tests).
- `OnboardingDraftsControllerTests.cs` — 4 new `Finalize` tests.
- `OnboardingPersistenceRepositoryTests.cs` — updated the one test that called the renamed `GetPendingAsync`.

## Validation behavior

Reuses the exact validation shape `SaveOnboardingDraftCommandHandler` already established (legal
entity active + tenant-scoped, department active + belongs to the legal entity, position active +
belongs to legal entity/department, work mode active), plus what only finalize needs: employment
type resolves to a real lookup row, work email is non-empty and RFC-shaped, employee number is
non-empty (see gap below) and tenant-unique, employee email is tenant-unique, and — new — position
capacity (`PositionAssignment` active count vs. `Position.MaxOccupancy`) and "position has no
department" (defends the access-grant request's non-nullable `TargetDepartmentId`).

**Gap, not silently patched:** the userflow doc says employee number is "required when not
auto-generated"; no auto-generation policy exists in this codebase, so it's always required here.
Flagged rather than invented.

**Gap, not silently patched:** `Employee.Email`/`User.Email` are `varchar(255)` and
`InvitationToken.InvitedEmail` is `varchar(254)`, but `SaveOnboardingDraftCommandValidator` allows
work email up to 320 chars. An email in the 255–320 range would pass draft save and then hit a raw
`DbUpdateException` at finalize. **Corrected in the verification pass below:** this is no longer
caught and mapped to a 409 — it now propagates unhandled and becomes a bare 500 via
`ExceptionHandlerMiddleware`'s default case, which is worse than originally described here, not
better. Out of scope to fix here — the validator isn't part of this endpoint — but the frontend
must be told this returns 500, not 409, until it is.

## Seat behavior

`Blocked` → draft moves to `WaitingForSeat`/`WaitingForSeat` and that alone is saved (permitted
explicitly by the task's own §9 carve-out: "not unless the seat decision is known blocked and no
partial records were created" — true here, nothing else was staged yet); returns 409. `Undetermined`
→ 422, draft left completely untouched (no `SaveChangesAsync` call at all). `Approved` → proceeds.

Ordering note: position-approval is checked *before* the seat recheck (matching
`SaveOnboardingDraftCommandHandler`'s established precedence), not before it as the task's own
numbering implied. When approval is required, seat is never evaluated — nothing seat-consuming is
being committed in that branch anyway.

## User/employee lifecycle

Only reached when `RequiresApproval` is false (see the mid-task decision above).
- Existing tenant user with the same email → reused, never duplicated (`IUserRepository.GetByTenantAndEmailAsync`).
- No existing user → creates one with `IsActive = false`, `EmailVerified = false`, `MustChangePassword = true`, `PasswordHash = string.Empty` (mirrors `TenantOwnerInvitationService`'s own pending-user shape, without touching that service).
- `Employee.EmploymentStatusId` stays at its default (1/"active") — no "onboarding" row exists in the tenant-wide `employment_statuses` lookup (it's read app-wide with an `"active"` fallback), so pending-ness is carried entirely by `User.IsActive = false`, `InvitationToken.Status = "pending"`, and the draft's own `Status`/`FinalizedAt`. This is the exact field-mapping documentation the task asked for if the User entity lacked a pending distinction — it doesn't lack one; `IsActive` is it.

## Position assignment behavior

Created only in the non-approval path, only if `PositionId` is set: `AssignmentKind = PrimaryEmployment`, `EffectiveFrom = draft.StartDate`, `AssignmentStatus = Active`. Capacity is checked earlier (see Validation) using `Position.MaxOccupancy` — the only capacity signal exposed by the repository layer.

## Role/access behavior

- No access template → no role assignment, no default Owner/Admin/Employee role (never invented).
- Template, `RequiresApproval = false` → `UserRole` created with `RoleId = template.RoleId` and the new `SourcePositionId`/`SourcePositionAccessTemplateId` fields set. No effective-date field exists on `UserRole` beyond `AssignedAt`/`ExpiresAt`; nothing was added there since the task only asked for it "if current schema supports it."
- Template, `RequiresApproval = true` → per the mid-task decision, only an `AccessGrantRequest` is created (`EmployeeId`/`UserId` null, correlated by `OnboardingDraftId`); draft moves to `WaitingForPositionApproval`; nothing else touches the DB. The idempotency guard (`GetPendingByDraftAsync`) prevents a second pending row on repeat calls.

## Checklist behavior

If `SelectedTemplateId` is set, loads the template scoped to tenant + department via
`GetActiveOnboardingAsync` (not found/inactive/wrong-scope → 422). `EfEmployeeChecklistTaskRepository.InstantiateAsync`
already existed and already rejects task JSON missing `title`/`ownerType`/`assignedToId`/`dueDate`
with `ArgumentException`, mapped here to 422 — nothing is persisted before this runs (it only
stages `EmployeeChecklistTask` rows on the shared `DbContext`; the single end-of-handler
`SaveChangesAsync` is what actually commits). Edited JSON (`draft.EditedTasksJson`) takes priority
over the template's own `TasksJson` — that priority is `InstantiateAsync`'s existing behavior, not
new. **Documented limitation carried over from the existing repository, not introduced here:**
`GetActiveOnboardingAsync` only scopes by tenant + department; `ChecklistTemplate` has no
`LegalEntityId`/`PositionId` columns, so the task's "applicable legal entity/department/position"
scoping is only partially representable.

## Invitation/outbox behavior

Only in the non-approval path. Reuses `InvitationTokenHasher` + `ISecureTokenGenerator` (both
pre-existing, used by `TenantOwnerInvitationService` for the same purpose — not reused directly,
mirrored). `Purpose = InvitationToken.EmployeeOnboardingPurpose`, 72-hour expiry (matched to
`TenantOwnerInvitationService.InviteValidityHours`; no employee-specific value was documented
anywhere I found). Queued via the existing `EmployeeOnboardingInviteEmailOutboxHandler`/
`OutboxMessageTypes.EmployeeOnboardingInviteEmail` — no direct email send. Raw token is not
returned in the response, matching this handler's own pattern of never exposing it via API.

## Transaction/concurrency behavior

Every branch stages all its writes on the same scoped `ApplicationDbContext` (via the different
repositories, all constructed over the same DbContext instance) and calls `SaveChangesAsync`
exactly once, at the very end — same pattern as `CreatePositionCommandHandler`. A concurrency
conflict on the draft's own `xmin` (or a unique-constraint race, e.g. two simultaneous finalizes
racing on employee number/email/pending-access-grant) causes the *entire* transaction to fail
atomically; nothing partial is left behind. `ConcurrencyConflictException` → 409 with a clean
message. **Corrected in the verification pass below:** the generic `DbUpdateException → 409`
mapping described here originally does not compile (Application layer has no compile-time EF Core
reference) and was removed; any other `DbUpdateException` now propagates unhandled and is mapped
by `ExceptionHandlerMiddleware`'s default case to a bare **500** (no ProblemDetails-friendly
message), not a 409. Repeated finalize on an already-`Finalized` or already-`WaitingForPositionApproval` draft
is rejected before any write is staged.

## Tests added/updated

20 handler tests (`FinalizeOnboardingDraftCommandHandlerTests.cs`) covering: missing draft (404),
already-finalized/cancelled/waiting-for-approval (409), invalid legal entity/department/position/work
mode (422), duplicate email/employee number (409), seat Approved (creates user+employee+token+outbox),
seat Blocked (creates nothing, sets WaitingForSeat), seat Undetermined (creates nothing, no save at
all), no template (no role), template without approval (role with source fields), template requiring
approval (grant request only, everything else deferred — including asserting seat evaluation is
never even called), repeated finalize while pending (no duplicate grant request), checklist template
creates tasks, edited JSON overrides template JSON, missing `assignedToId` rejects before any write,
token purpose is `employee_onboarding` not `general`, and a reflection guard that the constructor
never takes a `TenantOwner*` dependency. 4 new controller tests. 1 existing repository test updated
for the renamed method.

**Test list items not literally separate under "defer everything":** the required list's "seat
Approved creates pending user + employee + token + outbox" and the checklist/token/outbox items all
only exercise the non-approval path now, since under your ruling nothing exists to create in the
approval-pending path except the grant request itself — that's exactly what the dedicated
"AccessTemplateRequiringApproval" test asserts (grant request created, everything else `Times.Never`).

## Tests run

- `git diff --check` — passed (only LF/CRLF warnings on files this session already had modified, no errors).
- Docker — available.

## Blocked / could not verify — read this before trusting anything above compiled

**I could not compile or run this code.** `dotnet build`/`dotnet test` on every project (`ONEVO.Infrastructure`,
by extension `ONEVO.Api`, `ONEVO.Tests.*`) fails immediately (~0.4s, before touching any source) with:

```
error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.
error NU1301:   An attempt was made to access a socket in a way forbidden by its access permissions. (api.nuget.org:443)
```

Tried and ruled out: `--no-restore` alone, `--no-restore -p:NuGetAudit=false`, `--no-restore
-p:RestorePackagesPath=<local packages folder>`, and running the exact same command with the tool
sandbox explicitly disabled (identical failure, confirming this is an OS/environment-level socket
permission denial, not this session's own sandboxing). This matches the exact blocker four prior
sessions on this branch already recorded in their own reports — pre-existing, not something I
introduced or could work around. `ONEVO.sln` does not exist at the repo root (confirmed), so the
`--no-restore` on a solution file wasn't an option either way.

`dotnet test ... --no-build` "passed" (139 unit tests, 548 architecture tests) — **but this is
misleading and I'm flagging it rather than reporting it as verification.** `--no-build` silently
ran a *stale* compiled DLL (`ONEVO.Tests.Unit.dll`, last written 14:08, hours before this session's
changes) that predates every file this session touched. A targeted filter for
`FullyQualifiedName~FinalizeOnboardingDraft` matched zero tests in that DLL — my new test file
isn't in it. Do not read the 139/548 pass counts as evidence this session's code is correct; they
only confirm the *pre-existing* suite was green before I started.

**Net effect: everything above is verified by careful manual code/type review against the actual
repository interfaces I read (cross-checked every method signature, property, and namespace against
the real files), not by a compiler.** I caught and fixed one real bug this way already (`AccessGrantActionType`
constant initially exceeded the `varchar(30)` column) and one real Moq bug in my own test file
(verifying a generic `EnqueueAsync<TPayload>` call with `It.IsAny<object>()` instead of the concrete
payload type, which would have either always-failed or always-trivially-passed depending on
direction). I'm confident in the review but it is not a substitute for an actual build.

- Integration tests: not run (blocked by the same build failure; Docker being available doesn't help since the test binary can't be produced).
- Architecture tests: same — not meaningfully run (stale DLL only).

## Remaining blockers

1. **Build verification is owed.** The moment NuGet access is restored (or an offline package
   source is configured), run: `dotnet build src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj
   --no-restore` (then Application/Api/Tests), and re-run the unit/architecture test commands for
   real. Treat this implementation as unverified until that happens.
2. **The "approve & send invite" flow this design depends on doesn't exist yet.** A pending
   `AccessGrantRequest` (keyed by `OnboardingDraftId`) is the only artifact produced when approval
   is required. Nothing currently consumes it — a future endpoint needs to: load it, re-validate
   the draft is still finalizable, create the user/employee/position-assignment/checklist/invitation
   in one transaction (same shape as `FinalizeImmediatelyAsync` here), create the `UserRole` from
   `RequestedRoleId`, and mark the request `Approved`/`Rejected`.
3. Employee number auto-generation and the WorkEmail length mismatch (documented above) are
   pre-existing gaps this endpoint surfaces but doesn't fix.
4. No RLS/integration test coverage for the new `access_grant_requests.onboarding_draft_id` index
   or the `user_roles` source columns — blocked by the same build issue.

## Verification pass (2026-08-10, follow-up session)

The blockers in the previous section were investigated and resolved. Everything below was run for
real against freshly rebuilt binaries — this supersedes every "could not compile" caveat above.

### Restore / build

`ONEVO.sln` does not exist anywhere in this repo (confirmed again — not a regression, never
existed). The prior NU1301 network blocker was **not reproduced** in this session: each project's
`obj/*.nuget.g.*` files already existed from an earlier restore, so per-project restore
(`dotnet restore src\ONEVO.Api\ONEVO.Api.csproj`, then the three test projects) succeeded entirely
from the local NuGet cache with no network access needed. Whatever blocked NuGet in the prior
session is either environment-specific or transient; it is not currently blocking.

`dotnet build` then surfaced **three real, pre-existing compile errors** in the finalization code
(the "verified by manual review, not a compiler" caveat above was correct to be worried — the
manual review missed these):

1. **`FinalizeOnboardingDraftCommandHandler.cs` referenced `Microsoft.EntityFrameworkCore`
   (`DbUpdateException`) directly.** `ONEVO.Application.csproj` only carries
   `Microsoft.EntityFrameworkCore.Design` with compile assets excluded — Application is
   intentionally persistence-ignorant. Fix: removed the `using` and the generic
   `catch (DbUpdateException)` block, matching `SaveOnboardingDraftCommandHandler`'s existing
   pattern (only `ConcurrencyConflictException` is caught there either).
   **Behavioral consequence, checked against `ExceptionHandlerMiddleware.cs`:** a raw
   unique-constraint race that isn't a concurrency conflict now propagates unhandled and falls
   into that middleware's default case → **bare 500**, not a 409. This is the *same* behavior
   `SaveOnboardingDraftCommandHandler` already has (so it's not a new class of bug in this
   codebase), but it is a real regression from what this report originally documented for
   `finalize` itself (see the corrected "Transaction/concurrency behavior" and email-length-gap
   sections above) — flagging clearly rather than letting the earlier, inaccurate "mapped to a
   clean 409" claims stand.
2. **`'Employee' is a namespace but is used like a type` / same for `PositionAssignment`.**
   `ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces` and
   `...CoreHr.PositionAssignment.RepositoryInterfaces` are real namespaces in this codebase; because
   the handler's own namespace nests under `...CoreHr`, C#'s namespace-member lookup finds the
   sibling namespace segment `Employee`/`PositionAssignment` before the `using`-imported domain
   entity type of the same name (this is exactly the reason the file already had an
   `OnboardingDraftEntity` alias for the analogous `OnboardingDraft` collision — the author caught
   that one but missed these two). Fix: added `EmployeeEntity`/`PositionAssignmentEntity` aliases
   next to the existing one, used at both instantiation sites.
3. **The test file `FinalizeOnboardingDraftCommandHandlerTests.cs` has the identical collision**
   (a sibling `Tests.Unit/Features/CoreHr/Employee` folder exists), plus a cascading
   `'Employee?' does not contain 'UserId'` error from the same root cause. Fix: same alias pattern
   applied to the test file.
4. **`OnboardingPersistenceRepositoryTests.cs` was missing `using FluentAssertions;`** — `.Should()`
   calls didn't resolve (FluentAssertions is not a project-wide global using here, only `Xunit`
   is). Fix: added the import.
5. **`CreatePositionCommandHandlerTests.cs` didn't compile against the current
   `CreatePositionCommandHandler` constructor**, which now requires an `IRoleRepository roles`
   argument. This is caused by an unrelated, already-uncommitted change in this branch (position
   access-template creation on `CreatePosition`, not part of this finalization work) — but it
   blocks the entire `ONEVO.Tests.Unit` assembly from compiling, so no test in the project can run
   until it's fixed. Fixed minimally: added an `IRoleRepository` mock and passed it through. No
   production code touched for this one.

A sixth issue was environmental, not code: a stray `ONEVO.Api.exe` (leftover dev-server process,
PID 15148) was locking the build output DLLs, failing the Api project's copy step with MSB3027
after compilation had already succeeded. Stopped with the user's explicit confirmation; rebuild
then succeeded cleanly.

**Result: `ONEVO.Domain` → `ONEVO.Application` → `ONEVO.Infrastructure` → `ONEVO.Api` →
`ONEVO.Tests.Unit` → `ONEVO.Tests.Architecture` → `ONEVO.Tests.Integration` all build with 0
errors** (Integration was built separately after the first pass, since `git status` shows it has
its own modified files — `CapturingEmailService.cs`, `OnboardingDraftsIntegrationTests.cs` — that
could plausibly have broken against the changed `IEmailService` interface; they didn't). Remaining
warnings are pre-existing and unrelated (nullable-reference warnings in `AdminAuthController`,
`TenantRlsInterceptorTests`, `PermissionSeederTests`, `GetPositionTreeQueryHandlerTests`;
`SQLitePCLRaw` NU1903 advisory; obsolete-constructor warnings from `Testcontainers.PostgreSqlBuilder`
in several integration test files).

### Dependency-injection verification

The 22 handler tests construct `FinalizeOnboardingDraftCommandHandler` with 19 mocks directly, so
they cannot catch a missing DI registration — checked separately by reading
`DependencyInjection.cs` in both `Application` and `Infrastructure`:
- `IWorkModeRepository` → `EfWorkModeRepository` and `IEmploymentTypeRepository` →
  `EfEmploymentTypeRepository` are both registered (`Infrastructure/DependencyInjection.cs`).
  (Correction: the "Files changed" section above only mentions registering
  `IEmploymentTypeRepository`; `IWorkModeRepository` is also registered and was likely already
  present from earlier draft-save work — both are confirmed present regardless.)
- `EmployeeOnboardingInviteEmailOutboxHandler` is registered as `IOutboxMessageHandler`
  (`Application/DependencyInjection.cs:47`), so the outbox message this handler enqueues has a
  live consumer — the "outbox-based email only" behavior is wired end to end, not just enqueued
  into a void.

### Tests run against current binaries

- `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-build --filter
  "FullyQualifiedName~Onboarding|...|FullyQualifiedName~SeatEntitlement"` →
  **169/169 passed.** Confirmed non-stale: the filtered `FinalizeOnboardingDraft` subset alone
  (`--filter "FullyQualifiedName~FinalizeOnboardingDraft"`) matched and passed **22/22** — the
  previous session's stale-DLL run matched *zero*, so this is the first real execution of this
  test file. The 4 controller tests were confirmed separately with
  `--filter "FullyQualifiedName~OnboardingDraftsControllerTests.Finalize"` → **4/4 passed**
  (the `FinalizeOnboardingDraft` filter above only matches the handler-test class name, not the
  controller-test class, so this needed its own run to actually verify the "4 controller tests"
  claim rather than assume it from the 169 total).
- `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-build` →
  **545/548 passed, 3 failed** at the time this section was written. **Corrected in the
  "Backend verification blockers cleanup pass" section below: these were not left untouched —
  a later session fixed them by removing `RoleId`/`RequiresApproval` from `CreatePositionRequest`/
  `CreatePositionCommand`, since the dedicated `PUT /positions/{id}/access` endpoint
  (`SetPositionAccessCommand`) already provides this exact capability. That later section also
  documents a countervailing prior report that had called this addition deliberate, and explains
  why the removal was judged correct anyway — read it before treating this bullet's original
  "left untouched per scope" framing as current.**
- `git diff --check` → only pre-existing LF/CRLF warnings, no errors (same as previously recorded).

### Migration / model checks

- Migrations and `ApplicationDbContextModelSnapshot.cs` compile as part of the `Infrastructure`
  build above.
- `dotnet ef migrations has-pending-model-changes` **does not actually need a live database** —
  Npgsql only parses the connection string at DbContext construction, it doesn't connect. Re-run
  with a syntactically-valid dummy `ConnectionStrings__MigrationConnection` env var and it worked.
  **Result: it reports pending model changes exist.** To see what, `dotnet ef migrations add
  TempDiffProbe` was run and the generated migration inspected, then deleted (never committed —
  confirmed via `git status` that no trace remains). The diff contains:
  - `work_modes.is_active` — the tool wants to add it again with a *different* default
    (`false`) than the migration that already added it (`true`, in
    `20260810153000_CorrectOnboardingDraftIdentityAndWorkMode.cs`). The entity, its configuration,
    and the snapshot are all mutually consistent by direct inspection (all three agree: required
    `bool`, no explicit default annotation) — no root cause for this specific line was found by
    code reading. **Not resolved; flagged, not fixed.**
  - `subscription_plans` ⇄ `tenant_subscriptions` seat columns (`included_seats`,
    `overage_allowed`) and `approval_statuses.is_active` — these tables are untouched by this
    finalization work entirely; they belong to other already-uncommitted work on this branch
    (subscription/billing). Out of scope to fix here.
  - Several FK-constraint and index **rename** operations (e.g.
    `fk_access_grant_requests_position_access_templates_position_access_template_id`, an
    `ix_employee_checklist_tasks_...` index) that look like PostgreSQL 63-char identifier
    truncation differing between tool versions, not real schema drift — the `dotnet-ef` CLI
    tool (10.0.7) is older than the project's EF Core runtime (10.0.9), and the tool printed its
    own warning about this on every invocation. Plausible but **not confirmed**; would need the
    tool upgraded (`dotnet tool update -g dotnet-ef`) and re-run to separate real drift from
    tooling noise.
  **This contradicts the "no mismatch found" characterization this section originally had** before
  the dummy-connection-string trick was found — that claim was based on manual code reading alone,
  which this probe shows was insufficient for at least the `work_modes.is_active` case. A
  maintainer with the tool upgraded and a real Postgres instance should re-run this before treating
  the model snapshot as fully trustworthy.
- `AccessGrantRequest.EmployeeId`/`UserId` nullability: confirmed intentional — required by the
  "defer everything" design (the row is created before the employee/user exist).
- `access_grant_requests.onboarding_draft_id`: has both an FK (`Restrict`) and is part of the
  partial-unique index. Confirmed.
- `user_roles.source_position_id` / `source_position_access_template_id`: configured (nullable,
  indexed on the template id) and present in the snapshot. Note: neither column has a DB-level FK
  constraint (source_position_id has no FK at all; source_position_access_template_id has an index
  but no FK either) — consistent with their being soft historical-tracking fields, not live
  relationships, but flagging since this wasn't explicitly re-confirmed as intentional this pass.
- No brittle "latest migration by name/count" architecture assertion exists anywhere in
  `ONEVO.Tests.Architecture` related to this work (the 4 files matching a "latest migration" style
  search are pre-existing and unrelated: `LegalAcceptanceMigrationIntegrityTests`,
  `NormalizedEmailArchitectureTests`, `AuthRepositoryArchitectureTests`, `BaseLoginArchitectureTests`).

### Is the backend safe for frontend wiring?

**Yes, for the `finalize` endpoint's happy paths and its documented 404/409/422 error paths.** It
compiles (`Domain` through `Tests.Integration`, all 6 projects), its 22 handler tests + 4 controller
tests pass independently against current (non-stale) binaries, its DI registrations are all present
(including the outbox handler that actually sends the invite email), and the response contract
described below is unchanged from the original design. The 3 pre-existing architecture-test
failures are in a *different* endpoint (`CreatePosition`) and don't affect `finalize`.

**One correction the frontend needs before wiring error handling:** the endpoint can also return a
**bare 500** (not one of the documented 404/409/422s) on a raw `DbUpdateException` that isn't a
concurrency conflict — most concretely reachable via the known work-email length gap (255–320 char
emails pass draft save, then violate the `varchar(255)`/`varchar(254)` columns at finalize). The
frontend's error handling for this endpoint should not assume 500 means "crash, retry blindly" —
build in a generic fallback message for it.

Not yet fully safe / still open (as of this section's original pass — **superseded, see the
"Backend verification blockers cleanup pass" section below**, which fixed the `CreatePosition`
architecture-test failures and root-caused the `work_modes.is_active` diff to a `dotnet-ef` tool
version mismatch, not a real model inconsistency):
- ~~The `work_modes.is_active` model-diff anomaly found above is unresolved~~ — resolved below;
  was tooling noise, not a real diff.
- ~~The 3 pre-existing `CreatePosition` architecture-test failures remain unresolved~~ — fixed
  below; they were caused by this branch's own uncommitted `CreatePosition` changes, not
  pre-existing/unrelated as first assumed.
- Integration tests (`ONEVO.Tests.Integration`) build clean but were not *run* this pass (no live
  Postgres/Testcontainers environment available; not required by this task's instructions). Still
  true in the follow-up pass below (Docker daemon unavailable there too).

### Remaining product/backend gaps (unchanged from before, still open)

1. **Approve & send invite endpoint does not exist.** The pending `AccessGrantRequest` this
   endpoint creates has nothing downstream that consumes it yet.
2. **Employee-number auto-generation still missing** — `finalize` still requires it to be entered
   manually; no auto-generation policy exists in this codebase.
3. **WorkEmail length mismatch still unresolved** — `SaveOnboardingDraftCommandValidator` allows up
   to 320 chars but `Employee.Email`/`User.Email` are `varchar(255)` and
   `InvitationToken.InvitedEmail` is `varchar(254)`; an email in the 255–320 range still passes
   draft save and fails at finalize with a **bare 500** (see the verification-pass correction
   above — this is worse than the "generic 409" this bullet originally claimed), not a clean
   validation message.

## Backend verification blockers cleanup pass (2026-08-10, third session)

Scope: the three architecture-test failures, the EF pending-model-changes findings, and the
finalize error-mapping gap the previous session flagged but did not fix. Everything below was run
for real against rebuilt binaries; no step is a repeat of the "manual review only" caveat from
earlier sessions.

### 1. Architecture test failures — fixed, not dismissed

`dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-build` reproduced
the same 3 failures the previous session recorded: `PositionPart2BArchitectureTests.Commands_DoNotContainForbiddenRoleOrOccupantFields`,
`PositionPart2BArchitectureTests.RequestContracts_DoNotContainForbiddenOwnershipOrRoleFields`, and
`PositionsControllerArchitectureTests.RequestContracts_DoNotExposeRoleOrPermissionCreationFields`,
all asserting `CreatePositionRequest`/`CreatePositionCommand` must not expose `RoleId`/`RequiresApproval`.

**Proof this was caused by the current branch, not pre-existing:** `git log` shows both
architecture test files were last committed at `c8ea0d7` (untouched by this session's working-tree
changes), while `git diff` on `CreatePositionRequest.cs`/`CreatePositionCommand.cs` showed `RoleId`/
`RequiresApproval` were added to both types only in this branch's uncommitted diff. The task's own
instruction ("if caused by the current branch's CreatePosition role/access changes, fix them now")
applied directly — this was not dismissed as unrelated.

**Root cause:** `CreatePositionCommandHandler` had been changed (by an already-uncommitted, separate
"position access template" change on this branch, not this finalization work) to accept `RoleId`/
`RequiresApproval` and write a `PositionAccessTemplate` row directly during position creation. This
duplicates `SetPositionAccessCommand`/`SetPositionAccessCommandHandler` — an existing, complete,
separate command that already upserts `PositionAccessTemplate` rows via
`IPositionRepository.AddAccessTemplateAsync`/`UpdateAccessTemplate`. `FinalizeOnboardingDraftCommandHandler`
(this feature's own consumer) only ever reads templates via `GetAccessTemplateByPositionAsync` —
it has no dependency on `CreatePosition` writing them. The architecture tests are a deliberate
guardrail keeping role/access-template creation out of position creation; the duplicated logic
violated it for no functional reason.

**Fix:** removed `RoleId`/`RequiresApproval` from `CreatePositionRequest.cs` and
`CreatePositionCommand.cs`; removed the role-validation checks, the `IRoleRepository` dependency,
and the inline `AddAccessTemplateAsync` call from `CreatePositionCommandHandler.cs` (plus a leftover
now-unused `using ONEVO.Domain.Features.OrgStructure.Entities;` that removal exposed); updated
`PositionsController.Create` to stop passing those fields; reverted the now-unneeded
`IRoleRepository` mock the previous session had added to `CreatePositionCommandHandlerTests.cs`
purely to get it compiling. `git diff` on all five touched files now shows **zero residual diff
against HEAD** — this was a complete, clean revert of the addition, not a partial edit.

**Before committing to this direction, two things were checked that could have made it wrong:**

1. **Is `PositionAccessTemplate` creation still reachable after removing it from `CreatePosition`?**
   Yes — confirmed a live, already-wired endpoint: `PositionsController.cs:189-208`,
   `[HttpPut("{positionId:guid}/access")]` under `[RequirePermission("org:manage")]`, dispatching
   `SetPositionAccessCommand` (which upserts via `IPositionRepository.AddAccessTemplateAsync`/
   `UpdateAccessTemplate` — the same repository methods `CreatePositionCommandHandler` had been
   calling inline). Nothing was made unreachable; the two-step "create position, then `PUT .../access`"
   flow was already fully functional before this fix and remains so.

2. **Was adding `RoleId`/`RequiresApproval` to `CreatePosition` actually a deliberate product
   decision, not accidental duplication?** A prior session's own untracked report,
   `EMPLOYEE_ONBOARDING_FINALIZATION_AND_POSITION_ACCESS_REPORT.md`, states exactly that: *"Position
   creation now accepts optional `RoleId` and `RequiresApproval`... The create transaction adds a
   `PositionAccessTemplate` for the newly created position. ... The existing Set Position Access
   endpoint remains unchanged."* This is real countervailing evidence and was weighed seriously
   before proceeding with the removal. Two things tip the balance toward the removal being correct
   despite that report: (a) that report's own "Verification" section only ran
   `dotnet build src\ONEVO.Api\ONEVO.Api.csproj` — it never ran `ONEVO.Tests.Architecture`, so the
   violation of the pre-existing (committed at `c8ea0d7`, predating that session's uncommitted work)
   `RequestContracts_DoNotExposeRoleOrPermissionCreationFields`/
   `Commands_DoNotContainForbiddenRoleOrOccupantFields` guardrails was never actually checked against;
   (b) this task's own instructions are explicit that an architecture-test failure caused by this
   branch's `CreatePosition` changes should be fixed, not dismissed, and provide no carve-out for "a
   prior uncommitted session intended this." On balance the guardrail is treated as authoritative
   over an uncommitted, unverified prior-session decision that never confirmed it against that
   guardrail. **Flagging for the user rather than deciding unilaterally as final:** if create-time
   role selection in one call is actually wanted product behavior, the correct fix is the reverse of
   this one — update the architecture tests to allow it, not remove the fields — and that should be
   a deliberate call, not something either session made silently.

**Related risk, either way this is ultimately resolved:** `[FromBody]` model binding on
`CreatePositionRequest` silently ignores unknown JSON properties rather than 400ing. If the frontend
was already sending `roleId`/`requiresApproval` on the create call (untestable from this backend-only
task — frontend is out of scope here), removing the fields makes that data silently dropped, not a
visible error. Worth confirming with whoever owns the frontend create-position flow before this is
considered fully closed.

**Result:** `ONEVO.Tests.Architecture` → **548/548 passed, 0 failed** (previously 545/548).

### 2. EF pending model changes — root cause found, no snapshot edits needed

Re-ran the previous session's dummy-connection-string probe:
`dotnet ef migrations has-pending-model-changes --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj --no-build`
with `ConnectionStrings__MigrationConnection` set to a syntactically valid dummy value (EF only
parses the connection string at DbContext construction; it never connects).

The previous session's own report already suspected the diff (`work_modes.is_active` default
mismatch, `subscription_plans`/`tenant_subscriptions` seat-column and `approval_statuses.is_active`
noise, FK/index rename churn) was **tooling noise from a `dotnet-ef` CLI version older than the
project's EF Core runtime**, but left it "plausible, not confirmed." This session confirmed it:
`dotnet ef --version` reported **10.0.7**, while `ONEVO.Infrastructure.csproj` references
`Microsoft.EntityFrameworkCore.Design` **10.0.9**. Ran `dotnet tool update -g dotnet-ef --version 10.0.9`
to match, then re-ran the check.

**Result: `No changes have been made to the model since the last migration.`** (exit code 0, run
twice for consistency). This directly refutes the earlier "pending model changes exist" finding —
it was entirely a `dotnet-ef` 10.0.7 vs. EF Core 10.0.9 tool/runtime mismatch, not real drift. No
hand-editing of `ApplicationDbContextModelSnapshot.cs` was needed or performed; the snapshot,
entities, and configurations were already mutually consistent, exactly as the previous session's
manual code reading concluded for each individually — the tool just couldn't confirm it until the
version mismatch was removed.

This also directly resolves the task's specific attention points: `work_modes.is_active`, the
nullable `AccessGrantRequest` employee/user fields + `OnboardingDraftId` correlation, the
`UserRole` source-position/source-template fields, and the new checklist/access-grant tables are
all covered by this now-clean check. The RLS migration
(`20260810160000_AddEmployeeOnboardingPhase1PersistenceContracts.cs`) was inspected directly: its
`TenantTables` array (`access_grant_requests`, `checklist_templates`, `employee_checklist_tasks`)
enables + forces RLS and creates the standard `tenant_isolation` policy on all three new
tenant-scoped tables, matching the established pattern; `TenantIsolationArchitectureTests` (part of
the 548 passing architecture tests) covers this class of guardrail and is green.

**Pending model check is clean.**

### 3. Error mapping — conflict mapping added for a real gap, without adding EF to Application

The previous session's report was accurate: `FinalizeOnboardingDraftCommandHandler.SaveAsync` only
caught `ConcurrencyConflictException` (thrown by `EfOnboardingDraftRepository.SaveChangesAsync` on
`DbUpdateConcurrencyException`, i.e. a stale `xmin`). A *different* failure mode — two concurrent
finalize calls racing on a Postgres **unique-constraint** violation (duplicate work email, employee
number, or a duplicate pending `access_grant_requests` row under the partial unique index) — was
not caught anywhere and fell through to `ExceptionHandlerMiddleware`'s default case as a bare 500.

**Fix, using the codebase's own existing convention** (the same pattern already used by
`EfIdempotencyStore.TryBeginAsync` and `EfPositionRepository.SaveChangesAsync` — catch
`DbUpdateException` where `ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }`
at the repository boundary and throw a plain `Exception`-derived Application-layer type, no EF
reference added to `ONEVO.Application`):
- New `UniqueConstraintConflictException` in `ONEVO.Application.Common.Exceptions` (mirrors
  `ConcurrencyConflictException`'s shape).
- `EfOnboardingDraftRepository.SaveChangesAsync` now has a second `catch` (after the existing
  `DbUpdateConcurrencyException` one, since that type derives from `DbUpdateException` and must be
  ordered first) that maps a Postgres unique-violation to `UniqueConstraintConflictException`.
  Every write across `FinalizeOnboardingDraftCommandHandler`'s branches is staged on the same
  shared `DbContext` and committed by this one repository's `SaveChangesAsync` call, so this single
  catch site covers a race on any of the tables it touches (employee, user, position assignment,
  user role, invitation token, access grant request, checklist tasks).
- `FinalizeOnboardingDraftCommandHandler.SaveAsync` now also catches `UniqueConstraintConflictException`
  and returns a clean `409 Conflict` with a message naming the likely cause, instead of propagating.
- New unit test `Handle_ReturnsConflict_WhenSaveRacesOnUniqueConstraint` asserts the 409 mapping.

**What this does not fix:** the previous session's specific known gap — a work email in the
255–320 char range passing `SaveOnboardingDraftCommandValidator` (max 320) but violating
`Employee.Email varchar(255)`/`InvitationToken.InvitedEmail varchar(254)` at finalize — is a
Postgres **string-data-right-truncation** error (`22001`), not a unique-constraint violation
(`23505`). It is a different `PostgresErrorCodes` value and is **not** covered by this fix; it
still propagates as a bare 500. Documented here rather than silently left implied-fixed. Actually
closing it needs either tightening `SaveOnboardingDraftCommandValidator`'s max length to 255/254,
or a dedicated `22001` catch — out of scope for this pass since it's a pre-existing validator gap,
not a finalize-specific one.

**Also not changed (flagged, not fixed):** `SaveOnboardingDraftCommandHandler` calls the same
`_draftRepository.SaveChangesAsync(ct)` and also only catches `ConcurrencyConflictException`, so a
unique-constraint race during draft save now surfaces as `UniqueConstraintConflictException`
instead of a raw `DbUpdateException`. Confirmed directly by reading `ExceptionHandlerMiddleware.cs`
(not inherited from the previous session's claim): its `exception switch` only special-cases
`ValidationException`, `NotFoundException`, `DomainException`, `ForbiddenException`, and
`ServiceUnavailableException` — everything else, including both `ConcurrencyConflictException` and
the new `UniqueConstraintConflictException` (both plain `Exception` subclasses), falls into the `_ =>`
default arm → 500 "An unexpected error occurred." So this is confirmed to be the same bare-500
outcome as before this pass, not a regression — just a differently-typed unhandled exception.
Adding the same one-line `catch` to `SaveOnboardingDraftCommandHandler` would be a natural
follow-up but is outside this task's "review finalization failure behavior" scope.

### 4. Re-run verification (this session, against rebuilt binaries)

`ONEVO.sln` does not exist at the repo root (confirmed again, still not a regression — no solution
file exists anywhere in the repo). Built each of the 7 projects individually in dependency order
instead of the requested `dotnet build ONEVO.sln`:

- `dotnet build src\ONEVO.Domain\... / ONEVO.Application\... / ONEVO.Infrastructure\... / ONEVO.Api\... / ONEVO.Tests.Unit\... / ONEVO.Tests.Architecture\... / ONEVO.Tests.Integration\...  --no-restore --verbosity minimal`
  → **all 7 succeed, 0 errors.** Only pre-existing, unrelated warnings (nullable-reference warnings
  in `AdminAuthController`/`TenantRlsInterceptorTests`/`PermissionSeederTests`/
  `GetPositionTreeQueryHandlerTests`, `SQLitePCLRaw` NU1903 advisory, obsolete
  `Testcontainers.PostgreSqlBuilder` constructor warnings).
- `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-build --filter "FullyQualifiedName~Onboarding|FullyQualifiedName~Employee|FullyQualifiedName~Invitation|FullyQualifiedName~Invite|FullyQualifiedName~Outbox|FullyQualifiedName~Checklist|FullyQualifiedName~AccessGrant|FullyQualifiedName~PositionAssignment|FullyQualifiedName~UserRole|FullyQualifiedName~SeatEntitlement|FullyQualifiedName~Position"`
  → **286/286 passed** (up from the previous session's 169; includes the new
  `Handle_ReturnsConflict_WhenSaveRacesOnUniqueConstraint` test and the broader `Position` filter
  match this run added).
- `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-build`
  → **548/548 passed, 0 failed** (previously 545/548 — see §1).
- `git diff --check` → exit code 0; only pre-existing LF→CRLF line-ending warnings on files this
  branch already had modified (Windows checkout artifact), no actual whitespace/conflict errors.
- Integration tests: **not run.** `docker info` failed — the Docker daemon is not running in this
  session's environment (`failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`),
  so Testcontainers-backed `ONEVO.Tests.Integration` cannot execute. The project itself builds
  clean (0 errors) as part of the 7-project build above.
- `dotnet ef migrations has-pending-model-changes` re-run at final state (after the
  `UniqueConstraintConflictException`/`EfOnboardingDraftRepository` edits and the
  `CreatePositionCommandHandler` unused-`using` cleanup — neither touches the EF model, but this
  makes "pending model check is clean" a literal final-state fact rather than one inferred from an
  earlier run) → **still clean, exit code 0.**
- Full `ONEVO.Tests.Unit` suite (not just the filtered subset) re-run after all fixes → **1585/1585
  passed, 0 failed** — confirms the `CreatePosition` field removal caused no regressions anywhere
  else in the suite, not just in the filtered onboarding/position tests.

### Is the backend safe for frontend wiring now?

**Yes — more so than the previous session's report claimed**, with one caveat carried forward
unchanged. The `CreatePosition` architecture-test failures are fixed (not just "unrelated, don't
worry about it" as previously stated) and the EF pending-model-changes uncertainty is resolved
(confirmed clean, root-caused to a tool version mismatch, not real drift). `finalize`'s error
surface is smaller: duplicate-email/employee-number/access-grant races now return a clean 409
instead of a bare 500.

**Still open, unchanged from before:** the work-email length gap (255–320 chars) still returns a
bare 500 (different Postgres error code than what this pass's fix covers) — the frontend must still
build a generic fallback for this endpoint's 500s, not assume only 404/409/422 are possible.
Integration tests remain unrun in this environment (Docker unavailable). The "approve & send
invite" flow still does not exist — the pending `AccessGrantRequest` records this endpoint creates
still have no downstream consumer.

## Critical fix: 5 migrations were invisible to EF tooling (2026-08-10, same session, post-user-report)

While applying this report's own fixes, the user ran `ops\postgres\setup-local-db.ps1 -RunMigrations`
against a real local Postgres database and then `dotnet run`. Startup crashed:
`Npgsql.PostgresException: 42703: column "is_active" of relation "work_modes" does not exist` —
the exact column the "EF pending model changes" section above spent significant effort
concluding was fine. **That section's "pending model check is clean" conclusion was correct on
its own terms but answered the wrong question**, and this section corrects the record.

**Root cause, confirmed directly, not inferred:** `dotnet ef migrations list` (both before and
after the fix below) proved that the 5 migration files this branch added —
`20260810153000_CorrectOnboardingDraftIdentityAndWorkMode`,
`20260810154000_AddTenantSeatPolicyContract`,
`20260810155000_AddEmployeeOnboardingInvitationContract`,
`20260810160000_AddEmployeeOnboardingPhase1PersistenceContracts`,
`20260810161000_AddUserRoleSourcePositionTracking` — were **completely invisible to EF's migration
tooling**. `dotnet ef migrations list` stopped at `20260810072915_AddOnboardingDraftXminConcurrencyToken`
(the last migration with a proper `.Designer.cs`) and never listed any of the 5. Comparing them
against every other migration in the repo showed why: every other migration ships a
`{Name}.Designer.cs` companion carrying `[DbContext(typeof(ApplicationDbContext))]` and
`[Migration("<id>")]` attributes (confirmed by reading
`20260810072915_AddOnboardingDraftXminConcurrencyToken.Designer.cs`). All 5 of the new migrations
were hand-authored `.cs` files only — with **no Designer.cs and no attributes at all** — so EF's
migrations-assembly scanner silently skipped them. `dotnet ef database update` therefore only ever
applied migrations through `...072915`, no matter how many times it was run, and the live database
was permanently missing `work_modes.is_active`, the tenant seat policy contract, the invitation
contract, the Phase 1 persistence contracts (`checklist_templates`, `employee_checklist_tasks`,
`access_grant_requests`, including their RLS policies), and the `user_roles` source-position
columns — i.e. **the entire schema this whole finalization feature depends on**.

**Why the "EF pending model changes" check above didn't catch this:** `has-pending-model-changes`
compares the *runtime model* (from entities/configurations) against
`ApplicationDbContextModelSnapshot.cs` — a file previous sessions had hand-edited to already
reflect the target schema. Both sides agreed with each other, so the check reported clean. It
never touches the individual migration files' discoverability or a live database at all — a
structurally different question from "will `dotnet ef database update` actually apply this
branch's migrations." Both checks were run and both were necessary; neither alone was sufficient.

**Fix:** added the missing `[DbContext(typeof(ApplicationDbContext))]` and `[Migration("<id>")]`
attributes directly onto each of the 5 migration classes, plus the two required `using` statements
(`Microsoft.EntityFrameworkCore.Infrastructure`, `ONEVO.Infrastructure.Persistence`) — no separate
`.Designer.cs` files were added, since `BuildTargetModel` (what Designer.cs otherwise carries) is
only used by design-time diffing/scripting tools, not by `Migrator.Migrate()`/`database update`,
and the `Up()`/`Down()` SQL bodies (already reviewed extensively across prior sessions) were left
untouched. Verified minimally first: `dotnet ef migrations list` now lists all 5 in order.

**Verified against the real database, not just tooling:** re-ran
`ops\postgres\setup-local-db.ps1 -RunMigrations` — all 5 migrations applied cleanly with no SQL
errors (`Applying migration '20260810153000_...'` through `'...161000_...'`, `Done.`). Then started
the API (`dotnet run`, background, log inspected, then stopped cleanly): lookup seeding logged
`Seeded 3 work modes` (previously the exact `INSERT INTO work_modes (..., is_active, ...)` that
crashed), all other seeders ran, dev smoke-test tenants seeded, and the app reached
`Now listening on: http://localhost:5299` / `Application started.` with no unhandled exceptions.
The only warnings were pre-existing, unrelated config gaps (`Jwt:Secret`, `Urls:WebhookBaseUrl` not
set).

**Re-verified nothing else regressed:** all 7 projects rebuilt clean; `ONEVO.Tests.Unit` 1585/1585;
`ONEVO.Tests.Architecture` 548/548; `has-pending-model-changes` still reports clean (attribute-only
changes don't touch the model); `git diff --check` still clean (LF/CRLF warnings only).

**Correction to this report's own earlier framing:** every claim above this section that says
"pending model check is clean" or "safe for frontend wiring" was true only in the narrow sense of
model-vs-snapshot agreement — it was not, and should not have been read as, confirmation that this
branch's schema actually reaches a real database. That gap is now closed. Any future session on
this branch should treat **`dotnet ef migrations list`** (not just `has-pending-model-changes`) as
a required check whenever new migrations are added by hand rather than via `dotnet ef migrations add`.

### Is the backend safe for frontend wiring now? (supersedes both earlier answers in this file)

**Yes, with higher confidence than either prior "yes" in this report**, because this is now backed
by an actual applied-migrations run against a real Postgres database and a clean application
startup, not just static analysis. Remaining caveats are unchanged from the section above: the
work-email length gap still returns a bare 500, integration tests still weren't run in this
environment (though the live-database migration run above substitutes for a meaningful part of
what they'd have caught), the "approve & send invite" flow still doesn't exist, and the
`CreatePosition` role-field question (§1 above) still needs the user's/product's own call.

## Frontend contract required next

- `POST /api/v1/onboarding/drafts/{id}/finalize`, no body, `employees:write` required.
- Success response: `{ draftId, employeeId, status, draftReason, invitationQueued, positionApprovalPending, checklistTasksCreated, messageKey }`. `employeeId` is `null` when `positionApprovalPending` is `true`.
- Frontend needs a distinct UI state for "submitted for position approval, nothing else has happened yet" vs. today's draft-save-time `WaitingForPositionApproval` (same status value, but finalize's version is post-review-and-submit, not pre-invite).
- No raw invitation token is ever returned.
- Error shape is the existing `Problem()`/ProblemDetails convention (404/409/422), same as every
  other onboarding-draft endpoint — **plus a reachable bare 500** (application/problem+json,
  generic "An unexpected error occurred." detail, no ProblemDetails-specific hint) on an
  unmapped `DbUpdateException`, most concretely the known work-email length gap. Build a generic
  fallback for this endpoint's 500s rather than assuming only 404/409/422 are possible.
