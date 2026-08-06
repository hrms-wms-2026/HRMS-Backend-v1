# Invite Platform Manager — Design

## Goal

First of three sub-projects under "Invite Platform Manager & Configure Access". Lets
an existing platform admin (with `platform.accounts.manage`) invite a new platform
manager by email, assigning at least one platform role at invite time. Replaces the
`platform-users-list` "Invite Manager" button's current stub (`onInviteManagerClicked()`
just shows an "Invite Manager is coming soon." toast).

## Scope

Full-stack: `HRMS-Backend-v1` (this repo) + `platform-administration` (frontend repo,
own spec at `docs/superpowers/specs/2026-08-06-invite-platform-manager-design.md`
there). Explicitly **out of scope**: the accept-invite flow (token consumption,
password setup, account activation) and the "Configure Access" role-editing UI — both
are separate sub-projects, brainstormed and specced independently.

## Existing infrastructure this builds on

- `PlatformUserInvite` entity already exists
  (`src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformUserInvite.cs`):
  `Id`, `Email`, `FullName`, `InviteTokenHash`, `InvitedById`, `ExpiresAt`,
  `AcceptedAt`, `RevokedAt`, `CreatedAt`. No `RoleId` — this spec adds a join table for
  multi-role support (see below).
- `TenantOwnerInvitationService`
  (`src/ONEVO.Infrastructure/Services/DevPlatform/Provisioning/TenantOwnerInvitationService.cs`)
  is the pattern to follow: generate a 32-byte random token, base64url-encode it as the
  plaintext (sent in the email, never persisted), SHA-256 hash it for storage,
  `InviteValidityHours` expiry, duplicate-email guard against both existing active
  invites and existing accounts, one `IUnitOfWork.SaveChangesAsync` covering the invite
  row(s) and the outbox email message together.
- `PlatformAccessController`
  (`src/ONEVO.Api/Controllers/Admin/DevPlatform/PlatformAccess/PlatformAccessController.cs`)
  already has `[Authorize(Policy = "AdminPolicy")]` + `[RequirePlatformPermission(...)]`
  on every action — the new endpoint follows the same shape.
- `PlatformUserRole` (composite PK `user_id`+`role_id`) proves platform users already
  support multiple simultaneous roles — `UpdatePlatformUserRolesCommand` takes
  `IReadOnlyList<Guid> RoleIds`. The invite path mirrors this: multi-role, not
  single-role.
- Outbox pattern (`IOutboxWriter`, `OutboxProcessor`, `IOutboxMessageHandler`) is the
  only way any email leaves this system — no direct/synchronous sends anywhere in the
  codebase, and this feature doesn't introduce an exception.

## Data model change

New join table `platform_user_invite_roles` (mirrors `PlatformUserRole`'s shape,
scoped to invites instead of users):

```
invite_id  uuid  (FK -> platform_user_invites.id, cascade delete)
role_id    uuid  (FK -> platform_roles.id, restrict delete)
```

Composite PK `(invite_id, role_id)`. A migration adds this table; `PlatformUserInvite`
itself is unchanged (no `RoleId` column added directly, to keep the entity a pure
1:many relationship like `PlatformUserRole` rather than reintroducing a single-role
shape).

## Backend changes

### Command: `InvitePlatformManagerCommand`

`InvitePlatformManagerCommand(string Email, string FullName, IReadOnlyList<Guid> RoleIds)`

Handler responsibilities, in order:
1. Reject if `RoleIds` is empty (`Result.Failure(..., 400)`) — role selection is
   required at invite time (design decision: no zero-permission invites).
2. Normalize email (trim + lowercase), reject if an active (non-expired,
   non-revoked, non-accepted) invite already exists for that email
   (`Result.Conflict`).
3. Reject if a `PlatformUser` with that email already exists (`Result.Conflict`).
4. Generate the invite token exactly like `TenantOwnerInvitationService`
   (`GenerateInviteToken()` — 32 random bytes, base64url plaintext, SHA-256 hex hash).
