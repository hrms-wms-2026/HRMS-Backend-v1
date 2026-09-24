# Platform Users List — Design Spec

**Screen ID:** PA-USER-01 · **Journey:** Invite Platform Manager & Configure Access (sub-project 1 of 4)
**Status:** Approved for implementation planning

## 1. Purpose

Give the Platform Super Admin (and any platform user holding `platform.accounts.read`) a single
screen listing every platform user (Developer Platform staff — not tenant employees), with
search, role/status filtering, and pagination. This is the entry point for the larger "Invite
Platform Manager & Configure Access" journey; the invite flow itself, the user-profile drawer,
and role creation are separate sub-projects (see §7 Out of Scope).

## 2. Scope decisions (from brainstorming session)

| Decision | Choice |
|---|---|
| Sidebar nav | `Dashboard, Users, Roles & Permissions, Audit Logs, Settings` (placeholder routes for the last three — not built this round) |
| Row click (profile drawer) | No-op this round; drawer is a separate future sub-project |
| Role column, multi-role users | Show first-assigned role only, not a comma list |
| Pagination | Client-side (Option A): backend returns the full list in one call; frontend paginates 20/page in-memory. Revisit server-side pagination only if platform-user counts grow large — they're expected to stay in the tens, not thousands |
| "Invite Manager" button | Visible, role-gated; clicking shows a "Coming soon" message (its real flow is sub-project 2) |

## 3. Architecture

```
Angular (feature/platform-users)          ASP.NET Core (existing controller, extended)
┌─────────────────────────────┐           ┌──────────────────────────────────────┐
│ PlatformUsersList component │  GET      │ PlatformAccessController              │
│  - search (debounced)       │ ────────► │  GET /admin/v1/platform-access/users  │
│  - role filter              │           │  [RequirePlatformPermission            │
│  - status filter            │           │   (AccountsRead)]                     │
│  - table + status chips     │◄──────────│  → ListPlatformUsersQuery              │
│  - client-side pagination   │  full     │    → PlatformUserResponse[]            │
│  - "Invite Manager" (stub)  │  list     │      (Email, Name, Role, Status, ...)  │
└─────────────────────────────┘           └──────────────────────────────────────┘
```

No new backend route. The existing `GET /admin/v1/platform-access/users` endpoint is extended to
include role information; everything else (search, filter, pagination) happens client-side.

## 4. Backend changes

The endpoint, its permission gate (`platform.accounts.read`), and `ListPlatformUsersQuery` /
`ListPlatformUsersQueryHandler` already exist and need no route or authorization changes. The
gap: `PlatformUserResponse` has no role field, and today's mapper hard-codes `LastName: null`
and stuffs the full name into a field literally called `FirstName` — both are pre-existing
naming/shape issues in the path this work touches, so they're fixed as part of this change
rather than compounded:

- `PlatformUserResponse`: replace `FirstName`/`LastName` with a single `FullName: string`, and
  add `Role: string` (empty string when the user has no role assigned).
- `PlatformAccessMapper.Map(PlatformUser)`: update to the new shape. Role resolution needs the
  user's first-assigned role name — sourced without an N+1 query per user (batch-load roles for
  all listed users, or extend the repository query to project role names alongside users).
- `ListPlatformUsersQueryHandler` / `IPlatformUserRepository.ListUsersAsync`: extend to supply
  role data in the same round trip.
- `GetPlatformUserDetailQuery`/`MapDetail` already receives full role data separately — leave
  that path alone; only the list path changes.

No migration, no new permission, no new controller route.

## 5. Frontend changes

New feature module `src/app/modules/platform-users/feature/platform-users-list/`, mirroring the
existing `dashboard` and `auth` module conventions (standalone component, signals for state,
`ReactiveFormsModule` where forms are involved).

- **Sidebar** (`layouts/main-layout/sidebar/sidebar.html`): add `Users`, `Roles & Permissions`,
  `Audit Logs`, `Settings` links alongside the existing `Dashboard` link. Only `Users` gets a
  `routerLink` to a real route (`/users`) and `routerLinkActive` highlighting, matching the
  existing `Dashboard` link's pattern. The other three render as static, visually muted list
  items (no `routerLink`, no click handler) — present for the visual hierarchy the spec shows,
  not yet functional. No placeholder routes or "not built yet" pages are created for them.
- **Route**: `/users`, guarded by the existing `permissionGuard` with
  `data: { permission: 'platform.accounts.read' }` (mirrors how other guarded routes are wired).
- **Service**: extend `AuthService`-adjacent or add a small `PlatformUsersService` with a
  `list(): Observable<PlatformUser[]>` call to the existing endpoint. Response model updated to
  match the new backend shape (`fullName`, `role` instead of `first_name`/`last_name`).
- **Component** (`PlatformUsersList`):
  - Fetches the full list once on load (no re-fetch on filter/search/page change).
  - Search input, debounced (spec: live filtering by name/email).
  - Role filter dropdown: populated from the distinct roles present in the fetched list (no
    separate roles API call needed for this screen).
  - Status filter dropdown: Active / Inactive / All.
  - Table: avatar-initials, name, email, role, status chip (green=Active, red=Inactive).
  - Pagination: 20 rows/page, computed from the filtered result set client-side.
  - "Invite Manager" button: gated on `platform.accounts.manage` (matches the backend's split
    between `AccountsRead` for viewing and `AccountsManage` for inviting); click shows a
    "Coming soon" toast/snackbar — no navigation, no API call.
  - Row click: no handler this round.

