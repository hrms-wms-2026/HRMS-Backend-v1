# Force Change Password

**POST** `/api/v1/auth/force-change-password`

**Auth:** `AllowAnonymous`. **CSRF:** exempt.

## Description

Completes the required-password-change path (`must_change_password: true` branch from Login). **Known gap** (see `docs/superpowers/workflow/authentication.md` §12): the continue-URL for this step is built from the host where login started, so a base-domain-triggered forced password change can produce an unreachable `continue_url` — not yet fixed.

## Request

```json
{
  "email": "user@example.com",
  "currentPassword": "TempPass123!",
  "newPassword": "NewPassword123!"
}
```

## Response

Continues the same finalization logic as Login — usually `200 OK`/`202` with `AuthSessionViewModel`. See `_Shared - Session Result Response.md`.

## Errors

| Status | Cause |
|---|---|
| `400` | Current password wrong, or new password fails policy |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthPasswordController.cs` (`ForceChangePassword`)
`src/ONEVO.Api/Contracts/Auth/ForcePasswordChangeRequest.cs`
Handler: `ForcePasswordChangeCommandHandler` (`src/ONEVO.Application/Features/Auth/Login/Commands/ForcePasswordChange/`)