5. Insert the `PlatformUserInvite` row (hash only — the plaintext never touches the
   database) and one `platform_user_invite_roles` row per selected role.
6. Enqueue the outbox email (see below).
7. `IUnitOfWork.SaveChangesAsync` — invite rows + outbox message commit in one
   transaction, matching every other invite flow in this codebase.

### Endpoint

`POST /admin/v1/platform-access/users/invite`
- `[RequirePlatformPermission(PlatformPermissionCatalog.AccountsManage)]` — the same
  permission the frontend's `canInvite` computed signal already checks
  (`platform.accounts.manage`), so no frontend permission-gating changes are needed.
- Request body: `{ email: string, fullName: string, roleIds: string[] }`.
- `204 No Content` on success (matching `UpdateUserRoles`/`UpdateRolePermissions`).

### Revoke endpoint

`POST /admin/v1/platform-access/invites/{inviteId}/revoke`
- Same `AccountsManage` permission.
- Sets `RevokedAt` on the invite; no email sent for revocation (out of scope — not
  requested).

### Email

New outbox message type `platform_manager_invite_email`
(`OutboxMessageTypes.PlatformManagerInviteEmail`), new handler
`PlatformManagerInviteEmailOutboxHandler` (mirrors
`TenantOwnerInviteEmailOutboxHandler`'s shape: decrypt payload, call
`IEmailService.Send...Async`). Reset-link-style URL, built the same way
`EmailTemplateRenderer.RenderAdminPasswordReset` builds the admin reset link (using
`EmailOptions.AdminConsoleBaseUrl`, currently `https://admin.localhost:4200` in dev):

```
{AdminConsoleBaseUrl}/auth/accept-invite?token={plaintextToken}
```

`/auth/accept-invite` is a route **placeholder** for this sub-project — it's
registered (empty/stub component, or simply not yet linked from anywhere else) so the
email link is well-formed, but the actual accept-invite screen and its backend
token-consumption endpoint are built in the next sub-project, not this one.

### List endpoint change

`GET /admin/v1/platform-access/users` currently returns only real `PlatformUser`
rows via `PlatformUserResponse(Id, Email, FullName, Role, IsActive, CreatedAt,
LastLoginAt)`. This spec changes it to also include pending (non-expired,
non-revoked, non-accepted) invites in the same list, and changes the status
representation:

```
PlatformUserResponse(
    Id,            // PlatformUser.Id for real users, PlatformUserInvite.Id for pending
    Email,
    FullName,
    Role,          // comma-joined role names for both users and invites
    Status,        // "active" | "inactive" | "pending"  (replaces bool IsActive)
    CreatedAt,
    LastLoginAt)   // null for pending invites
```

`ListPlatformUsersQueryHandler` merges `_userRepository.ListUsersAsync(...)` with a
new `IPlatformAccessReadRepository` (or a new invite-specific repository) query for
active pending invites, sorted together by `CreatedAt`.

## Testing

- `InvitePlatformManagerCommandHandlerTests`: empty-roles rejection, duplicate-active-invite
  conflict, duplicate-existing-user conflict, successful invite creates the invite row +
  join rows + outbox message in one transaction, token is never logged/returned in the
  command result.
- Architecture test extension (or new test) asserting `InvitePlatformManagerCommandHandler`
  never calls `IEmailService` directly — delivery goes through the outbox, matching the
  existing `PasswordResetHandlers_NeverCallSendPasswordResetAsyncDirectly`-style guard.
- `ListPlatformUsersQueryHandlerTests`: pending invites appear with `Status: "pending"`
  and `LastLoginAt: null`; revoked/expired/accepted invites do not appear.

## Out of scope (explicitly deferred to other sub-projects)

- `/auth/accept-invite` screen and its token-validation/account-activation endpoint.
- Role editing for already-active platform users (`UpdatePlatformUserRolesCommand`
  already exists and is untouched by this spec).
- Resending an invite email / regenerating an expired invite's token.
