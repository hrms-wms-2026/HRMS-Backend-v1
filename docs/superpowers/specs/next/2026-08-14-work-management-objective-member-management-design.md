# Work Management — Objective Member Management (Invite/Accept) — Backend Design

**Status:** Designed, pending implementation planning.

**Scope guardrail:** Work Management module only — `ONEVO.Domain/Features/WorkManagement/*`, `ONEVO.Application/Features/WorkManagement/*`, `ONEVO.Api/Controllers/Tenant/WorkManagement/*`, related EF migrations/configurations, `docs/postman-request/Work Management/`. Do not touch Core HR (`EmployeesController` etc.), Org Structure, or any other module — those are a teammate's active work. `GET /api/v1/employees` is consumed read-only (as the frontend's people-picker source) and never modified.

**Origin:** brainstormed live with the user 2026-08-14 via `superpowers:brainstorming`.

**Builds on:** `docs/superpowers/specs/finished/2026-08-04/2026-08-04-work-management-milestone-hierarchy-design.md` (Head/Reporting-Manager model, `objective_change_requests`) and `docs/superpowers/specs/finished/2026-08-08/2026-08-06-work-management-milestone-membership-and-achieve-design.md` (current direct-add `project_members` model). Does not modify either of those flows for the case where a real approval routing target exists — see §5.

---

## 1. Goal

Today, Add/Remove Objective Member are immediate, unconditional actions — no acceptance step. There is no way to list an objective's members (real + pending) in one call, and no way to add members/a leader at objective-creation time. This design adds an invite → accept membership model, reusing a table that was scaffolded on day one (2026-08-03, `AddWorkManagementFoundation` migration) and never wired to any API: `project_member_invitations`.

**Explicitly out of scope for this slice** (per user 2026-08-14): the existing Reporting-Manager approval system for Transfer/Edit/Delete (`objective_change_requests`) is untouched except for one additive branch (§5). Objective Edit approval is untouched entirely, deferred to a future slice.

## 2. Current-state findings (verified against code, not just docs)

- `ProjectMemberInvitation` entity (`src/ONEVO.Domain/Features/WorkManagement/ProjectInvitations/Entities/ProjectMemberInvitation.cs`), its EF configuration, and its DB table exist and are fully migrated — but are referenced **nowhere else** in the codebase (no repository, handler, or controller). It currently has no `Role`/`Type` column ("Forbidden: role" per `phase1-table-inventory.md`).
- `AddObjectiveMember`/`RemoveObjectiveMember` handlers write directly to `project_members`, bypassing the invitations table entirely.
- `GetObjectiveById` returns a single `ownerId`/`reportingManagerId` — there is no members-list endpoint anywhere in the API.
- `objectives.reporting_manager_id` is nullable and, per `phase1-table-inventory.md`, is dynamically kept equal to the **parent** Objective's current owner (cascaded one level on Transfer). It is `null` only for the per-project Default Objective — which `Transfer` already rejects with `400` regardless of this design. The new branch in §5 is therefore expected to fire rarely/never in practice; it is implemented anyway per explicit user instruction, as a defensive/complete case rather than a commonly-hit path.

## 3. Schema change

`project_member_invitations` — add one column and two partial-unique constraints:

- `invite_type varchar(20) NOT NULL` — `'member'` | `'leader'`. New `ProjectInvitationTypes` static class (`Member`, `Leader`), mirroring the existing `ProjectInvitationStatuses` style in the same file.
- Partial unique on `(tenant_id, objective_id, invited_user_id) WHERE status = 'pending'` — can't double-invite the same person to the same objective.
- Partial unique on `(tenant_id, objective_id) WHERE status = 'pending' AND invite_type = 'leader'` — at most one pending leader-designate per objective at a time, enforcing "creator/current head stays owner until accepted" at the DB level, matching the existing `objective_change_requests` pending-uniqueness pattern.

New migration: `AddProjectMemberInvitationTypeAndUniqueness`.

## 4. API changes — member add/remove/list/accept/reject

**4.1 `POST /objectives/{id}/members` (Add Member) — same route, changed behavior.**
Old: immediate add to `project_members`, `204`. New: if `userId` is already an active member → `204` no-op (unchanged). Otherwise creates a `project_member_invitations` row (`invite_type='member'`, `status=pending`) → `202 Accepted` with the invitation body. `409` if a pending invite already exists for that user on this objective. Permission/validation otherwise unchanged (Head-only, `projects:access`, `400` if achieved).

