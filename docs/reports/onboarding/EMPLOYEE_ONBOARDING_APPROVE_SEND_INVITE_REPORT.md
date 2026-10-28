# Employee onboarding "Approve & Send Invite" / Reject implementation

## Endpoints added

- `POST /api/v1/onboarding/access-grant-requests/{id}/approve-and-send-invite`
- `POST /api/v1/onboarding/access-grant-requests/{id}/reject` (body: `{ "decisionNote": string | null }`, body itself is optional)

New controller `AccessGrantRequestsController` (`src/ONEVO.Api/Controllers/Tenant/CoreHr/AccessGrantRequestsController.cs`),
route `api/v1/onboarding/access-grant-requests`, matching the task's preferred routes exactly (they
were consistent with the sibling `api/v1/onboarding/drafts` controller's style, so no deviation was
needed here unlike the `finalize` endpoint's own report). `TenantId` is always server-derived from
`ICurrentUser`; neither action accepts it in the route or body.

**Permission:** both actions are gated by `[RequirePermission("employees:write")]` only, under the
class-level `[Authorize(Policy = "TenantPolicy")]`. `PermissionSeeder.cs` has no permission finer
than `employees:write` for position-access approval — the userflow doc's `position:approve` /
`org:manage` references are not backed by a seeded permission for this purpose (`org:manage` exists
but is scoped to "create and edit org structure, departments", not approvals). This is a
documented limitation, not an oversight: a dedicated `position:approve`-style permission should be
introduced and both actions moved onto it in a follow-up.

## Files changed

**Application**
- `IOnboardingPersistenceRepositories.cs` — added `IAccessGrantRequestRepository.GetTrackedByIdAsync(tenantId, id, ct)`.
- `Onboarding/Commands/ApproveAccessGrantRequest/ApproveAccessGrantRequestCommand.cs`,
  `ApproveAccessGrantRequestCommandHandler.cs` (new).
- `Onboarding/Commands/RejectAccessGrantRequest/RejectAccessGrantRequestCommand.cs`,
  `RejectAccessGrantRequestCommandHandler.cs` (new).
- `Onboarding/DTOs/Responses/ApproveAccessGrantRequestResponse.cs`,
  `RejectAccessGrantRequestResponse.cs` (new).

**Infrastructure**
- `EfOnboardingPersistenceRepositories.cs` — `EfAccessGrantRequestRepository.GetTrackedByIdAsync`.

**Api**
- `Controllers/Tenant/CoreHr/AccessGrantRequestsController.cs` (new).
- `Contracts/CoreHr/AccessGrantRequests/RejectAccessGrantRequestRequest.cs` (new).

