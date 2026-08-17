# Login

**POST** `/api/v1/auth/login`

**Auth:** `AllowAnonymous`. **CSRF:** exempt. **Host:** base/system host only — tenant-host requests get `400`.

## Description

Base-domain credential-first email + password login. Checks the password against **every** tenant/user candidate for that email (fixed-work-factor, timing-safe — always exactly 8 BCrypt comparisons) so response timing never reveals how many tenants share that email.

## Request

```json
{
  "email": "user@example.com",
  "password": "CurrentPass123!"
}
```

## Response

- Zero or overflow matches → `401` (generic, enumeration-safe).
- **Multiple matches** → `202`, `WorkspaceSelectionRequiredResponse` (see shared doc) — call **Select Workspace** next.
- **Exactly one match** → continues through `must_change_password` → MFA → legal-acceptance gates → finalize. Returns one of the `AuthSessionViewModel` branches, or the tenant-session-exchange branch if login started on the base host (see `_Shared - Session Result Response.md`).

## Errors

| Status | Cause |
|---|---|
| `400` | Request made on a tenant host (not supported) |
| `401` | No matching candidate / wrong password |
| `429` | Rate limited |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthLoginController.cs` (`Login`)
`src/ONEVO.Api/Contracts/Auth/LoginRequest.cs`
Handler: `BaseLoginCommandHandler` (`src/ONEVO.Application/Features/Auth/Login/Commands/BaseLogin/`)
