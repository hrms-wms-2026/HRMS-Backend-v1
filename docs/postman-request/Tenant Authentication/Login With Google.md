# Login With Google

**POST** `/api/v1/auth/login/google`

**Auth:** `AllowAnonymous`. **CSRF:** exempt. **Host:** base/system host only — tenant-host requests get `400`.

## Description

Base-domain Google Sign-In login: verifies the Google ID token first, then resolves eligible tenant workspace(s) for that Google identity — same continuation branches as password login.

## Request

```json
{
  "google_id_token": "eyJhbGciOi..."
}
```

## Response

Same branches as **Login**: workspace-selection (`202`, `WorkspaceSelectionRequiredResponse`) if multiple tenants match, otherwise the usual `AuthSessionViewModel`/tenant-session-exchange branches. See `_Shared - Session Result Response.md`.

## Errors

| Status | Cause |
|---|---|
| `400` | Request made on a tenant host, or Google ID token invalid |
| `401` | No matching tenant/user for that Google identity |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthLoginController.cs` (`LoginWithGoogle`)
`src/ONEVO.Api/Contracts/Auth/BaseGoogleLoginRequest.cs`
Handler: `BaseGoogleLoginCommandHandler` (`src/ONEVO.Application/Features/Auth/Login/Commands/BaseGoogleLogin/`)