**4.2 `DELETE /objectives/{id}/members/{userId}` (Remove Member) — same route, extended.**
If `userId` is an active member → deactivate (unchanged, still rejects removing the current Head). Else if `userId` has a pending invitation on this objective → cancel it (`status='cancelled'`). Else → `404` (unchanged). One route covers both "remove a real member" and "the Head cancels a request they sent" — no separate cancel endpoint.

**4.3 `GET /objectives/{id}/members` — new.**
Returns active `project_members` (real, with an `isHead` flag) merged with pending `project_member_invitations` for this objective (tagged `pending: true`, `inviteType`, `invitedAt`). Permission: same visibility rule as `GetObjectiveById` (`projects:read`/`*` OR active membership on this objective or an ancestor).

**4.4 `POST /objectives/invitations/{invitationId}/accept` — new.**
Caller must be `invited_user_id`. `invite_type='member'` → create/reactivate the `project_members` row. `invite_type='leader'` → reassign the objective's Head (same side effects Transfer already performs: sync project membership for both heads, cascade `ReportingManagerId` to direct children, drop the old head's participation if they have no other active access). Marks the invitation `accepted`. `409` if already decided.

**4.5 `POST /objectives/invitations/{invitationId}/reject` — new.**
Caller must be `invited_user_id`. Marks `declined`. No side effects — for a leader invite, the current Head simply remains Head.

**4.6 `GET /objectives/invitations/mine` — new.**
Lists the caller's own pending invitations across all objectives. Necessary because an invited person may not have any access to the target objective yet — without this, they'd have no way to discover or act on the invite. Filtered to `status='pending'` by default.

## 5. Transfer — same route, one new branch

`POST /objectives/{id}/transfer`:

- `objectives.reporting_manager_id IS NOT NULL` → **unchanged**: immediate if the caller created the objective, else creates an `objective_change_requests` row (`request_type: transfer`) routed to that Reporting Manager for approval, exactly as today.
- `objectives.reporting_manager_id IS NULL` → **new**: no approval routing. Creates a `project_member_invitations` row (`invite_type='leader'`, `status=pending`) addressed to `newHeadUserId`, returns `202`. The caller remains Head until the invitee accepts via §4.4.

All existing validations are unchanged: `400` for the Default Objective, `400` if achieved, `403` if caller isn't current Head, `409` if a request/invite is already pending.

## 6. Create Objective — new optional field

`POST /objectives` gains an optional `memberInvitations: [{ userId: guid, type: 'member' | 'leader' }]` (default empty). The creator becomes owner immediately (unchanged rule — the creator never needs approval for their own creation). Each entry then creates a pending invitation exactly as §4.1/§4.7 would — the creation-time path and the popup's "Add member"/"Assign leader" actions share one invitation-creation code path, invoked in-process rather than as separate HTTP round-trips.

## 7. Handler / code organization

Follows the existing Work Management CQRS layout:

- `Commands/AddObjectiveMember/...`, `Commands/RemoveObjectiveMember/...` — modify existing handlers.
- `Commands/AcceptObjectiveInvitation/...`, `Commands/RejectObjectiveInvitation/...` — new, modeled directly on `ApproveObjectiveChangeRequest`/`RejectObjectiveChangeRequest`.
- `Queries/GetObjectiveMembers/...`, `Queries/GetMyObjectiveInvitations/...` — new.
- `Commands/TransferObjectiveHead/...` — modify existing handler, add the null-RM branch.
- `Commands/CreateObjective/...` — modify existing handler, add the optional invitations loop.
- First real repository/DbContext usage of `ProjectMemberInvitation` — no existing repository methods to extend.

## 8. Postman docs to update/add

Update: `Add Objective Member.md`, `Remove Objective Member.md`, `Transfer Objective Head.md`, `Create Objective.md`.
New: `Get Objective Members.md`, `Accept Objective Invitation.md`, `Reject Objective Invitation.md`, `My Objective Invitations.md`.

## 9. Error handling / edge cases

- Inviting someone already an active member as `'leader'` still goes through accept (becomes pending leader-designate) — accept has real side effects (Head reassignment) beyond plain membership, so it can't short-circuit.
- Inviting an inactive/non-existent employee → `400`, same as today's Add Member validation.
- Achieved (frozen) objective → `400` on invite-send, accept, and reject alike, consistent with the existing achieved-freeze rule.
- Double-decision race (two accept/reject clicks) → `409`, consistent with the existing `objective_change_requests` pattern.

## 10. Out of scope for this slice

- The Reporting-Manager approval path itself (`objective_change_requests` for Transfer/Edit) — untouched except §5's additive branch.
- Objective Edit approval — untouched, deferred.
- Invitation expiry (`expires_at` exists on the table; no TTL/reaper job is being built now — left `null`).
