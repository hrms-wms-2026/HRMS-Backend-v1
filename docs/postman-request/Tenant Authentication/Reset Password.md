# Reset Password

**POST** `/api/v1/auth/reset-password`

**Auth:** `AllowAnonymous`. **CSRF:** exempt.

## Description

Consumes the reset token from the Forgot Password email and sets a new password. Also revokes all active `RefreshToken` rows for the user (forces re-login) — see the auth workflow doc's gap note: this is the legacy `RefreshToken` table, not the current session table.

## Request

```json
{
  "token": "reset token from email link",
  "newPassword": "NewPassword123!"
}
```

## Response

`200 OK`:

```json
{ "message": "Password reset successful. Please log in." }
```

## Errors

| Status | Cause |
|---|---|
| `400` / `401` | Token invalid or expired |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthPasswordController.cs` (`ResetPassword`)
`src/ONEVO.Api/Contracts/Auth/ResetPasswordRequest.cs`
Handler: `ResetPasswordCommandHandler` (`src/ONEVO.Application/Features/Auth/Login/Commands/ResetPassword/`)
