# Confirm MFA Setup

**POST** `/api/v1/auth/mfa/confirm-setup`

**Auth:** `[Authorize(Policy = "TenantPolicy")]`. **CSRF:** required.

## Description

Verifies the first TOTP code generated from the secret returned by **Enable MFA**, and flips the stored `UserMfa` row to `IsVerified = true`. From this point on, login requires MFA (see **Login** → `mfa_required` branch → **Verify MFA**).

## Request

```json
{
  "code": "123456"
}
```

## Response

`200 OK`:

```json
{ "success": true }
```

## Errors

| Status | Cause |
|---|---|
| `400` | Code invalid, or no pending MFA setup for this user |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthMfaController.cs` (`ConfirmMfaSetup`)
`src/ONEVO.Api/Contracts/Auth/ConfirmMfaSetupRequest.cs`
Handler: `ConfirmMfaSetupCommandHandler` (`src/ONEVO.Application/Features/Auth/Login/Commands/MfaConfirmSetup/`)
