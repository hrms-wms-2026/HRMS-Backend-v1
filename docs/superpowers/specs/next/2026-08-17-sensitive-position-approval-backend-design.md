# Sensitive Position Approval + Change Position Action Menu — Backend Design

**Status:** Approved by user 2026-08-17, ready for implementation planning. Revised same day after live testing surfaced the position-based approver model (v1) as unnecessarily complex — v2 below replaces it with a permission-based model (`roles:manage`) and adds a self-position-change block found missing during the same review.

**Companion spec:** `Hrms--Web-application---front-end---v1/docs/superpowers/specs/2026-08-17-sensitive-position-approval-frontend-design.md`.

**Origin:** brainstormed live with the user 2026-08-17, triggered by testing the just-shipped "Change Position" action (sub-project 2) live and finding it should route sensitive positions through approval rather than assigning immediately. Grounded in the existing `AccessGrantRequest`/`PositionAccessTemplate` onboarding-approval machinery, cross-checked against the actual current codebase.

## 1. Goal

Extend "Change Position" (built in the employee-detail-screen sub-project) so that moving an existing employee into a position whose access template requires approval creates a pending request instead of assigning immediately — reusing the same `AccessGrantRequest` mechanism onboarding already uses for new hires, rather than building a parallel approval system. Fix a self-approval gap in onboarding's existing approval flow along the way. Block an employee from ever changing their own position. Give the "Change Position" button proper Promotion/Transfer/Offboarding entry points.

## 2. Current-state facts this design depends on

