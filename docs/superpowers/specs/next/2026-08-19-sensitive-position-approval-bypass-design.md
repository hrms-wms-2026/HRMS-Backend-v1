# `roles:manage` Bypass for Sensitive-Position Approval — Backend Design

**Status:** Approved by user 2026-08-19, ready for implementation planning.

**Companion spec:** `2026-08-17-sensitive-position-approval-backend-design.md` (same folder) — this design extends that machinery; read it first for the base `AccessGrantRequest`/`PositionAccessTemplate.RequiresApproval` model this builds on.

**Origin:** User request: "even if a position was sensitive that needs to be approved to be changed or onboarded, if the person doing the onboarding/promotion/transfer has `roles:manage` then they can do it without approval needed." Brainstormed live 2026-08-19.

## 1. Goal

When a position requires approval (its active `PositionAccessTemplate.RequiresApproval == true`) and the acting user already holds `roles:manage`, the onboarding or position-change (transfer) action completes immediately instead of being queued for someone else to approve. An `AccessGrantRequest` audit row is still written, pre-stamped `Approved`, so the sensitive-position change remains visible in approval history even though no one had to act on it.

**Promotion is explicitly out of scope.** No promotion workflow exists in this codebase (confirmed in `ChangeEmployeePositionCommandHandler`'s own doc comment — only onboarding and position-change/"transfer" exist). This bypass applies only to those two existing flows.

**Self-transfer stays blocked regardless of `roles:manage`.** `ChangeEmployeePositionCommandHandler`'s self-position-change check (`employee.UserId == _currentUser.UserId` → `Forbidden`) runs before the `RequiresApproval` branch is reached and is unconditional — this design does not touch it. A `roles:manage` holder still cannot move themselves into a sensitive position without someone else acting.

## 2. Current-state facts this design depends on

- `AccessGrantRequest.DecisionNote` (`string?`) already exists (`src/ONEVO.Domain/Features/CoreHr/Onboarding/Entities/AccessGrantRequest.cs:31`) — used here to record the bypass reason, no schema change needed.
- `IPermissionRepository.UserHasPermissionCodeAsync(Guid userId, string permissionCode, DateTimeOffset now, CancellationToken ct)` already exists and is a single-user check (distinct from `ListUserIdsWithPermissionCodeAsync`, which resolves all approvers). Both handlers below already have `IPermissionRepository` injected — no DI changes needed.
- `ChangeEmployeePositionCommandHandler` (`src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandHandler.cs`): sensitive branch at lines 112-185 reserves a `Planned` seat, creates a `Pending` `AccessGrantRequest`, emails approvers, returns `PendingApproval: true`.
- `ApproveAccessGrantRequestCommandHandler` (`src/ONEVO.Application/Features/CoreHr/Onboarding/Commands/ApproveAccessGrantRequest/ApproveAccessGrantRequestCommandHandler.cs`), `ActionType == PositionChange` branch (lines 131-192): the exact "activate reserved seat, end old assignment, mark request Approved" sequence this design's transfer bypass reuses synchronously instead of on a second request.
- `OnboardingDraftWriteService` (`src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs`): `requiresApproval` computed at line 369, branches to `FinalizeWithPendingApprovalAsync` (logs a `Pending` request, defers all creation, lines 379-437) or `FinalizeImmediatelyAsync` (creates user/employee/role/invitation now, lines 439+).
- `FinalizeImmediatelyAsync`'s role-assignment guard (line 564) currently reads `accessTemplate is { IsActive: true, RequiresApproval: false }` — i.e. it only auto-assigns the access template's role when approval wasn't required. This must change for the bypass case, where `RequiresApproval` is true but the actor is self-authorizing.

## 3. `ChangeEmployeePositionCommandHandler` — bypass branch

Immediately after `accessTemplate is { RequiresApproval: true }` is confirmed (line 113) and the department-null check passes, resolve the bypass:

```csharp
var bypassApproval = await _permissionRepository.UserHasPermissionCodeAsync(
    _currentUser.UserId, "roles:manage", _clock.UtcNow, ct);
```

**If `bypassApproval` is true:** inside the same transaction that today only reserves the seat (lines 135-167), additionally:
1. Fetch the employee's current active primary assignment (`GetActivePrimaryAsync`) and end it (`EndActiveAsync`) — same effective-date logic already used in the non-sensitive path (lines 194-205).
2. Activate the just-reserved assignment (`ActivatePlannedAsync`), mirroring `ApproveAccessGrantRequestCommandHandler`'s `PositionChange` branch.
3. Create the `AccessGrantRequest` pre-stamped decided:
   ```csharp
   ApprovalStatus = "Approved",
   DecidedByUserId = _currentUser.UserId,
   DecidedAt = _clock.UtcNow,
   DecisionNote = "Self-authorized: requester holds roles:manage.",
   ```
   (all other fields unchanged from today's pending-branch construction)
4. Skip the approver-email loop entirely — no one needs to act.
5. Return `Result<ChangeEmployeePositionResponse>.Success(new ChangeEmployeePositionResponse(PendingApproval: false))`, same shape as the non-sensitive path.

**If `bypassApproval` is false:** existing behavior, unchanged.

The existing `hasPendingChange` conflict check and `PositionAtCapacityException`/`ConcurrencyConflictException`/`UniqueConstraintConflictException` handling wrap the bypass path the same as today's pending path — no new exception types.

## 4. `OnboardingDraftWriteService` — bypass branch

At line 369-374, resolve the bypass alongside `requiresApproval`:

```csharp
var requiresApproval = accessTemplate is { IsActive: true, RequiresApproval: true };
var bypassApproval = requiresApproval
    && await _permissionRepository.UserHasPermissionCodeAsync(actingUserId, "roles:manage", _clock.UtcNow, ct);

if (requiresApproval && !bypassApproval)
{
    return await FinalizeWithPendingApprovalAsync(draft, accessTemplate!, position!, actingUserId, ct);
}

return await FinalizeImmediatelyAsync(draft, accessTemplate, position, employmentTypeId.Value, actingUserId, bypassApproval, ct);
```

`FinalizeImmediatelyAsync` gains a `bool selfAuthorizedBypass` parameter:

- **Role assignment (line 564):** condition changes from `accessTemplate is { IsActive: true, RequiresApproval: false }` to `accessTemplate is { IsActive: true } && (!accessTemplate.RequiresApproval || selfAuthorizedBypass)` — the role is still granted when a bypass authorized the sensitive position.
- **Audit record:** when `selfAuthorizedBypass` is true, once `employeeId` and `user.Id` are resolved (inside the existing transaction, after the employee row is added, before the final save), create an `AccessGrantRequest`:
  ```csharp
  Id = Guid.NewGuid(),
  TenantId = draft.TenantId,
  EmployeeId = employeeId,
  UserId = user.Id,
  OnboardingDraftId = draft.Id,
  ActionType = AccessGrantActionType.EmployeeOnboarding,
  TargetPositionId = draft.PositionId!.Value,
  TargetDepartmentId = position!.DepartmentId!.Value,
  PositionAccessTemplateId = accessTemplate!.Id,
  RequestedRoleId = accessTemplate.RoleId,
  ApprovalStatus = "Approved",
  RequestedByUserId = actingUserId,
  DecidedByUserId = actingUserId,
  RequestedAt = _clock.UtcNow,
  DecidedAt = _clock.UtcNow,
  EffectiveFrom = ToUtcMidnight(draft.StartDate),
  ReservedPositionAssignmentId = reservedAssignmentId,
  DecisionNote = "Self-authorized: requester holds roles:manage.",
  ```
  added via `_accessGrantRequestRepository.AddAsync(...)`, flushed by the transaction's existing `SaveChangesAsync`.
- Everything else in `FinalizeImmediatelyAsync` (seat check, checklist instantiation, invitation, outbox email) is unchanged and untouched by the bypass flag.

## 5. Frontend impact

None required. Both endpoints already return the same response shape (`PendingApproval: false`) that a non-sensitive change returns; the frontend's existing "sent for approval" vs. "done" branching needs no changes. The Approvals inbox (`GET .../pending-for-me`, `GET .../access-grant-requests?status=pending`) already filters to `Pending` only, so bypass-created `Approved` rows never appear there — they only surface if someone explicitly queries `status=approved` history.

## 6. Testing

- **Unit** — `ChangeEmployeePositionCommandHandlerTests`: bypass actor gets `PendingApproval: false` and the position is active immediately; non-bypass actor still gets queued (`PendingApproval: true`); self-transfer still `Forbidden` even when the caller holds `roles:manage`; capacity conflict during bypass rolls back cleanly (no orphaned `Approved` request). `OnboardingDraftWriteServiceTests` (or equivalent finalize handler tests): bypass actor's draft finalizes immediately (`Finalized` status, invitation queued) with role assigned despite `RequiresApproval: true`; non-bypass actor still lands in `WaitingForPositionApproval`.
- **Integration** — extend `SensitivePositionChangeApprovalIntegrationTests.cs`: a `roles:manage` holder changing another employee into a sensitive position succeeds immediately against real Postgres, and the resulting `AccessGrantRequest` row is `Approved` with `DecidedByUserId` set to the actor and the expected `DecisionNote`; a non-`roles:manage` actor doing the same still produces a `Pending` row and does not move the assignment. Equivalent pair for onboarding finalize.

## 7. Self-review

- No placeholders — every referenced method/field/line traces to code read directly from the current repo during this design session.
- Internal consistency: the self-transfer block and the bypass are confirmed independent (§1) — no code path lets `roles:manage` skip both.
- Scope: promotion is explicitly excluded (§1) since it has no implementation to attach a bypass to; flagged rather than silently ignored.
- No new migration, no new DI registration, no new repository method — reuses `UserHasPermissionCodeAsync` and `AccessGrantRequest.DecisionNote`, both already present.
