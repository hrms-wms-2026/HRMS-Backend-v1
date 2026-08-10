# Milestone Ownership Signal + Subtree Access Loosening — Design

**Status:** Approved 2026-08-10 (brainstormed same day). No plan written yet. Backend half of a cross-repo feature — frontend half: `Hrms--Web-application---front-end---v1/docs/superpowers/specs/next/2026-08-10-milestone-cards-and-tree-view-design.md`.

## Problem

The frontend is building a Milestone card grid (Project Detail page) where each card conditionally shows Edit/Achieve buttons only to the milestone's owner (Head), and a View button (open to any project member) that opens a tree-view page. Two backend gaps block this:

1. **No ownership signal reaches the frontend.** `GetMyProjectMilestonesQueryHandler`'s own doc says the frontend should compare `ownerId` to "your own userId" — but no `userId` is exposed anywhere in the tenant session response (`AuthSessionViewModel.user` is `{ email }` only) or the frontend's `AuthStore`. `Project` already solved this the other way: `ProjectViewModelMapper` computes `isLead` server-side so the frontend never needs its own user id.
2. **`GetObjectiveSubtree` is Head-only.** Its permission check requires the caller to be the target Objective's current Head (`403` otherwise). A non-owner project member clicking View on a milestone they're not the Head of would get `403`, but the frontend's View button is meant to be open to any project member.

## Changes

### 1. `GetMyProjectMilestonesQueryHandler` — add `isOwner`

Add `isOwner: bool` to the response DTO, computed the same way `Project.isLead` is: `objective.HeadUserId == callerId` (or whatever the current Head-tracking field is named on `Objective` per the milestone-hierarchy schema — confirm field name against `Objective.HeadUserId`/`OwnerId` in `docs/superpowers/project_ core/phase1-table-inventory.md` before implementing). No new query, no new join — the handler already loads the Objective rows and already has the caller's id from the auth context.

Response gains one field:
```json
{ "...": "...", "isOwner": true }
```

### 2. `GetObjectiveSubtreeQueryHandler` — loosen permission check

Change the authorization check from "caller must be `{id}`'s current Head" to the membership-fallback pattern already implemented for `GetObjective` and `GetObjectiveTree` (per their own docs: "active membership on this milestone or any of its ancestors — checked in-handler, not via `[RequirePermission]`"). Reuse whatever shared helper/method those two handlers already use for that check rather than re-implementing it a third time — grep for how `GetObjectiveByIdQueryHandler` does it first.

No response shape change — same `parentObjective`/`objective` tree payload as today, just a wider set of callers who can reach `200` instead of `403`.

## Out of scope

- No change to `EditObjective`, `AchieveObjective`, `UnachieveObjective`, or `GetObjective` — their existing Head-only permission checks are correct as-is (Edit/Achieve/Unachieve are mutations that should stay Head-restricted; only the *read* endpoint for the tree view is being opened up).
- No change to `PlatformUser`/session/auth endpoints — deliberately avoided per the frontend team's own recommendation, to keep this change small and scoped to Work Management rather than touching the auth surface.

## Testing

- `GetMyProjectMilestonesQueryHandlerTests`: add a case asserting `isOwner: true` when the caller is the Head, `false` otherwise.
- `GetObjectiveSubtreeQueryHandlerTests`: add a case asserting a non-Head caller with active membership on the milestone (or an ancestor) now gets `200` instead of `403`; keep the existing "no membership at all → `403`" case unchanged.