- `PositionAccessTemplate` (`src/ONEVO.Domain/Features/OrgStructure/Position/Entities/PositionAccessTemplate.cs`): `Id, TenantId, PositionId, RoleId, RequiresApproval, IsActive, CreatedAt, UpdatedAt`. **Unchanged by this spec** — v1 of this design added `ApprovingPositionId` here; v2 drops that entirely since the approver is now a fixed permission, not configured per-position.
- `roles:manage` (`PermissionSeeder.cs` line 172, `"Create and edit roles, assign permissions."`) already exists — this is the approval permission, not a new one.
- `AccessGrantRequest` (`src/ONEVO.Domain/Features/CoreHr/Entities/AccessGrantRequest.cs`) already has `ActionType` (string, currently only `AccessGrantActionType.EmployeeOnboarding = "onboarding_position_access"`) — designed to be extensible. `EmployeeId` is nullable specifically because onboarding defers employee creation until approval; for an existing employee it can be set immediately at request-creation time instead.
- Approval today (`AccessGrantRequestsController.cs` lines 40/62/75) is gated by plain `[RequirePermission("employees:write")]` — the same permission that lets someone create/finalize the onboarding draft in the first place, so the same person can both submit and approve a sensitive request. Real gap, confirmed during brainstorming.
- **No frontend UI currently calls `approveAccessGrantRequest`/`rejectAccessGrantRequest` at all** (both already exist on `PeopleApiService`, confirmed via repo-wide search — zero component references). Dead/orphaned API bindings from an earlier slice. The frontend companion builds the approvals UI from scratch.
- **No existing query resolves "every user in the tenant holding permission X"** — `IPermissionRepository.ListRolePermissionCodesWithModulesAsync` goes the other direction (given a user, list their permissions). This spec needs the inverse, new.
- `PositionListItemResponse`/`PositionResponse` (the position picker's response shape, used by `PositionApiService.listFlat`, which `change-position-modal.component.ts` already calls) does **not** currently expose whether a position requires approval — only `PositionAccessTemplateResponse` has `RequiresApproval`, and that's a separate call (`GetPositionAccess`). The frontend's sensitivity reminder needs this on the picker response directly, or a second round trip per selection — this spec adds the field to the picker response rather than requiring the extra call.
- `ChangeEmployeePositionCommandHandler`, `IPositionAssignmentRepository.TryReservePositionAssignmentAsync`/`ActivatePlannedAsync`/`CancelPlannedAsync`, and `RevokeEmployeeInvitationCommandHandler`'s already-decided/idempotency pattern (all from the multi-legal-entity-employment-foundation and employee-detail-screen sub-projects) are the direct precedents this design reuses.

## 3. Data model changes

### 3.1 `AccessGrantRequest` — add two columns, add an `ActionType` constant

```
ReservedPositionAssignmentId  Guid?   (the Planned PositionAssignment this request reserved - needed so approve/reject knows which row to activate or cancel)
ChangeReason                  string? ("Promotion" | "Transfer" | "LateralMove" - null for onboarding requests)
```

```csharp
public static class AccessGrantActionType
{
    public const string EmployeeOnboarding = "onboarding_position_access";
    public const string PositionChange = "position_change_access";
}
```

`EmployeeId`, `TargetPositionId`, `TargetDepartmentId`, `PositionAccessTemplateId`, `RequestedRoleId`, `RequestedByUserId` all already fit a position-change request's shape unchanged.

### 3.2 `PositionAssignment` — add `ChangeReason`

```
ChangeReason  string?  ("Promotion" | "Transfer" | "LateralMove", nullable - onboarding-created assignments have none)
```

Set on the assignment when a position-change request is approved (or immediately, for the non-sensitive path). Audit-trail only — no behavioral branching on this value anywhere in this spec.

## 4. Approver resolution (shared by onboarding and position-change)

New repository method, `IPermissionRepository.ListUserIdsWithPermissionCodeAsync(Guid tenantId, string permissionCode, DateTimeOffset now, CancellationToken ct)`:

```csharp
public async Task<IReadOnlyList<Guid>> ListUserIdsWithPermissionCodeAsync(
    Guid tenantId, string permissionCode, DateTimeOffset now, CancellationToken ct = default)
{
    return await _db.UserRoles
        .Where(ur => (ur.ExpiresAt == null || ur.ExpiresAt > now))
        .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur, rp })
        .Join(_db.Permissions, x => x.rp.PermissionId, p => p.Id, (x, p) => new { x.ur, p })
        .Where(x => x.p.Code == permissionCode)
        .Join(_db.Users, x => x.ur.UserId, u => u.Id, (x, u) => new { u.Id, u.TenantId })
        .Where(x => x.TenantId == tenantId)
        .Select(x => x.Id)
        .Distinct()
        .ToListAsync(ct);
}
```

(Mirrors the join shape `EfAuthRepository.ListRolePermissionCodesWithModulesAsync` already uses, inverted — filter by permission code instead of by user id.) Both approval paths call this with `"roles:manage"`. **Multiple holders:** all are valid approvers, first to act decides. **Zero holders:** extremely unlikely in practice (this permission typically sits on the tenant owner's default role) but not assumed — see §5's guard.

## 5. `ChangeEmployeePositionCommandHandler` — self-block + sensitive branch

**Self-position-change block (new, applies unconditionally, checked first):**

```csharp
var callerEmployee = await _commonEmployeeRepository.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
if (callerEmployee is not null && callerEmployee.Id == request.EmployeeId)
    return Result<Unit>.Forbidden("You cannot change your own position.");
```

This is a backend-enforced rule, not just a hidden button — the frontend also hides/disables the action on the caller's own detail page (companion spec), but per this codebase's own stated principle elsewhere ("hidden buttons must never be treated as security"), the handler checks it independently.

**Non-sensitive path:** unchanged from the employee-detail-screen sub-project.

**Sensitive path** (`accessTemplate.RequiresApproval == true`):

```csharp
var approverUserIds = await _permissionRepository.ListUserIdsWithPermissionCodeAsync(tenantId, "roles:manage", _clock.UtcNow, ct);
if (approverUserIds.Count == 0)
    return Result<Unit>.UnprocessableEntity("No one currently holds the permission required to approve this request.");

var reservedAssignmentId = await _positionAssignmentRepository.TryReservePositionAssignmentAsync(
    tenantId, employee.Id, position.Id, request.EffectiveFrom, _currentUser.UserId, ct);
if (reservedAssignmentId is null)
    return Result<Unit>.Conflict("This position has reached its capacity.");

var grantRequest = new AccessGrantRequest
{
    Id = Guid.NewGuid(),
    TenantId = tenantId,
    EmployeeId = employee.Id,
    ActionType = AccessGrantActionType.PositionChange,
    TargetPositionId = position.Id,
    TargetDepartmentId = position.DepartmentId!.Value,
    PositionAccessTemplateId = accessTemplate.Id,
    RequestedRoleId = accessTemplate.RoleId,
    ApprovalStatus = "Pending",
    RequestedByUserId = _currentUser.UserId,
    RequestedAt = _clock.UtcNow,
    EffectiveFrom = request.EffectiveFrom.ToDateTime(TimeOnly.MinValue),
    ReservedPositionAssignmentId = reservedAssignmentId,
    ChangeReason = request.ChangeReason,
};
await _accessGrantRequestRepository.AddAsync(grantRequest, ct);
await _unitOfWork.SaveChangesAsync(ct);

foreach (var approverUserId in approverUserIds)
    await EnqueueApprovalRequestEmailAsync(tenantId, approverUserId, grantRequest, ct);

return Result<Unit>.Success(Unit.Value); // request created, not yet effective - frontend shows "sent for approval"
```

## 6. `ApproveAccessGrantRequestCommandHandler` / `RejectAccessGrantRequestCommandHandler`

Route-level authorization simplifies to a static permission check now that the approver isn't per-record: both endpoints move from `[RequirePermission("employees:write")]` to `[RequirePermission("roles:manage")]`. Handlers still add the self-approval and already-decided checks in-handler (a static route permission can't express "not the same person who submitted this specific request"):

```csharp
if (grantRequest.RequestedByUserId == _currentUser.UserId)
    return Result<T>.Forbidden("You cannot approve or reject a request you submitted yourself.");
if (grantRequest.ApprovalStatus != "Pending")
    return Result<T>.Conflict("This request has already been decided.");
```

**Approve, `ActionType == EmployeeOnboarding`:** unchanged business logic — only the route's permission attribute and the added self-approval check are new.

**Approve, `ActionType == PositionChange`** (new branch): skip every draft-revalidation step (`OnboardingDraftId` is null — that's the discriminator). Flip `grantRequest.ReservedPositionAssignmentId` from `Planned` to `Active` (`ActivatePlannedAsync`). End the employee's previous `PrimaryEmployment` assignment (`GetActivePrimaryAsync` + `EndActiveAsync`, same pair the non-sensitive Change Position path already uses). Set `ChangeReason` on the newly-activated assignment from `grantRequest.ChangeReason`. Set `ApprovalStatus = "Approved"`, `DecidedByUserId`, `DecidedAt`.

**Reject, either `ActionType`:** unchanged shape for onboarding. For `PositionChange`: cancel the reservation (`CancelPlannedAsync`), freeing the seat — same as `RevokeEmployeeInvitationCommandHandler`'s revoke-frees-the-seat behavior.

## 7. Notification

Reuses the existing outbox pattern. New outbox message type, `OutboxMessageTypes.PositionChangeApprovalRequestEmail`, new handler, new email template case (`"position_change_approval_request"`). One outbox message per current `roles:manage` holder, enqueued at request-creation time. Onboarding's existing sensitive-position path gets the same treatment (it sends nothing today) — same message type/template, fired from `FinalizeOnboardingDraftCommandHandler`'s `FinalizeWithPendingApprovalAsync` branch, recipients resolved the same way via §4.

## 8. `GET /api/v1/onboarding/access-grant-requests/pending-for-me`

New read endpoint, `[RequirePermission("roles:manage")]` (simpler than v1 — being a valid approver at all now *is* holding this permission, no per-record occupancy check needed for the list view). Returns every `Pending` `AccessGrantRequest` tenant-wide (any caller who can reach this endpoint can act on any of them, per §6). Response: `Id, ActionType, EmployeeName (nullable - null for onboarding requests where EmployeeId is still null; use InvitedFullName/draft name instead), TargetPositionName, ChangeReason (nullable), RequestedByName, RequestedAt`. Powers the frontend companion's Approvals inbox.

## 9. Position picker response — expose `RequiresApproval`

`PositionListItemResponse` (or whichever DTO backs `GET /org/legal-entities/{id}/positions`, consumed by `PositionApiService.listFlat`) gains `RequiresApproval` (bool), sourced the same way `PositionListItemResponse` already resolves other position-scoped facts (a join to `PositionAccessTemplate` where `IsActive = true`, false if none exists). Lets the frontend show the sensitivity reminder the moment a position is selected in the Change Position modal, no second round trip.

## 10. Testing

- Unit: `ChangeEmployeePositionCommandHandlerTests` (self-position-change rejection, sensitive-branch zero-approver rejection, capacity-conflict, successful pending-request creation with reservation + ChangeReason carried through), `ApproveAccessGrantRequestCommandHandlerTests`/`RejectAccessGrantRequestCommandHandlerTests` (both `ActionType`s, self-approval rejection, already-decided conflict, successful approve activates+ends+sets ChangeReason, successful reject cancels reservation), `PermissionRepositoryTests` for `ListUserIdsWithPermissionCodeAsync`.
- Integration: two `roles:manage` holders, one approves, the other's subsequent attempt gets the already-decided conflict; end-to-end position-change-with-approval against real Postgres verifying the seat stays reserved throughout the pending window; self-position-change rejected even for a caller who holds `org:manage`.

## 11. Self-review

- No placeholders — every field traces to an explicit current-codebase fact (§2) or a decision made during brainstorming (including the v1→v2 revision itself).
- Internal consistency: v1's `PositionAccessTemplate.ApprovingPositionId`/`SetPositionAccessCommand` changes are fully removed, not left as dead references alongside the new model.
- Scope: onboarding's approval authorization is intentionally changed too (explicit user decision to unify both flows, not leave the older one's self-approval bypass unfixed) — the one place this spec touches already-shipped code, called out rather than buried.
- §9 (position picker exposing `RequiresApproval`) was added because the frontend's sensitivity-reminder requirement has no way to work without it — surfaced here rather than left as a cross-spec gap the way §8 was caught in v1.
