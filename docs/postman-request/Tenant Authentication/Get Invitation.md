# Get Invitation

**GET** `/api/v1/auth/invitations/{token}`

**Auth:** `AllowAnonymous`. **CSRF:** exempt (whole invitations prefix).

## Description

Public invitation preview — the landing page after clicking an invite email link uses this to show who invited the user, to which tenant/role, and which completion methods (password/Google) are allowed, before the user commits to accepting.

## Request

Path parameter only: `token` (raw invitation token from the email link). No body.

## Response

`200 OK`:

```json
{
  "invitation_id": "guid",
  "tenant_id": "guid",
  "tenant_name": "Acme Pvt Ltd",
  "invited_email": "user@example.com",
  "first_name": "Jane",
  "last_name": "Doe",
  "role_name": "Employee",
  "expires_at": "2026-08-10T00:00:00Z",
  "status": "pending",
  "password_setup_enabled": true,
  "google_sign_in_enabled": true,
  "allow_google_email_mismatch": false,
  "allowed_email_domains": ["acme.com"]
}
```

## Errors

| Status | Cause |
|---|---|
| `404` | Token invalid, expired, or already used |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthInvitationController.cs` (`GetInvitation`)
Response: `InvitationDetailDto` (`src/ONEVO.Application/Features/Auth/Invite/DTOs/Responses/InvitationDetailDto.cs`)
Query: `GetInvitationByTokenQuery`