**Tests**
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ApproveAccessGrantRequestCommandHandlerTests.cs` (new, 18 tests).
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/RejectAccessGrantRequestCommandHandlerTests.cs` (new, 8 tests).
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/AccessGrantRequestsControllerTests.cs` (new, 5 tests).
- `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/OnboardingPersistenceRepositoryTests.cs` — 1 new
  repository test for `GetTrackedByIdAsync`.

**No migration was needed.** `AccessGrantRequest` (added by an earlier session, per
`EMPLOYEE_ONBOARDING_PHASE1_PERSISTENCE_CONTRACTS_REPORT.md` /
`EMPLOYEE_ONBOARDING_FINALIZATION_IMPLEMENTATION_REPORT.md`) already carries nullable `EmployeeId`/
`UserId`, `DecidedByUserId`, `DecidedAt`, and `DecisionNote` — every column both actions need to
write already exists.

## Approve behavior

`ApproveAccessGrantRequestCommandHandler` re-runs, field-for-field, the same validations
`FinalizeOnboardingDraftCommandHandler` runs, because nothing from the deferred "requires approval"
branch was ever persisted:

1. Load the request by id (tenant-scoped, tracked). 404 if missing.
2. `ApprovalStatus != "Pending"` → 409 (covers already-approved, already-rejected, and repeated
   approval in one check).
3. `OnboardingDraftId is null` → 422 (defensive; the finalize handler that creates these requests
   always sets it, so this is effectively unreachable today, but the field is nullable in the
   schema and the code does not assume it).
4. Load the draft (tenant-scoped, tracked). 404 if missing; 409 if `Status != WaitingForPositionApproval`.
5. Field validation: first/last name, work email, legal entity active, department active/scoped
   (if set), **draft's position still equals `grantRequest.TargetPositionId`** (409 if it has
   moved — the "position mismatch" case), position active/scoped, work mode active, employment
   type resolves, work email not already used (409), employee number present and not already used
   (409).
6. Access template revalidation: template must still exist and be `IsActive` (422 if not); its
   `Id` must still equal `grantRequest.PositionAccessTemplateId` (409 if the template itself
   changed since the request was submitted); its `RoleId` must still equal
   `grantRequest.RequestedRoleId` (409 if the role changed). **Deliberately not re-checked:**
   `template.RequiresApproval` being flipped to `false` after the request was submitted does not
   block approval — an approver explicitly reviewing and approving is strictly safer than silently
   skipping the decision, so the pending request is still honored on its own terms.
7. Position capacity (`PositionAssignment` active count vs. `MaxOccupancy`) — same signal
   `finalize` uses.
8. Seat re-check via `ISeatEntitlementService.EvaluateAsync`. `Blocked` → 409, request stays
   `Pending`, nothing written (**no draft mutation at all in this branch** — approving again once
   seats free up must still work, so the draft is never moved to `WaitingForSeat`, which would
   have erased the pending-approval state). `Undetermined` → 422, same "create nothing, save
   nothing" behavior. `Approved` → proceeds.
9. Checklist: same `InstantiateAsync` staging pattern as finalize; malformed task JSON → 422 before
   anything else is created.
10. User: reused by tenant+email if one exists, otherwise created exactly like finalize's pending
    user (`IsActive=false`, `EmailVerified=false`, `MustChangePassword=true`,
    `PasswordHash=string.Empty`).
11. Employee, PositionAssignment: created exactly like finalize's non-approval branch.
12. UserRole: `RoleId = grantRequest.RequestedRoleId` (the request's own field, not re-derived from
    the template, so the "uses requested role" test is actually asserting something real),
    `SourcePositionId`/`SourcePositionAccessTemplateId` set. **Guard added that finalize's
    non-approval branch doesn't need:** `user_roles`' primary key is `(UserId, RoleId)` — not
    tenant- or position-scoped (`UserRoleConfiguration.cs:12`). When the matched user is a
    *pre-existing* tenant user who already holds the requested role, inserting again would be a
    PK violation surfacing as a misleading "duplicate email/employee number" 409. The handler
    calls `IUserRoleRepository.ListActiveByUserIdAsync` first and skips the insert if the role is
    already held. **Discrepancy flagged, not silently trusted:** `database/phase1-table-inventory.md`
    documents `user_roles`' unique key as `(tenant_id, user_id, role_id, source_position_id,
    effective_from)`, but the live EF configuration disagrees and is authoritative — the doc is
    stale on this point.
13. Invitation token + outbox enqueue: identical shape to finalize's non-approval branch
    (`Purpose = employee_onboarding`, 72-hour expiry via `IDateTimeProvider`,
    `OutboxMessageTypes.EmployeeOnboardingInviteEmail`, raw token never persisted or returned).
14. On success: `grantRequest.ApprovalStatus = "Approved"`, `DecidedByUserId`, `DecidedAt`,
    `EmployeeId`, `UserId` all set; `draft.Status = Finalized`, `DraftReason = InvitationSent`,
    `FinalizedAt` set.
15. Everything above is staged on the shared `ApplicationDbContext` and committed by exactly one
    `IOnboardingDraftRepository.SaveChangesAsync(ct)` call (not the access-grant-request
    repository's own `SaveChangesAsync` — see Concurrency below for why that choice matters).

## Reject behavior

`RejectAccessGrantRequestCommandHandler` is deliberately much smaller:

1. Load the request. 404 if missing.
2. `ApprovalStatus != "Pending"` → 409 (covers "approved request cannot be rejected" and
   "already-rejected cannot be rejected again").
3. `DecisionNote` longer than 500 chars → 422 (`AccessGrantRequestConfiguration.cs` caps
   `DecisionNote` at `varchar(500)`).
4. `OnboardingDraftId is null` → 422 (same defensive posture as approve).
5. Load the draft. 404 if missing.
6. `grantRequest.ApprovalStatus = "Rejected"`, `DecidedByUserId`, `DecidedAt`,
   `DecisionNote = note?.Trim()` (empty/whitespace normalized to `null`).
7. **Draft status/reason are left completely untouched** — `Status` stays
   `waiting_for_position_approval`, `DraftReason` stays `waiting_for_position_approval`. No
   rejected-draft state exists in the current status model
   (`OnboardingDraftStatus`/`OnboardingDraftReason` only have `Draft`, `WaitingForSeat`,
   `WaitingForPositionApproval`, `Cancelled`, `Finalized`), and the task's own fallback instruction
   for this exact situation is to leave the draft waiting and let the rejected request itself be
   the signal. **This recovery path was checked against the actual code, not assumed — see
   "Remaining backend blockers" #5 below: re-requesting approval for the *same* position after a
   rejection does not currently work.** Changing to a *different* (non-approval, or differently
   accessed) position and saving does work, because `SaveOnboardingDraftCommandHandler`
   recomputes `requiresApproval` from whatever position is on the request at save time. "Cancel"
   is not reachable at all today — `OnboardingDraftsController` has no cancel action.
8. `draft.UpdatedAt` is bumped (see Concurrency below) and the change is committed via
   `IOnboardingDraftRepository.SaveChangesAsync(ct)`. This has a visible side effect beyond
   concurrency: `IOnboardingDraftRepository.ListAsync` orders by `UpdatedAt DESC`, so a rejected
   draft moves to the top of the drafts list. That is a reasonable, arguably desirable
   side effect (surfaces the thing HR needs to act on), but it is a deliberate consequence of the
   concurrency fix, not an accident — noting it so a future session doesn't "simplify" reject by
   dropping the `UpdatedAt` bump and silently reopening the approve/reject race described below.
9. No user, employee, invitation, checklist task, or role is created or touched. The draft is
   never deleted.

## Seat behavior (approve only; reject never checks seats)

Identical three-way handling to `finalize`: `Approved` → proceeds; `Blocked` → 409, nothing
created, nothing saved, request stays `Pending`; `Undetermined` → 422, nothing created, nothing
saved. Unlike `finalize`'s own `Blocked` handling (which moves the draft to `WaitingForSeat`),
approve's `Blocked` path does **not** touch the draft — doing so would silently erase the
`WaitingForPositionApproval` state and lose why the record was waiting in the first place. Both
`Blocked` and `Undetermined` leave the request approvable again later once billing/seats are
resolved.

## Transaction / concurrency behavior

Every write in the approve path (user, employee, position assignment, user role, invitation
token, access-grant-request fields, draft fields) is staged on the one shared, scoped
`ApplicationDbContext` and committed by a single `SaveChangesAsync` call — identical to
`FinalizeOnboardingDraftCommandHandler`'s own pattern. `ConcurrencyConflictException` (stale
draft `xmin`) and `UniqueConstraintConflictException` (Postgres unique-violation, e.g. racing on
work email/employee number) are both caught and mapped to a clean 409, reusing
`EfOnboardingDraftRepository.SaveChangesAsync`'s existing exception translation — no changes were
needed to Infrastructure's exception handling for this to work.

**Approve/reject race, and why reject also touches the draft:** `AccessGrantRequest` itself has no
optimistic-concurrency token (no `xmin`, unlike `OnboardingDraft`). If reject only wrote to
`access_grant_requests` and saved via the access-grant-request repository's own
`SaveChangesAsync`, a reject that commits followed by an in-flight approve of the *same* request
could still complete — the approve handler's in-memory `ApprovalStatus != "Pending"` check runs
before either transaction commits, so it can't see a concurrent write. To close this,
`RejectAccessGrantRequestCommandHandler` also sets `draft.UpdatedAt` and saves through
`IOnboardingDraftRepository.SaveChangesAsync` — the same repository, same `xmin`-checked entity,
that approve always mutates (`draft.Status = Finalized`). Approve and reject therefore always
contend on the *draft's* row when they race, and the loser gets a clean
`ConcurrencyConflictException` → 409 instead of silently clobbering the other's decision. This
also means **no Infrastructure changes were needed for reject's exception mapping** —
`EfAccessGrantRequestRepository.SaveChangesAsync` was left exactly as it was (no unique-violation
catch), since reject never calls it.

Repeated approval/rejection is idempotency-guarded purely by the `ApprovalStatus != "Pending"`
check — no duplicate user/employee/token/outbox/checklist/role can be produced by re-calling
either endpoint once a request is decided.

**On "transaction rollback leaves no partial records" specifically (a required test-list item):**
there is no dedicated rollback test, deliberately — a real partial-write rollback is not something
mocked repositories can exercise (every `Add*Async` call in these tests is a no-op stub; there is
no in-memory transaction to roll back). What *is* tested, and is what actually makes rollback safe
in production, is the single-`SaveChangesAsync`-commits-everything design itself (the same design
`FinalizeOnboardingDraftCommandHandler` already uses and already documented this way) plus the two
conflict-mapping tests (`Handle_ReturnsConflict_WhenSaveRacesOnUniqueConstraint` /
`...OnConcurrency`) confirming that when that single commit fails, the handler returns a clean 409
rather than a partially-applied success. Real partial-write coverage would need an integration
test against a live Postgres instance; that is listed under "Skipped checks" below, not silently
omitted.

## User/employee/role/checklist/invite behavior

Same as `finalize`'s non-approval branch, reusing every existing helper/repository/entity shape
with no new abstractions: pending user (`IsActive=false`), employee (`EmploymentStatusId=1`,
pending-ness carried by `User.IsActive`/`InvitationToken.Status`/draft status, same as
`finalize`), `PositionAssignment` (`PrimaryEmployment`, `Active`), `UserRole` sourced from the
access-grant-request's own `RequestedRoleId` (never a hardcoded Owner/Admin role — confirmed by a
constructor-reflection guard test mirroring `finalize`'s own), `InvitationToken`
(`Purpose = employee_onboarding`), and outbox enqueue via
`OutboxMessageTypes.EmployeeOnboardingInviteEmail` (not the tenant-owner invitation path — no
`TenantOwner*` dependency exists in either handler's constructor, verified by a reflection test).

## Response DTOs

```
ApproveAccessGrantRequestResponse(
    AccessGrantRequestId, OnboardingDraftId, EmployeeId, FinalizationStatus,
    InvitationQueued, ChecklistTaskCount, PositionApprovalStatus, MessageKey)

