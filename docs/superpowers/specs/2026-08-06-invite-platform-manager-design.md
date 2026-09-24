# Invite Platform Manager — Design (v2, supersedes the 2026-08-06 v1 in git history)

## Goal

Sub-project 2 of "Invite Platform Manager & Configure Access" (see the amendment in
`2026-08-03-platform-users-list-design.md` §9 for the delivery-order decision this
spec implements). Lets a platform admin (`platform.accounts.manage`) invite a new
platform manager by email, assigning one or more **existing** platform roles at
invite time, and lets the invited person accept the invite and activate their
account. Replaces the `platform-users-list` "Invite Manager" button's stub
(`onInviteManagerClicked()` → "coming soon" toast).

**Release rule:** invite-sending and invite-acceptance ship together. An invite email
with no way to accept it is a dead end — this spec covers both halves for exactly
that reason.

## Scope

Full-stack: this repo + `platform-administration` (companion spec, same filename,
that repo). Out of scope: role creation, permission configuration, and the "Configure
Access" UI for already-active users — separate future sub-projects (§9 of the
amended PA-USER-01 spec).

## Existing infrastructure this builds on

- `PlatformUser` (`src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformUser.cs`)
  already has `Status` (`StatusPending`/`StatusActive`/`StatusInactive`) and
  `InviteStatus` (`InvitePending`/`InviteAccepted`/`InviteRevoked`/`InviteExpired`)
  constants and a DB column + index for `InviteStatus` — but **nothing in the codebase
  reads or writes either field today** (confirmed by grep). This is dormant,
  originally-intended-but-never-wired infrastructure; this spec is what wires it up.
- `PlatformUserInvite` (`.../Entities/PlatformUserInvite.cs`): `Id`, `Email`,
  `FullName`, `InviteTokenHash`, `InvitedById`, `ExpiresAt`, `AcceptedAt`,
  `RevokedAt`, `CreatedAt`. Gets one new column this spec (see Data model change).
- `PlatformUserRole` (composite PK `user_id`+`role_id`) — platform users already
  support multiple simultaneous roles; `UpdatePlatformUserRolesCommand` takes a role-ID
  list. The invite path uses this table directly (see below) rather than inventing a
  parallel one.
- `TenantOwnerInvitationService`
  (`src/ONEVO.Infrastructure/Services/DevPlatform/Provisioning/TenantOwnerInvitationService.cs`)
  is the pattern for invite creation: generate a 32-byte random token, base64url the
  plaintext (goes in the email, never persisted), SHA-256 hash it for storage,
  duplicate-email guard, one transaction covering the invite row(s) and the outbox
  email message.
- `AcceptInvitationPasswordCommandHandler`
  (`src/ONEVO.Application/Features/Auth/Invite/Commands/AcceptInvitationPassword/`)
  is the pattern for invite acceptance: hash the submitted token, look it up,
  used/revoked/expired checks, set password + activate, mark the invite used, one
  transaction. The platform-side handler is a new command in its own right (different
  entities, different auth scheme) — not a call into this tenant-scoped one.
- `PlatformAccessController` — every action already has
  `[Authorize(Policy = "AdminPolicy")]` + `[RequirePlatformPermission(...)]`; the new
  invite/revoke endpoints follow that shape. The accept endpoint is **not**
  authenticated (the invited person has no session yet) — it belongs on the
  `admin/v1/auth/...` controller instead, alongside `login`/`forgot-password`, which
  are the codebase's existing unauthenticated admin routes.
- Outbox pattern (`IOutboxWriter`, `OutboxProcessor`, `IOutboxMessageHandler`) is the
  only way any email leaves this system.

## Data model change

One new nullable column on the existing `PlatformUserInvite`:

```
PlatformUserId  uuid?  (FK -> platform_users.id, cascade delete)
```

No new join table. Roles are assigned to the pending `PlatformUser` row directly via
the existing `platform_user_roles` table, at invite time — because the `PlatformUser`
row is created at invite time, not at acceptance time (see Backend changes below).

