# Forgot Password

**POST** `/api/v1/auth/forgot-password`

**Auth:** `AllowAnonymous`. **CSRF:** exempt.

## Description

Requests a password-reset email. On a tenant host, the reset is scoped to that tenant's user; on the base host, resolves eligible tenant(s) the same way base login does. **Always returns the same generic message regardless of whether the email exists** — enumeration-safe.

## Request

```json
{
  "email": "user@example.com"
}
```

## Response

`200 OK`:

```json
{ "message": "If the email exists, a reset link has been sent." }
```

## Errors

None distinguishable — this endpoint deliberately never returns a different status for "email not found."

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthPasswordController.cs` (`ForgotPassword`)
`src/ONEVO.Api/Contracts/Auth/ForgotPasswordRequest.cs`
Handlers: `RequestPasswordResetCommandHandler` (tenant host) / `BaseForgotPasswordCommandHandler` (base host)