RejectAccessGrantRequestResponse(
    AccessGrantRequestId, OnboardingDraftId, RequestStatus, DraftStatus, DraftReason, MessageKey)
```

No raw invitation token is ever returned, matching every existing invite-issuing endpoint in this
codebase.

## Tests added/updated

**Approve (18 tests):** missing request → 404; already-approved / already-rejected → 409; request
with no `OnboardingDraftId` → 422; missing draft → 404; draft not
`WaitingForPositionApproval` → 409; draft's position no longer matches the request → 409; access
template changed since the request was submitted → 409; role changed since the request was
submitted → 409; duplicate email → 409 (nothing created); duplicate employee number → 409
(nothing created); seat `Blocked` → 409, nothing created, draft never saved; seat `Undetermined` →
422, nothing created, draft never saved; seat `Approved` → creates user + employee + position
assignment + role (source fields asserted) + invitation token + outbox message (payload type
asserted, not `Times.Any`), response fields asserted; existing user who already holds the
requested role → user role insert skipped (no PK-violation risk), no duplicate user created;
save races on a unique-constraint conflict → 409; save races on a concurrency conflict → 409;
checklist template present → task count reported in the response; constructor takes no
`TenantOwner*` dependency.

**Reject (8 tests):** missing request → 404; already-approved → 409 (nothing saved); already-
rejected → 409; decision note over 500 chars → 422 (nothing saved); request with no
`OnboardingDraftId` → 422; missing draft → 404; pending request → marked rejected with
decider/timestamp/note, draft status/reason provably unchanged, response fields asserted, save
called exactly once; save races on concurrency → 409.

**Controller (5 tests):** approve sends the command with the route id and returns the mediator's
success value as `200 OK`; approve maps a failure `Result` to the correct `ObjectResult` status
code (409 exercised); reject sends the command with the route id **and** the body's
`DecisionNote`; reject tolerates a `null` body (`DecisionNote` sent as `null`, not a 400); reject
maps a failure `Result` to the correct status code (404 exercised).

**Repository (1 test):** `GetTrackedByIdAsync` is tenant-scoped (wrong tenant or wrong id both
return `null`).

## Verification

All commands run from `C:\onevoNew\HRMS-Backend-v1`. `ONEVO.sln` does not exist anywhere in the
repo (confirmed again, consistent with every prior report on this branch) — built each of the 7
projects individually in dependency order.

- `dotnet build src\ONEVO.Domain\ONEVO.Domain.csproj --no-restore --verbosity minimal` → 0 errors.
- `dotnet build src\ONEVO.Application\ONEVO.Application.csproj --no-restore --verbosity minimal` → 0 errors.
- `dotnet build src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --no-restore --verbosity minimal` → 0 errors.
- `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` → **0 compile
  errors**, but the copy step initially failed (`MSB3027`) because a stray `ONEVO.Api.exe`
  (PID 34156, a leftover dev-server process from an earlier session) held the output DLLs locked —
  the exact same class of issue a prior session on this branch recorded. First verified the
  compiler itself was clean by building into an isolated output path
  (`-p:BaseOutputPath=bin_verify\`); then asked the user for explicit confirmation before stopping
  the stray process (confirmed), after which the normal-output build succeeded cleanly. The
  isolated-output folders were deleted afterward so no stray build artifacts were left behind.
- `dotnet build tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --verbosity minimal` →
  after the process was stopped, this surfaced **2 real compile errors in this session's own new
  test file** (not pre-existing): (1) `SetupHappyPath(out Guid requestId, out Guid draftId, ...)`
  captured the `out` parameters inside a `Mock.Setup` lambda, which C# forbids (`CS1628`) — fixed
  by assigning to local variables first and copying to the `out` parameters at the end of the
  method; (2) the same `Employee`/`PositionAssignment`-vs-namespace collision documented in the
  `finalize` implementation report (`ONEVO.Application.Features.CoreHr.PositionAssignment.*` is a
  real sibling namespace under `CoreHr`, shadowing the `PositionAssignment` domain type) — fixed
  with the identical `PositionAssignmentEntity` alias pattern already used elsewhere in this
  codebase. Rebuilt clean afterward: 0 errors, only pre-existing warnings unrelated to this work.
- `dotnet build tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal` → 0 errors.
- `dotnet build tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --verbosity minimal` → 0 errors (only pre-existing `Testcontainers.PostgreSqlBuilder` obsolete-constructor warnings, unrelated to this work).
- `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-build --filter "FullyQualifiedName~Onboarding|FullyQualifiedName~AccessGrant|FullyQualifiedName~Employee|FullyQualifiedName~Invitation|FullyQualifiedName~Invite|FullyQualifiedName~Outbox|FullyQualifiedName~Checklist|FullyQualifiedName~PositionAssignment|FullyQualifiedName~UserRole|FullyQualifiedName~SeatEntitlement" --verbosity minimal` →
  **203/203 passed.** Confirmed non-stale by a second, narrower run filtered to exactly this
  session's three new test classes
  (`FullyQualifiedName~ApproveAccessGrantRequestCommandHandlerTests|FullyQualifiedName~RejectAccessGrantRequestCommandHandlerTests|FullyQualifiedName~AccessGrantRequestsControllerTests`)
  → **32/32 passed**, all real (a stale-DLL run would match 0, as a prior session on this branch
  once caught for a different endpoint's tests).
- `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-build --verbosity minimal` (full
  suite, no filter) → **1618/1618 passed** (up from the prior `finalize` report's 1585 — the 33
  added here: 32 new handler/controller tests + 1 new repository test — with zero regressions
  anywhere else in the suite).
- `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-build --verbosity minimal` → **548/548 passed, 0 failed** — no new architecture-guardrail violations from this work.
- `git diff --check` → exit code 0; only pre-existing LF→CRLF line-ending warnings on files this
  branch already had modified before this session (Windows checkout artifact), no actual
  whitespace/conflict errors, and none on any file this session touched.
- Docker: `docker info` exits 1 in this environment (daemon unavailable) — same as every prior
  session's finding on this branch. Integration tests were **not run** (project builds clean, per
  above); this is recorded as skipped, not silently omitted.

## Skipped checks

- Integration tests (`ONEVO.Tests.Integration`) — build-verified only, not executed (Docker
  daemon unavailable in this environment).
- `dotnet ef migrations list` / `has-pending-model-changes` — not run. No migration was added or
  needed for this task (see "No migration was needed" above), so there is nothing for either check
  to catch; re-running them would only repeat the prior `finalize` report's already-resolved
  findings.

## Remaining backend blockers

1. **No dedicated approval permission.** Both new actions use `employees:write`; a
   `position:approve`-style permission (referenced by the userflow doc but never seeded) should be
   added and both actions moved onto it once product confirms the exact permission model.
2. **`user_roles` unique-key documentation is stale.** `database/phase1-table-inventory.md`
   documents `(tenant_id, user_id, role_id, source_position_id, effective_from)` as the unique key;
   the live `UserRoleConfiguration.cs` defines the primary key as `(UserId, RoleId)` only, with no
   tenant/position/date component. This report's approve handler works around the live schema
   (skip-if-already-held), not the documented one — the doc should be corrected or the schema
   should be revisited, but that decision is out of scope here.
3. **`GetTrackedByIdAsync` has no equivalent "list requests for a draft/tenant" query yet.** The
   frontend will need a way to show a rejected (or pending) request against a draft — today the
   only lookup is by the request's own id, or the finalize-time `GetPendingByDraftAsync`. A future
   session should add a query surface for this (e.g. `GET
   /api/v1/onboarding/drafts/{id}/access-grant-requests` or similar) if the frontend needs it,
   since neither endpoint added here returns a way to discover a request's id from the draft
   alone.
4. **The pre-existing gaps `finalize`'s own report already flagged are unchanged and still apply
   here** since approve shares the same validation shape: the work-email length gap
   (255–320 chars passes draft-save validation but violates `varchar(255)`/`varchar(254)` columns)
   is a Postgres `22001` error, not a unique-constraint violation, so it is **not** covered by the
   existing `UniqueConstraintConflictException` mapping and still surfaces as a bare 500 from
   approve too. Out of scope to fix here (pre-existing validator gap, not introduced by this task).
5. ~~**Hard blocker: rejecting a request and then re-requesting approval for the *same* position
   does not currently work.**~~ **Resolved — see "Rejected approval retry behavior (2026-08-10,
   fourth session)" below.** The description that used to live here (unconditional 409 in
   `FinalizeOnboardingDraftCommandHandler.Handle` on `Status == WaitingForPositionApproval`,
   regardless of whether the correlated request was since rejected) was accurate at the time and
   is preserved in that new section as the documented root cause, not restated here.

## Rejected approval retry behavior (2026-08-10, fourth session)

Scope: HR could reject a pending position-access approval, but the onboarding draft had no way
back — retrying was a dead end. Fixed without deleting any audit history, without changing the
approve/reject concurrency protection, and without adding a separate cancel/reopen endpoint (the
smallest change consistent with the existing status/reason model and the userflow doc's "Starter
can edit draft, cancel draft, or request again" rule).

### Root cause (as found, before this session's fix)

Two independent problems combined into a permanent dead end:

1. `RejectAccessGrantRequestCommandHandler` marked the `AccessGrantRequest` `Rejected` but left
   `draft.Status`/`DraftReason` at `waiting_for_position_approval` untouched — there was no
   "rejected" draft state, so the draft stayed visibly (and functionally) "waiting" forever.
2. `FinalizeOnboardingDraftCommandHandler.Handle` returned an unconditional 409 whenever
   `draft.Status == WaitingForPositionApproval`, before ever checking whether the correlated
   request was still actually pending or had already been rejected. Even if problem 1 were fixed
   by simply resetting the draft to `Draft` on rejection, that reset alone would not have been
   durable: `SaveOnboardingDraftCommandHandler` (behind `PUT /drafts/{id}`) recomputes
   `requiresApproval` from the draft's current position on every save and re-stamps
   `WaitingForPositionApproval` whenever a still-approval-requiring position is unchanged — so a
   normal "HR reopens the draft via Continue Draft and saves" step between reject and re-finalize
   would have silently put the guard's unconditional 409 back in the way. A fix confined to the
   reject handler alone would not have closed the loop; both handlers needed to change together.

### Fix

1. New `OnboardingDraftReason.PositionApprovalRejected = "position_approval_rejected"` constant
   ([OnboardingDraft.cs](src/ONEVO.Domain/Features/CoreHr/OnboardingDraft/Entities/OnboardingDraft.cs)).
   `DraftReason` is a plain `varchar(50)` with no `CHECK` constraint or enum parsing anywhere in
   the codebase (confirmed by reading the column's migration DDL and every `OnboardingDraftReason.*`
   reference), so this needed no migration and is not a breaking DTO change — `DraftReason` was
   already a free-form nullable string on every response DTO that carries it.
2. `RejectAccessGrantRequestCommandHandler`: when the draft being rejected against is currently
   `WaitingForPositionApproval` (the normal case), it now moves the draft to `Status = Draft`,
   `DraftReason = PositionApprovalRejected` instead of leaving it untouched. **Defensive guard:**
   if the draft is *not* currently `WaitingForPositionApproval` (e.g. a stale `Pending` request
   rejected after the draft was already `Finalized` via a different request), the reset is
   skipped — only `UpdatedAt` changes — so a rejection can never silently undo an unrelated
   decision already made on the draft. The request itself is still marked `Rejected` either way;
   nothing about the request-side behavior described earlier in this report changed.
3. New `IAccessGrantRequestRepository.AnyPendingByDraftAsync(tenantId, onboardingDraftId, ct)`
   (`EfAccessGrantRequestRepository` implementation: `ApprovalStatus == "Pending"`, no position/
   template filter — deliberately draft-wide, see "Duplicate prevention" below).
4. `FinalizeOnboardingDraftCommandHandler.Handle`: the `WaitingForPositionApproval` guard no
   longer 409s unconditionally. It now calls `AnyPendingByDraftAsync` first — 409 only if a
   `Pending` request genuinely still exists (a live decision is outstanding); if not (the only
   request on file for this draft is `Rejected`), execution falls through to the normal
   validation pipeline, which independently re-resolves the draft's current position and access
   template from scratch — it never trusted the stale `WaitingForPositionApproval` flag for
   anything except this one guard.

### Retry behavior this produces

- **Draft unchanged, same position still requires approval:** reject → draft becomes
  `draft`/`position_approval_rejected` → HR calls `PUT /drafts/{id}` (optionally with no changes;
  `SaveOnboardingDraftCommandHandler` recomputes and re-stamps `WaitingForPositionApproval`, same
  as before this fix) or calls `finalize` directly against the `Draft`-status record → either way,
  `finalize`'s `AnyPendingByDraftAsync` check finds no `Pending` row (the old one is `Rejected`)
  → validation re-runs → `FinalizeWithPendingApprovalAsync` submits a **new**
  `AccessGrantRequest` (`ApprovalStatus = "Pending"`). The old `Rejected` row is never deleted,
  updated, or reused — `GetPendingByDraftAsync`'s existing `ApprovalStatus == "Pending"` filter
  (unchanged) simply never matches it, so it stays in history as-is.
- **HR changes position or the access template no longer requires approval:** the same fall-through
  reaches the normal `requiresApproval` computation off the *current* position/template, so
  finalize proceeds through the non-approval path (or a fresh approval request against the new
  position/template) per the ordinary rules — nothing approval-specific was hardcoded into this
  path before or after this fix.
- **A decision is still genuinely outstanding** (the existing `Pending` request has not been
  decided): `AnyPendingByDraftAsync` returns `true`, finalize still 409s, exactly as before.

### Duplicate-request prevention (unchanged, verified — no new gap introduced)

`AnyPendingByDraftAsync` is intentionally scoped to "any `Pending` request for this draft", not
"any `Pending` request for this draft *and* this position/template" — this was a deliberate choice
to keep the finalize-time guard from ever admitting two live pending requests for the same draft
against two different positions (e.g. HR submits for Position A, then changes to Position B and
submits again before A is decided). The pre-existing, unmodified layer of protection is unchanged:
the partial unique index on `access_grant_requests(tenant_id, onboarding_draft_id,
target_position_id, position_access_template_id) WHERE approval_status = 'Pending'`
(`AccessGrantRequestConfiguration.cs`) plus `FinalizeWithPendingApprovalAsync`'s own
`GetPendingByDraftAsync` reuse-check together still guarantee only one live `Pending` row can ever
exist per draft/position/template combination — this session added a guard in front of that layer,
it did not touch or loosen it.

### Concurrency (unchanged, re-verified)

The approve/reject race protection this report already documented is untouched: both handlers
still save through `IOnboardingDraftRepository.SaveChangesAsync`, so they still contend on the
draft's own `xmin` token. Rejecting still bumps `draft.UpdatedAt` even in the defensive-guard
branch where `Status`/`DraftReason` are left alone, specifically so that branch keeps
participating in the same concurrency contention rather than silently saving through a path that
skips it. `Approve` was not modified at all in this pass — its `ApprovalStatus != "Pending"` check
(covering both "approve a rejected request" and "approve an already-approved request" with a 409)
and its own draft-status guard are exactly as this report already described them.

### Tests added/updated

- `RejectAccessGrantRequestCommandHandlerTests.cs`: renamed
  `Handle_PendingRequest_MarksRejectedAndLeavesDraftStatusUnchanged` →
  `Handle_PendingRequest_MarksRejectedAndMovesDraftToRetryableState`, asserting
  `draft.Status == Draft`, `draft.DraftReason == PositionApprovalRejected`, and the same on the
  response DTO. Added `Handle_PendingRequest_WhenDraftAlreadyMovedOn_LeavesDraftStatusUnchanged`
  covering the defensive guard (draft already `Finalized` when a stale `Pending` request is
  rejected against it — status/reason must not be clobbered).
- `FinalizeOnboardingDraftCommandHandlerTests.cs`: renamed
  `Handle_ReturnsConflict_WhenDraftIsWaitingForPositionApproval` →
  `Handle_ReturnsConflict_WhenDraftIsWaitingForPositionApprovalAndRequestStillPending` (now
  explicitly arranges `AnyPendingByDraftAsync = true`, since an unconfigured mock would otherwise
  default to `false` and the test would stop proving what its name claims). Added
  `Handle_DraftWaitingForApprovalWithNoPendingRequest_CreatesFreshAccessGrantRequest` (the
  reject → resave → re-finalize sequence: `AnyPendingByDraftAsync = false`, position still
  requires approval → asserts a new `AccessGrantRequest` is added exactly once) and
  `Handle_DraftWaitingForApprovalWithNoPendingRequest_FinalizesImmediately_WhenPositionNoLongerRequiresApproval`
  (same starting state, but the access template no longer requires approval → asserts the
  non-approval path runs and no `AccessGrantRequest` is created).
- `OnboardingPersistenceRepositoryTests.cs`: added
  `AccessGrantRequest_AnyPendingByDraft_OnlyMatchesPendingAndIsTenantScoped` against a real EF
  Core InMemory `ApplicationDbContext` (not a mock) — a `Rejected`-only row reads as "not
  pending" for both the correct tenant and a different tenant; adding a genuinely `Pending` row
  then flips it to `true` for the correct tenant only.
- No test asserts a hardcoded Owner/Admin role assignment anywhere in this retry path — this
  session did not touch role-assignment logic at all (it lives entirely inside
  `FinalizeWithPendingApprovalAsync`'s existing `AccessGrantRequest` construction and
  `FinalizeImmediatelyAsync`'s existing `UserRole` construction, both unmodified), and the
  existing constructor-reflection guard tests for both handlers (`Handle_DoesNotDependOn
  TenantOwnerProvisioningService` and its `Approve` equivalent) are unchanged and still pass.
- No changes were needed to `ApproveAccessGrantRequestCommandHandler` or its tests — approve was
  never part of the dead end (its own `ApprovalStatus != "Pending"` check already returns 409 for
  a rejected request, unaffected by any of the above).

### Verification

All commands run from `C:\onevoNew\HRMS-Backend-v1`. `ONEVO.sln` still does not exist anywhere in
the repo — built each project individually. A leftover `ONEVO.Api.exe` (PID 34688) was again
holding the normal `bin\Debug` output DLLs locked (the same recurring issue prior sessions on this
branch recorded); this time the user did not respond to the confirm-before-stopping prompt, so
rather than stopping the process unilaterally, every build/test in this pass was run against an
isolated `-p:BaseOutputPath=bin_verify\` output tree (deleted afterward — `git status` confirms no
stray `bin_verify*` paths remain) instead of skipping verification.

- `dotnet build` for `ONEVO.Domain`, `ONEVO.Application`, `ONEVO.Infrastructure` (normal output,
  unaffected by the lock) → all **0 errors**.
- `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore -p:BaseOutputPath=bin_verify\` →
  **0 errors**, 1 pre-existing unrelated warning (`AdminAuthController.cs` nullable dereference).
- `dotnet build tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore -p:BaseOutputPath=bin_verify\`
  → **0 errors**, only pre-existing unrelated warnings (`TenantRlsInterceptorTests.cs`,
  `PermissionSeederTests.cs`, `GetPositionTreeQueryHandlerTests.cs`, `SQLitePCLRaw` NU1903
  advisory).
- `dotnet vstest` against the isolated-output `ONEVO.Tests.Unit.dll`, filtered to
  `RejectAccessGrantRequest|ApproveAccessGrantRequest|FinalizeOnboardingDraft|
  OnboardingPersistenceRepositoryTests|AccessGrantRequestsControllerTests` → **63/63 passed**.
  Narrowed further to exactly this session's new/renamed test names (`AnyPendingByDraft|
  RetryableState|AlreadyMovedOn|FreshAccessGrantRequest|NoLongerRequiresApproval|
  RequestStillPending`) with detailed console logging → **6/6 passed, all 6 individually listed by
  name** (guards against the stale-DLL false-green this branch's own reports have caught before —
  a stale run would have matched 0).
- Full `ONEVO.Tests.Unit` suite (no filter) → **1622/1622 passed** (up from the prior report's
  1618 by exactly the 4 new tests added this pass; 0 regressions).
- `dotnet build tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore -p:BaseOutputPath=bin_verify\`
  → 0 errors; `dotnet vstest` against it → **548/548 passed, 0 failed** (unchanged from the prior
  report — this pass touched no architecture-guarded surface).
- `dotnet build tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore -p:BaseOutputPath=bin_verify\`
  → **0 errors** (only pre-existing `Testcontainers.PostgreSqlBuilder` obsolete-constructor
  warnings, unrelated). `docker info` → exit 1, daemon unavailable in this environment (same as
  every prior session on this branch) → integration tests **not run**, documented as skipped, not
  silently omitted.
- `git diff --check` → exit 0; only pre-existing LF→CRLF warnings on files already modified before
  this session (Windows checkout artifact), none on any file this session touched, no real
  whitespace/conflict errors.
- `dotnet ef migrations list` / `has-pending-model-changes` — not run, same rationale as the
  original report: no migration was added or needed (`DraftReason` is `varchar(50)` with no enum
  constraint, confirmed above), so there is nothing new for either check to catch.

## Frontend contract required next

- `POST /api/v1/onboarding/access-grant-requests/{id}/approve-and-send-invite` — no body,
  `employees:write` required. Success: `{ accessGrantRequestId, onboardingDraftId, employeeId,
  finalizationStatus, invitationQueued, checklistTaskCount, positionApprovalStatus, messageKey }`.
  Unchanged by this pass.
- `POST /api/v1/onboarding/access-grant-requests/{id}/reject` — body `{ decisionNote?: string }`
  (omit or `null` for no note), `employees:write` required. Success: `{ accessGrantRequestId,
  onboardingDraftId, requestStatus, draftStatus, draftReason, messageKey }`. **Changed by this
  pass:** `draftStatus`/`draftReason` after a successful reject are now typically `"draft"` /
  `"position_approval_rejected"` (previously always `"waiting_for_position_approval"` /
  `"waiting_for_position_approval"`, unchanged) — **except** when the draft had already moved on
  to a different outcome before this reject landed (the defensive guard above), in which case
  they reflect whatever that outcome already was. The frontend should treat this draft appearing
  in "My Drafts"/"Continue Draft" (rather than a permanently blocked state) as expected once
  `requestStatus: "Rejected"` is seen, and should still surface the *request's* own `Rejected`
  status as the "why" explanation, since the draft's own reason string
  (`position_approval_rejected`) is the only field carrying that context once the draft is back to
  editable.
- **"Request again" now works end-to-end** (previously explicitly blocked — see the struck-through
  "Remaining backend blockers" #5 above): re-saving the draft (with or without changes) and
  calling `finalize` again submits a fresh `AccessGrantRequest` if the current position/template
  still requires approval, or finalizes immediately if it no longer does. **"Cancel draft" still
  has no backend endpoint** — `OnboardingDraftsController` has no cancel/delete action; that part
  of the userflow doc's "Starter can edit draft, cancel draft, or request again" rule remains
  unimplemented and is unrelated to this pass's scope.
- Error shape is the existing `Problem()`/ProblemDetails convention (404/409/422) on both
  endpoints, same as `finalize`, with the same caveat `finalize`'s report already raised: a bare
  500 is still reachable via the known work-email length gap (see Remaining backend blockers #4)
  and is not endpoint-specific to this feature.
- No raw invitation token is ever returned by either endpoint.