## Backend changes

### Command: `InvitePlatformManagerCommand`

`InvitePlatformManagerCommand(string Email, string FullName, IReadOnlyList<Guid> RoleIds)`

1. Reject if `RoleIds` is empty (`Result.Failure(..., 400)`) — role selection is
   required; no zero-permission invites.
2. Normalize email (trim + lowercase). Reject if a `PlatformUser` with that email
   already exists (`Result.Conflict`) — covers both "already active" and "already
   has a pending invite", since a pending user *is* a `PlatformUser` row under this
   design.
3. Validate every `RoleIds` entry resolves to a real `PlatformRole` (`Result.NotFound`
   on the first miss).
4. Create the `PlatformUser` row: `Status = PlatformUser.StatusPending`,
   `InviteStatus = PlatformUser.InvitePending`, `Email`, `FullName`, `CreatedById`
   (the inviting admin).
5. Insert one `PlatformUserRole` row per `RoleIds` entry, `AssignedById` = inviting
   admin.
6. Generate the invite token exactly like `TenantOwnerInvitationService`
   (`GenerateInviteToken()`).
7. Insert the `PlatformUserInvite` row: hash only (plaintext never persisted),
   `PlatformUserId` set to the row created in step 4, `InvitedById` = inviting admin,
   `ExpiresAt` = now + validity window (reuse `TenantOwnerInvitationService`'s 72-hour
   constant unless product wants something else — no requirement given otherwise).
8. Enqueue the outbox email (see below).
9. `IUnitOfWork.SaveChangesAsync` — everything above commits in one transaction.

### Endpoint

`POST /admin/v1/platform-access/users/invite`
- `[RequirePlatformPermission(PlatformPermissionCatalog.AccountsManage)]` (matches the
  frontend's existing `canInvite` check — no frontend permission-gating changes
  needed).
- Request: `{ email: string, fullName: string, roleIds: string[] }`. `204 No Content`
  on success (matches `UpdateUserRoles`/`UpdateRolePermissions`).

### Revoke endpoint

`POST /admin/v1/platform-access/invites/{inviteId}/revoke`
- Same permission. Sets `RevokedAt` on the invite AND `InviteStatus = InviteRevoked` +
  `Status = StatusInactive` on the linked `PlatformUser` — a revoked invite must not
  leave a stray active-looking pending user behind.

### Email

New outbox type `platform_manager_invite_email`
(`OutboxMessageTypes.PlatformManagerInviteEmail`), handler
`PlatformManagerInviteEmailOutboxHandler` (mirrors
`TenantOwnerInviteEmailOutboxHandler`). Link built the same way
`EmailTemplateRenderer.RenderAdminPasswordReset` builds the admin reset link (uses
`EmailOptions.AdminConsoleBaseUrl`):

```
{AdminConsoleBaseUrl}/auth/accept-invite?token={plaintextToken}
```

### Command: `AcceptPlatformManagerInviteCommand`

`AcceptPlatformManagerInviteCommand(string RawToken, string Password)`

1. Hash `RawToken`, look up the `PlatformUserInvite` by hash. Not found →
   `Result.NotFound("Invitation not found.")`.
2. Usability checks, same shape as `AcceptInvitationPasswordCommandHandler.CheckInvitationUsable`:
   already accepted (`AcceptedAt is not null`) / revoked (`RevokedAt is not null`) /
   expired (`ExpiresAt <= now`) → `Result.Failure(..., 400)` with a distinct message
   each, matching the tenant-side pattern (this is a public error path, not a
   password-reset-style single-generic-message path — the existing tenant invite
   flow already distinguishes these, so this one does too for consistency within the
   same feature family).
3. Load the linked `PlatformUser` (`invite.PlatformUserId`) — `Result.Failure(..., 500)`
   if somehow missing (would indicate stored data corruption, not a user error).
