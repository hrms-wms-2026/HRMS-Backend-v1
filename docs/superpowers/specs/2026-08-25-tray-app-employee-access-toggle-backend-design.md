# Tray App Employee Access Toggle — Backend Design

## Problem

Any authenticated employee can currently generate a desktop-agent (OneXso Workspace TrayApp)
activation code via `POST /api/v1/monitoring/activation/generate` and redeem it via
`POST /api/v1/monitoring/activation/exchange` — there is no way for an admin/HR user to block a
specific employee from connecting the tray agent. This is Phase A of a two-phase request; Phase
B (replacing the copy-paste activation code with a device-code/browser-confirm flow closer to
the architecture doc's §9 target design) is separate, larger, and not part of this spec.

## Scope

- A new per-employee boolean flag, `TrayAppAccessEnabled`, defaulting to `true` (opt-out model —
  every existing and new employee is allowed by default; an admin turns it off for a specific
  employee to block them).
- An admin/HR-facing endpoint to toggle the flag, gated on the existing `employees:write`
  permission (no new permission code — this is an ordinary employee-record mutation, matching
  `ChangePosition`'s gating).
- Enforcement at both `generate` and `exchange` — not just `generate`. `exchange` is
  `[AllowAnonymous]` and resolves the employee from the code itself, so a code minted before the
  flag was turned off must still be rejected at exchange time; gating only `generate` would leave
  that hole open.

## Explicitly out of scope

- Revoking an already-connected device (existing `TrayDeviceRegistration` rows). This flag only
  blocks *new* connections (new `generate`/`exchange` calls). Revoking a live device is a
  separate, not-yet-built admin action.
- `RefreshTrayToken` is not gated by this flag. Refresh keeps an *already-connected* device's
  session alive; that's a different concern from blocking new connections, matching the
  "don't revoke already-connected devices" scope line above. A future admin "revoke device"
  action would be the correct place to kill a live device's ability to refresh.
- Any change to the `generate`/`exchange` protocol shape, rate limiting, or code lifetime.

## Data model

New column on `Employee` (`src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs`, tenant-owned
via `BaseEntity`): `TrayAppAccessEnabled` (`bool`, `NOT NULL DEFAULT true`). EF configuration
(`EmployeeConfiguration.cs`) sets `IsRequired()` and `HasDefaultValue(true)` so the default is
enforced at the database level too, not just in C#.

## API

`POST /api/v1/employees/{id}/tray-app-access`, `[RequirePermission("employees:write")]`, body
`{ "enabled": bool }`, returns `204 No Content` on success — mirrors
`POST /employees/{id}/revoke-invitation`'s shape exactly (`EmployeesController.cs:129-138`).

`GET /api/v1/employees/{id}/detail` — `EmployeeDetailResponse` gains a new top-level field
`TrayAppAccessEnabled: bool` (serializes as `trayAppAccessEnabled`, the default camelCase System.Text.Json
policy already used by this DTO family — not the snake_case override used only by the Auth/
TrayActivation DTOs) so the admin UI can show the current state.

## Enforcement points

**`GenerateActivationCodeCommandHandler`** (`Features/Monitoring/TrayActivation/Commands/
GenerateActivationCode/`): runs under `TenantPolicy`, already tenant-scoped, so no context
switch is needed. Before the rate-limit check, look up the caller's employee profile via the
already-injected `ITrayActivationRepository.FindEmployeeProfileAsync(userId, tenantId, ct)` (this
method already exists, used by Exchange/Refresh for display purposes — see below). If a profile
exists and `TrayAppAccessEnabled` is `false`, return
`Result<ActivationCodeResponseDto>.Failure("Your account is not permitted to connect a desktop device. Contact your admin.", 403)`.
If no profile exists yet (auth `User` not yet linked to an `Employee` row), allow — the gate only
applies once an employee record exists, matching this codebase's existing "never block on HR
onboarding not being finished" philosophy (see the doc comment on
`ExchangeActivationCodeCommandHandler.ResolveEmployeeIdentityAsync`).

**`ExchangeActivationCodeCommandHandler`**: this handler currently resolves the employee's
display identity (`ResolveEmployeeIdentityAsync`) *after* the code is marked used and the device
is already registered — deliberately, because at that point undoing those side effects would be
worse than serving null display fields. The access check is different: it must run *before* the
code is consumed, or a blocked employee could still burn a valid code into a live device
registration. This requires moving the tenant-context switch (currently inside
`ResolveEmployeeIdentityAsync`) earlier, right after the code is found:

1. Find the activation code by hash (existing, unchanged). Not found → `401` (existing).
2. **New:** look up the tenant (`_tenantRepository.GetByIdAsync`). Not found → `401` "Invalid or
   expired activation code." (same message as step 1 — don't leak whether the code itself or the
   tenant was the problem). This tightens today's behavior slightly: currently a missing tenant
   still lets the exchange succeed with null identity fields, because by the time that check ran
   the code was already consumed. Moving the check earlier means a missing tenant now fails
   *before* anything is created, which is strictly safer and doesn't leave an orphaned device
   registration for a nonexistent tenant.
3. **New:** switch tenant context (`_tenantSwitcher.SwitchToTenantAsync`, same call
   `ResolveEmployeeIdentityAsync` used to make), then call
   `_repository.FindEmployeeProfileAsync(activationCode.UserId, activationCode.TenantId, ct)`.
   If a profile exists and `TrayAppAccessEnabled` is `false`, return
   `Result<TrayAuthResponseDto>.Failure("Your account is not permitted to connect a desktop device. Contact your admin.", 403)`
   — before marking the code used, before registering the device.
4. Continue exactly as today: mark code used, register device, issue refresh token, save, issue
   access token.
5. Build the response identity fields from the `profile` already fetched in step 3 (falling back
   to the `IUserRepository` lookup exactly as `ResolveEmployeeIdentityAsync` does today) instead
   of re-fetching — `ResolveEmployeeIdentityAsync` is removed and its logic inlined/reused, since
   the tenant switch and profile fetch now happen once, earlier.

`TrayEmployeeProfile` (`ITrayActivationRepository.cs:29`) gains a new field,
**appended last** — `TrayAppAccessEnabled` — so `RefreshTrayTokenCommandHandler`, which also
consumes this record for display purposes only (not gated by this flag, see Scope), keeps
compiling unchanged against its existing named-property access.

## Error handling

Both new denial paths return `403` with the same user-facing message:
`"Your account is not permitted to connect a desktop device. Contact your admin."` — this maps
to `ErrorHandlerService`'s existing `403: 'You do not have access to perform this action.'`
banner on the frontend (see the frontend spec) unless the frontend chooses to surface the
server's own `detail` text instead (the frontend spec decides this).

## Testing

- **Unit** (`tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/`):
  `SetEmployeeTrayAppAccessCommandHandlerTests.cs`, mirroring
  `RevokeEmployeeInvitationCommandHandlerTests.cs`'s Moq-based style — covers toggling on/off and
  "employee not found."
- **Integration** (existing `tests/ONEVO.Tests.Integration/Monitoring/TrayActivation/
  TrayActivationIntegrationTests.cs`, which already covers `Generate`/`Exchange`/`Refresh`
  end-to-end against a real Testcontainers PostgreSQL): add cases for
  `TrayAppAccessEnabled = false` returning `403` on both `Generate` and `Exchange`, and a case
  confirming `Exchange` still succeeds when no `Employee` row exists yet for the user.
