# Verify MFA

**POST** `/api/v1/auth/mfa/verify`

**Auth:** `AllowAnonymous`, but requires the `onevo_mfa` cookie (set by a prior Login/Select Workspace/etc. response when `mfa_required: true`). **CSRF:** exempt.

## Description

Completes login when the account has verified MFA enrolled. Verifies the TOTP code against the decrypted secret (±90 second window), with a 5-attempt lockout. On success, continues the same finalization logic as Login (`FinishAuthenticatedLoginAsync`) and clears the `onevo_mfa` cookie.

## Request

```json
{
  "code": "123456"
}
```

## Response

Same branches as **Login**'s post-continuation result — usually `200 OK` with `AuthSessionViewModel`, or a further gate (legal acceptance) if still pending. See `_Shared - Session Result Response.md`.

## Errors

| Status | Cause |
|---|---|
| `401` | `onevo_mfa` cookie missing/expired, or code invalid (5-attempt lockout applies) |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthMfaController.cs` (`VerifyMfa`)
`src/ONEVO.Api/Contracts/Auth/VerifyMfaRequest.cs`
Handler: `VerifyMfaCommandHandler` (`src/ONEVO.Application/Features/Auth/Login/Commands/MfaVerify/`)