4. Create the `PlatformUserCredential`: `CredentialType = PasswordType`,
   `PasswordHash` via the platform's existing password hasher (whatever
   `RequestAdminPasswordReset`/`ResetAdminPassword` already use), `PasswordAlgorithm
   = BCryptAlgorithm`, `PasswordChangedAt = now`, `MustChangePassword = false`.
5. Update the `PlatformUser` row: `Status = StatusActive`, `InviteStatus =
   InviteAccepted`.
6. Update the invite: `AcceptedAt = now`.
7. `IUnitOfWork.SaveChangesAsync`.
8. Return success (no auto-login — the frontend redirects to `/auth/login` after
   showing an "access activated" confirmation; keeps this command from having to deal
   with `AdminScheme` session/cookie issuance, which the existing admin login command
   already owns).

### Endpoint

`POST /admin/v1/auth/accept-invite` — unauthenticated (no `[Authorize]`), alongside
`login`/`forgot-password`/`reset-password` on the existing admin auth controller.
Request: `{ token: string, password: string }`. `204 No Content` on success, `400`
with the specific usability-check message on failure, `404` if the token doesn't
resolve to any invite at all.

**CSRF note:** this is a new unauthenticated `POST` under `/admin/v1/...` — it must be
added to `CsrfProtectionMiddleware`'s `ExemptPaths` set alongside
`/admin/v1/auth/forgot-password`/`/admin/v1/auth/reset-password` (same reasoning:
unauthenticated flows can't meaningfully be CSRF-attacked, and a stale
`admin_session` cookie in the requester's browser must not block this route — this is
exactly the bug fixed for the two neighboring routes on 2026-08-06, in
`fix/module-catalog-seeder-startup`; this endpoint must not repeat that gap).

**Rate limiting note:** for the same reason `admin/v1/auth/forgot-password` and
`admin/v1/auth/reset-password` have rules in `AuthRateLimitingMiddleware`, this new
public, unauthenticated, token-guessable-in-principle endpoint needs its own rule
(IP-scoped and token-scoped, mirroring the reset-password shape).

### List endpoint — no structural change needed

`ListPlatformUsersQueryHandler` already queries `PlatformUser` rows; because invite
creates a real `PlatformUser` row immediately (step 4 above), pending invites appear
in the existing list with no query-merging logic required. The only change:
`PlatformUserResponse`'s status representation moves from `IsActive: bool` to
`Status: string` (`"active" | "inactive" | "pending"`, sourced directly from
`PlatformUser.Status`) so the frontend can render a third, distinct "pending" badge
instead of collapsing it into "inactive".

## Testing

- `InvitePlatformManagerCommandHandlerTests`: empty-roles rejection, unknown-role-id
  rejection, duplicate-email conflict (both against an active user and an existing
  pending invite — same check, since both are `PlatformUser` rows), successful invite
  creates the user + role rows + invite row + outbox message in one transaction, raw
  token never appears in the command result or any log.
- `AcceptPlatformManagerInviteCommandHandlerTests`: not-found token, already-accepted,
  revoked, expired, successful accept activates the user and sets the credential,
  double-accept after success fails with "already accepted".
- Architecture test: neither handler calls `IEmailService` directly (outbox-only,
  matching the existing `PasswordResetHandlers_NeverCallSendPasswordResetAsyncDirectly`
  guard) and `/admin/v1/auth/accept-invite` is present in
  `CsrfProtectionMiddleware.ExemptPaths` and has an `AuthRateLimitingMiddleware` rule
  (extends the existing `AuthRateLimitingMiddleware_StillCoversForgotResetAndForceChangePassword`-style
  test).
- `ListPlatformUsersQueryHandlerTests`: a pending invite's `PlatformUser` row appears
  with `Status: "pending"`.

## Out of scope

- Role creation, permission configuration ("Configure Access" — separate sub-project).
- Resending an invite email / regenerating an expired invite's token.
- Auto-login after accept (frontend redirects to login instead — see step 8 above).
