# Logout

**POST** `/api/v1/auth/logout`

**Auth:** `[Authorize(Policy = "TenantPolicy")]`. **CSRF:** required (`X-CSRF-Token` header, mirrors `onevo_csrf` cookie).

## Description

Revokes the server-side session (`IsRevoked = true` in the `sessions` table via `TenantDatabaseTicketStore.RemoveAsync`) and clears every tenant auth cookie: `onevo_session` (via sign-out), `onevo_csrf`, `onevo_mfa`, `onevo_legal_pending`, `onevo_legal_csrf`.

## Request

No body.

## Response

`204 No Content`.

## Errors

| Status | Cause |
|---|---|
| `401` | No/expired session |
| `403` | Missing/invalid CSRF token |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthSessionController.cs` (`Logout`)
