# Multi-Legal-Entity Employment Foundation — Design

**Status:** Approved by user 2026-08-16, ready for implementation planning.

**Sub-project 1 of 2** in a decomposed feature request (Employee Detail screen, position
capacity, invitations, cross-legal-entity employment, company switcher). This spec is the
**foundational** half everything else depends on. Sub-project 2 (Employee Detail screen,
sensitive-field gating consuming the coverage/visibility infra confirmed here, "Change
Position" action) is a separate design, written after this one is planned/implemented.

**Origin:** brainstormed live with the user 2026-08-16 via `superpowers:brainstorming`,
grounded in `OneVo-HR/Userflow/Employee-Management/profile-management.md`,
`OneVo-HR/Userflow/Auth-Access/user-invitation.md`, `OneVo-HR/Userflow/Org-Structure/position-setup.md`,
`OneVo-HR/database/phase1-table-inventory.md`, `ONEVO_Backend_Architecture_Document.md`,
`ONEVO_HRMS_Frontend_Architecture.md`, cross-checked against the actual current codebase
(not assumed from docs — see §1 for concrete divergences and confirmations found).

---

## 1. Current-state facts this design depends on

Verified directly against the codebase on branch `feature/employee-management-phase1-foundation`
(confirmed identical commit to `feature/employee-profile-backend`, i.e. yesterday's Employee
Self-Service Profile work is already merged into this base):

- `Employee` (`src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs`) has a single
  `LegalEntityId` FK — no multi-entity employment model exists. One `Employee` row = one
  employment record in one legal entity, sharing a `UserId` FK to `User`.
- `IEmployeeRepository.EmailExistsAsync(tenantId, email, excludeId)` (impl:
  `EfEmployeeRepository.cs:218`) is scoped by `tenantId` only — called at 3 sites:
  `SaveOnboardingDraftCommandHandler:73`, `FinalizeOnboardingDraftCommandHandler:184`,
  `ApproveAccessGrantRequestCommandHandler:179`. All three currently block a second employee
  record for the same email anywhere in the tenant, regardless of legal entity.
- **Coverage-manager visibility is already fully implemented, not a gap.**
  `EmployeeVisibilityScopeResolver` (`Infrastructure/Persistence/Repositories/CoreHr/`)
  resolves the caller's active `PrimaryEmployment` position, looks up
  `ManagementCoverageRecord` rows owned by that position (`CoveredTargetType`
  Position/Department/Company), and returns an `EmployeeVisibilityScope` consumed by
  `GetEmployeeQueryHandler`/`ListEmployeesQueryHandler`/`GetVisibleByIdAsync`/`ListVisibleAsync`.
  Sub-project 2 (Employee Detail) reuses this as-is — no changes needed here.
- **Position capacity** (`Position.MaxOccupancy`) is checked in two handlers —
  `FinalizeOnboardingDraftCommandHandler` and `ApproveAccessGrantRequestCommandHandler`
  (`ApproveAccessGrantRequestCommandHandler.cs:202-204`, comment: "same signal
  FinalizeOnboardingDraftCommandHandler uses") — both via
  `_positionAssignmentRepository.CountActiveAsync(tenantId, position.Id, ct)` compared to
  `position.MaxOccupancy`. Only `active` assignments are counted, so two concurrent
  approvals/finalizes against the last vacant seat can both pass this check before either
  commits — a real race.
- `PositionAssignment.AssignmentStatus` (`Domain/Features/CoreHr/PositionAssignment/Entities/PositionAssignment.cs`)
  already has a `Planned` value (`"planned"`, alongside `Active`/`Ended`/`Cancelled`) —
  currently unused by any handler. This is exactly the primitive needed for capacity
  reservation (§3).
- `InvitationToken` (`Domain/Features/Auth/Invite/Entities/InvitationToken.cs`) already has
  `UserId`, `PositionId`, `LegalEntityId`, `EmployeeId`, `RevokedAt`, `RevokedById` — all
  nullable, all present, none wired to an employee-invitation revoke path today. Only a
  DevPlatform-side revoke exists (`RevokePlatformUserInvite`, unrelated entity).
- **Accept always requires a password today.** Both `AcceptEmployeeInvitationCommandHandler`
  and `AcceptInvitationPasswordCommandHandler` (`Application/Features/Auth/Invite/Commands/`)
  set `user.PasswordHash` unconditionally — no branch for "this email already has an active,
  credentialed `User` account."
- **Permissions confirmed NOT broken** (ruled out during investigation, listed here so this
  isn't silently re-litigated later): `payroll:read/write/approve/run` are forward-seeded for
  a not-yet-built Payroll module and are referenced by
  `Application/Features/Auth/Permission/Helpers/DerivedPermissions.cs`'s role-derivation
  lists — not orphaned, not touched by this spec. Bank-detail read/write reusing
  `employees:write` is yesterday's deliberate, shipped decision
  (`specs/next/2026-08-15-employee-self-service-profile-backend-design.md` §6) — not touched.
  `EmployeesController` invitation-adjacent actions (`Resend`) currently sit on
  `[RequirePermission("employees:write")]` — this **is** a real gap (§2).
- No `invitations:*` permission exists anywhere in `PermissionSeeder.cs`.
- No "active legal entity" concept exists in `CurrentUserService` or session state
  (grep for `CurrentLegalEntity`/`ActiveLegalEntity`/`LegalEntityContext` returns nothing).
  Permission resolution today is `User → Position → Role → RolePermissions`, implicitly
  assuming one Employee/Position per User.
- Frontend `company-selector.component.ts` (`layouts/main-layout/top-navbar/`) +
  `LegalEntityStore.selectCompany()` only patches local UI state today — no backend call.

## 2. Permission & seeding changes

- Add **`invitations:manage`** to `PermissionSeeder.cs`. Re-point
  `ResendEmployeeInvitationCommandHandler`'s endpoint and the new Revoke endpoint (§3) to it,
  off their current `employees:write` reuse. Invitation lifecycle management is a distinct
  action family from employee-record CRUD; bundling it under `employees:write` means anyone
  who can edit an employee's job info can also manage invites, which isn't the same
  responsibility. Existing `employees:write` usages elsewhere are untouched.
- `employees:read:sensitive` is deliberately **not** added here — no consumer exists in this
  spec (see §1's `payroll:*` cautionary note). It's added together with the Employee Detail
  query that actually checks it, in sub-project 2.
- No other seeding changes. `payroll:*` stays as forward-seeded, untouched.

## 3. Position capacity reservation

Close the race between concurrent invites/approvals for the last vacant seat.

- When an invitation is created (i.e. at `FinalizeOnboardingDraftCommandHandler` /
  `ApproveAccessGrantRequestCommandHandler`'s existing commit point — no new command needed,
  these are the two places seats are already claimed today), insert a `PositionAssignment`
  row with `AssignmentStatus = Planned` for the target seat, inside the same transaction as a
  row-level lock on the `Position` row (`SELECT ... FOR UPDATE` via EF Core, mirroring the
  storage-quota lock-then-check-then-reserve pattern already used elsewhere per
  `ONEVO_Backend_Architecture_Document.md`).
- The capacity check in both handlers changes from `CountActiveAsync` to a new
  `CountActiveOrPlannedAsync(tenantId, positionId, ct)` on `IPositionAssignmentRepository`,
  compared against `position.MaxOccupancy`. Lock acquisition + count + insert happen inside
  one transaction so two concurrent requests serialize instead of racing.
- On **accept** (§4): the `Planned` row flips to `Active`.
- On **revoke** (§4, new) or **expiry** (existing expiry path — locate and confirm it exists
  as part of implementation task 1, since no expiry-sweep handler was found during
  investigation and may need its own small fix if missing): the `Planned` row flips to
  `Cancelled`, freeing the seat.

## 4. Invitation lifecycle fixes

### 4.1 Cross-legal-entity duplicate check split

`EmailExistsAsync` becomes two checks used at all 3 call sites (§1):

- **`EmployeeExistsInLegalEntityAsync(tenantId, legalEntityId, email, excludeId)`** — blocks
  (409, unchanged message) if an `Employee` row already exists for this email in the *target*
  legal entity.
- **`FindExistingUserByEmailAsync(tenantId, email)`** — if a `User` row exists for this email
  in the tenant (any legal entity) but the above check is false, the invite proceeds down the
  existing-user link path (§4.3) instead of being rejected.
- If neither matches, proceeds down the standard new-person path unchanged.

### 4.2 Revoke invitation (new)

`RevokeEmployeeInvitationCommandHandler`, gated by `invitations:manage` (§2). Sets
`InvitationToken.Status = "revoked"`, `RevokedAt`, `RevokedById` (all pre-existing columns).
Cancels the associated `Planned` position_assignment (§3), freeing the seat. New endpoint:
`POST /api/v1/employees/invitations/{invitationId}/revoke`.

### 4.3 Passwordless accept for existing-tenant users

When invite creation hit the existing-user branch (§4.1), the `InvitationToken` is created
with `CompletedWith` pre-set to a new constant (e.g. `"linked_account"`) so the accept handler
knows which path to take without re-deriving it. `AcceptEmployeeInvitationCommandHandler`
branches on this:

- **Linked-account path**: no password step. The person authenticates with their existing
  credentials (existing login flow); accepting just validates the token, finalizes the new
  `Employee` row for the target legal entity (pre-filled personal-identity fields — name, DOB,
  nationality, personal contact — sourced from their other `Employee` row(s) as onboarding-draft
  defaults, editable, stored independently per row), flips the reserved `Planned`
  position_assignment to `Active`, marks the invite used.
- **Standard path** (brand-new person): unchanged, password set as today.

`AcceptInvitationPasswordCommandHandler` is unaffected — it's a distinct, already-narrower
password-specific step only reached by the standard path.

## 5. Session-level active-entity + permission recompute

- Extend the existing session record with `ActiveEmployeeId` (`Guid`). On login, default it to
  the user's sole `Employee` row, or their most-recently-active one if they have several
  (tie-broken by most recent `PositionAssignment.EffectiveFrom` among their `Active`
  `PrimaryEmployment` assignments).
- New endpoint `POST /api/v1/session/active-company` — body `{ employeeId }`. Validates the
  target `Employee` row's `UserId` matches the caller, updates the session's
  `ActiveEmployeeId`. Returns the refreshed permission/role set (frontend re-fetches session
  state after switching, same shape as initial login response).
- `CurrentUserService` changes: Position resolution becomes "the active Employee's active
  `PrimaryEmployment` position_assignment" instead of assuming exactly one Employee per User.
  The existing `User → Position → Role → RolePermissions` chain is otherwise unchanged — this
  is a scoping change at the Position-lookup step, not a new resolution mechanism.
- Frontend: `company-selector.component.ts` calls the new endpoint instead of only patching
  `LegalEntityStore` locally, and triggers a session/permission re-fetch on switch so
  permission-gated UI (nav items, buttons via `permission.directive.ts`) updates immediately.

## 6. Contracts & testing

- New DTOs: `src/ONEVO.Api/Contracts/Auth/Invitation/RevokeInvitationRequest.cs`,
  `src/ONEVO.Api/Contracts/Auth/Session/SwitchActiveCompanyRequest.cs`.
- **Unit** (`Tests.Unit`, mirroring existing handler-test conventions): capacity reservation
  (lock + count-active-or-planned, both success and at-capacity-with-planned-seat rejection),
  `RevokeEmployeeInvitationCommandHandlerTests` (happy path, already-accepted/already-revoked
  rejections, seat freed), `EmployeeExistsInLegalEntityAsync`/`FindExistingUserByEmailAsync`
  split (same-entity duplicate still blocks, cross-entity existing-user routes to link path,
  brand-new email routes to standard path), linked-account accept branch (no password set,
  `Employee` row created in target entity, `Planned`→`Active`), permission resolution against
  `ActiveEmployeeId` (switching returns the target entity's position-derived permission set,
  not the previous entity's).
- **Integration** (`Tests.Integration`, Testcontainers): two concurrent
  finalize/approve requests against a 1-vacancy position — exactly one succeeds, the other
  gets the capacity-conflict error; invite an existing tenant user (already an `Employee` in
  legal entity A) into legal entity B end-to-end, accept without a password, confirm two
  `Employee` rows now share one `UserId`; switch active company via the new endpoint and
  confirm the returned permission set changes to reflect the new entity's position; revoke an
  invitation and confirm the reserved seat becomes available again for a new invite.
- Seeder change (`invitations:manage`) gets a seed-idempotency assertion consistent with
  existing seeding tests.

## 7. Out of scope (deferred to sub-project 2)

- Employee Detail screen UI/fields/sections.
- `employees:read:sensitive` permission and the Employee Detail query that checks it.
- "Change Position" action UI, wired to the existing Transfer/Promotion workflow (not a raw
  inline edit — confirmed direction during brainstorming).
- Any change to Payroll & Statutory read/write behavior beyond what's already shipped.

## 8. Self-review

- No placeholders — every claim traces to a specific file/line found during investigation
  (§1) or a specific `OneVo-HR` doc.
- Internal consistency: §3's capacity reservation, §4.1's duplicate-check split, and §4.3's
  linked-account accept all interlock at the same transaction boundaries (invite creation,
  accept) rather than being designed independently and reconciled after the fact.
- Scope: deliberately narrowed after investigation — coverage-manager enforcement,
  `payroll:*` seeding, and bank-detail write permission were all initially flagged as gaps
  during brainstorming and are explicitly *not* touched here once verified as already-correct
  or already-deliberate (§1, §2). This keeps the spec to the genuine gaps only.
- Divergences from initial brainstorming are called out explicitly (§1, §2) rather than
  silently carried through from the earlier, less-verified draft.
