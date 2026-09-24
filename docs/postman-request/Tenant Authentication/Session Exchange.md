# Session Exchange

**POST** `/api/v1/auth/session-exchange`

**Auth:** `AllowAnonymous`. **CSRF:** exempt. **Host:** tenant host only — non-tenant-host requests get `400`.

## Description

The last step of a base-domain login: consumes the one-time exchange code from **Login**'s `continue_url` and finally sets the real `onevo_session`/`onevo_csrf` cookies on the correct tenant host. The tenant is resolved from the request's `Host` header, never from the request body.

## Request

```json
{
  "code": "opaque one-time code from continue_url, 2-minute expiry, single-use"
}
```

## Response

`200 OK`, `AuthSessionViewModel` (see `_Shared - Session Result Response.md`) with `authenticated: true` and real session cookies now set.

## Errors

| Status | Cause |
|---|---|
| `400` | Request made on a non-tenant host |
| `401` | Code invalid, expired, or already consumed |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthSessionController.cs` (`SessionExchange`)
`src/ONEVO.Api/Contracts/Auth/TenantSessionExchangeRequest.cs`
Service: `ITenantSessionExchangeService.ConsumeAsync`
