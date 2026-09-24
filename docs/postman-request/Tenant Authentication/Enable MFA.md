# Enable MFA

**POST** `/api/v1/auth/mfa/enable`

**Auth:** `[Authorize(Policy = "TenantPolicy")]`. **CSRF:** required.

## Description

Begins TOTP MFA setup for the currently authenticated user: generates a secret and stores an **unverified** `UserMfa` row. The user must call **Confirm MFA Setup** with a code generated from this secret before MFA is actually enforced at login.

## Request

No body.

## Response

`200 OK`:

```json
{
  "secret": "base32 TOTP secret",
  "qrCodeUri": "otpauth://totp/..."
}
```

## Errors

| Status | Cause |
|---|---|
| `409` | MFA already enabled for this user |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthMfaController.cs` (`EnableMfa`)
Response: `MfaSetupDto` (`src/ONEVO.Application/Features/Auth/Login/DTOs/Responses/MfaSetupDto.cs`)
Handler: `EnableMfaCommandHandler` (`src/ONEVO.Application/Features/Auth/Login/Commands/MfaEnable/`)
