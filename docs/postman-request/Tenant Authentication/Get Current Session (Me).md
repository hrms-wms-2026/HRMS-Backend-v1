# Get Current Session (Me)

**GET** `/api/v1/auth/me`

**Auth:** `[Authorize(Policy = "TenantPolicy")]` — requires a valid `onevo_session` cookie. **CSRF:** n/a (GET).

## Description

Session-bootstrap endpoint used by the frontend's `authGuard` on every app load / route guard check, to confirm the session is still valid and repopulate user/permission view state.

## Request

No body.

## Response

`200 OK`, `AuthSessionViewModel` (see `_Shared - Session Result Response.md`), always the fully-authenticated shape (`authenticated: true`, `workspace` populated) since this endpoint requires an already-established session.

## Errors

| Status | Cause |
|---|---|
| `401` | No session, expired, or revoked |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthSessionController.cs` (`Me`)
Query: `GetCurrentSessionQuery` (`src/ONEVO.Application/Features/Auth/Login/Queries/GetCurrentSession/`)
