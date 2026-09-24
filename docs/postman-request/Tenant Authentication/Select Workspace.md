# Select Workspace

**POST** `/api/v1/auth/login/select-workspace`

**Auth:** `AllowAnonymous`. **CSRF:** exempt.

## Description

Completes base-domain login after **Login** (or **Login With Google**) returned a workspace-selection challenge because the email matched more than one tenant. The challenge is opaque, single-use, and expires after 5 minutes.

## Request

```json
{
  "login_challenge": "opaque string returned by Login's workspace-selection response",
  "workspace": "acme"
}
```

## Response

Continues into `LoginContinuationService` exactly like a single-match Login — returns one of the `AuthSessionViewModel` branches or the tenant-session-exchange branch. See `_Shared - Session Result Response.md`.

## Errors

| Status | Cause |
|---|---|
| `401` | Challenge expired, invalid, already used, or `workspace` not one of the offered options |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthLoginController.cs` (`SelectWorkspace`)
`src/ONEVO.Api/Contracts/Auth/SelectWorkspaceRequest.cs`
Handler: `SelectWorkspaceCommandHandler` (`src/ONEVO.Application/Features/Auth/Login/Commands/SelectWorkspace/`)
