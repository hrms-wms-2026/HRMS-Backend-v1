# Accept Invitation With Google

**POST** `/api/v1/auth/invitations/{token}/accept-google`

**Auth:** `AllowAnonymous`. **CSRF:** exempt.

## Description

Completes an invitation by linking a Google identity and accepting the required legal documents in the same call, then behaves like a successful login for this new user.

## Request

Path parameter: `token`.

```json
{
  "google_id_token": "eyJhbGciOi...",
  "acceptances": [
    { "document_type": "terms", "version": "2026-01-01", "decision": "accepted" }
  ]
}
```

## Response

Same branches as Login's post-continuation result. See `_Shared - Session Result Response.md`.

## Errors

| Status | Cause |
|---|---|
| `400` | Token invalid/expired, Google token invalid, email domain not allowed, or required acceptance missing |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthInvitationController.cs` (`AcceptInvitationGoogle`)
`src/ONEVO.Api/Contracts/Auth/AcceptInvitationGoogleRequest.cs`
Handler: `AcceptInvitationGoogleCommandHandler` (`src/ONEVO.Application/Features/Auth/Invite/Commands/AcceptInvitationGoogle/`)