## 6. Error handling / empty states

Per the spec's screen-messages table:

| Condition | Message |
|---|---|
| Search/filter yields no rows | "No users match your search" |
| List is empty (no users at all) | "No platform users yet" (the spec's "Invite your first manager" wording is deferred — inviting isn't wired up yet this round) |
| Route guard denies access | Existing `/access-denied` route (no new page needed) |
| API call fails | Reuse the existing HTTP-error-to-message pattern already used in `login.ts`/`mfa-setup.ts` (status-code switch → user-facing string) |

## 7. Out of scope (tracked as future sub-projects)

1. **Invite Manager flow** (PA-USER-02 → PA-ROLE-01 → PA-ROLE-02 → PA-PERM-01 → PA-PERM-02 →
   PA-USER-03 → PA-USER-04 + invite email) — needs new backend command(s), a `RoleId` (and
   likely a permission-snapshot) added to the existing but unused `platform_user_invites`
   table/entity, and ~6 new frontend screens.
2. **User profile drawer** (row click target on this screen).
3. **Create New Role** (PA-ROLE-02) — no backend command exists yet.
4. **Invited-manager onboarding** (PA-USER-05 Account Setup, PA-DASH-02 Access Activated) — a new
   unauthenticated, token-based entry point mirroring the existing tenant invite-acceptance
   pattern (`AcceptInvitationPasswordCommandHandler` / `AcceptInvitationGoogleCommandHandler`).
5. Server-side search/filter/pagination (only if/when the client-side approach stops scaling).

## 8. Testing

- **Backend**: unit tests for `PlatformAccessMapper.Map` (new shape) and
  `ListPlatformUsersQueryHandler` (role resolution, empty-role case). Existing tests referencing
  `FirstName`/`LastName` on `PlatformUserResponse` need updating to `FullName`.
- **Frontend**: component test for `PlatformUsersList` (mirrors `login.spec.ts` conventions) —
  search filtering, role/status filtering, pagination math, permission-gated "Invite Manager"
  button, "Coming soon" stub behavior, empty-state messages.

## 9. Scope Amendment (2026-08-06) — sub-project 2 delivery order

**Status:** Approved. Does not change anything in §1–§8 above; PA-USER-01 is unaffected
and already shipped. This amendment only revises how sub-project 2 ("Invite Manager
flow", §7 item 1) gets delivered.

**Decision:** The original seven-screen journey
(`PA-USER-02 → PA-ROLE-01 → PA-ROLE-02 → PA-PERM-01 → PA-PERM-02 → PA-USER-03 → PA-USER-04`)
remains the target-state UX and is **not discarded**. It is deferred to a future
enhancement. Initial delivery implements invitation using **existing platform roles
only** — no inline role creation, no inline permission configuration. Role creation
and permission configuration are delivered separately, as their own
platform-access-management capabilities (a later "Platform Roles & Permissions"
project), independent of the invite flow.

**Why:** the seven-screen journey bundles three separate domain concerns into one UI
flow — inviting a person (identity lifecycle), creating a role (authorization
structure), and configuring permissions (access policy). None of role-creation's or
permission-configuration's backend commands exist yet (`CreatePlatformRoleCommand`
does not exist; only `UpdatePlatformRolePermissionsCommand`, which edits an *existing*
role, is built). Requiring all three to land in one sub-project inflates it into a
much larger, harder-to-review deliverable and blocks the invite flow on unrelated
role-management work. Splitting them keeps each capability independently testable,
reviewable, and reusable — including by the eventual seven-screen wizard, which (once
built) will orchestrate these same APIs rather than invent new ones.

**Data model:** `platform_user_invites` does **not** get a single `RoleId` column
(reversing this spec's §7 item 1 parenthetical, which was a preliminary note, not a
locked decision — the table was confirmed still fully unused with no role linkage of
any kind at the time of this amendment). Platform users already support multiple
simultaneous roles (`platform_user_roles`, composite PK `user_id`+`role_id`;
`UpdatePlatformUserRolesCommand` takes a role-ID list). Invitations follow the same
multi-role shape via a new join table, `platform_user_invite_roles`
(`invite_id`+`role_id` composite PK), copied into `platform_user_roles` at acceptance
time.

**Release rule:** sending invitations must not ship before invitation *acceptance*
does. An invite email with no way to accept it is a dead-end. The "Invite Manager"
button stays disabled/"coming soon" (its current state) until both the invite-sending
and invite-acceptance paths are live in the same release.

**Revised delivery breakdown for sub-project 2:**
1. Invitation domain + database: `platform_user_invite_roles` migration,
   `CreatePlatformUserInviteCommand` (validation, token generation/hashing, outbox
   email), unit + integration tests.
2. Admin invite modal: replaces the "coming soon" toast with a real form (email, full
   name, multi-select from *existing* roles only).
3. Invitation acceptance: `AcceptPlatformUserInvitationCommand` (token validation,
   password setup, role copy from the invite's roles into `platform_user_roles`,
   expired/revoked/already-accepted handling) + two minimal screens (set password,
   access activated).

Steps 2 and 3 ship in the same release (or step 2 stays behind a flag until step 3 is
ready) — see the release rule above.

Full design detail for this revised scope: `2026-08-06-invite-platform-manager-design.md`
(this repo) and the companion frontend spec of the same name in `platform-administration`.
