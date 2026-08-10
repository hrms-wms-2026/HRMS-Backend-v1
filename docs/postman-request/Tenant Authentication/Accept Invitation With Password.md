# Accept Invitation With Password

**POST** `/api/v1/auth/invitations/{token}/accept-password`

**Auth:** `AllowAnonymous`. **CSRF:** exempt.

## Description

Completes an invitation by setting a password and accepting the required legal documents in the same call, then behaves like a successful login for this new user.

## Request

Path parameter: `token`.

```json
{
  "password": "NewPassword123!",
  "confirm_password": "NewPassword123!",
  "acceptances": [
    { "document_type": "terms", "version": "2026-01-01", "decision": "accepted" }
  ]
}
```

## Response

Same branches as Login's post-continuation result (usually `200 OK`/`202` with `AuthSessionViewModel`). See `_Shared - Session Result Response.md`.

## Errors

| Status | Cause |
|---|---|
| `400` | Token invalid/expired, passwords don't match, password policy failure, or required acceptance missing |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthInvitationController.cs` (`AcceptInvitationPassword`)
`src/ONEVO.Api/Contracts/Auth/AcceptInvitationPasswordRequest.cs`
Handler: `AcceptInvitationPasswordCommandHandler` (`src/ONEVO.Application/Features/Auth/Invite/Commands/AcceptInvitationPassword/`)
