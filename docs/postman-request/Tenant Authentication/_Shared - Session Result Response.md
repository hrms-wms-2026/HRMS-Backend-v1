# Shared: session-result response shape

Several endpoints in this folder (Login, Select Workspace, Login With Google, Force Change Password, Verify MFA, Accept Invitation With Password/Google, Complete Pending Legal Acceptance) all funnel through the same backend helper (`TenantAuthResponseWriter.HandleSessionResultAsync`) and return **one of these shapes**, depending on how far the login/continuation flow got. Each endpoint's own `.md` says which of these it can return — refer back here instead of repeating the schema.

## `AuthSessionViewModel` — used by every branch below except workspace-selection and tenant-session-exchange

```json
{
  "authenticated": true,
  "user": { "email": "user@example.com" },
  "permissions": ["employee.read", "..."],
  "active_modules": ["hr", "..."],
  "must_change_password": false,
  "mfa_required": false,
  "legal_acceptance_required": false,
  "pending_legal_documents": null,
  "expires_at": "2026-08-03T12:00:00Z",
  "continue_url": null,
  "workspace": null
}
```

- `must_change_password: true` → **202**, `continue_url` points at `/api/v1/auth/force-change-password` on the same host the request started on.
- `mfa_required: true` → **202**, `onevo_mfa` cookie set, `continue_url` points at `/api/v1/auth/mfa/verify`.
- `legal_acceptance_required: true` → **202**, `onevo_legal_pending` + `onevo_legal_csrf` cookies set, `pending_legal_documents` populated, `continue_url` points at `/api/v1/legal/acceptances/complete-login`.
- None of the above and not a tenant-session-exchange case → **200 OK**, real `onevo_session`/`onevo_csrf` cookies set, `authenticated: true`, `workspace` populated.

## Workspace-selection branch (base host, multiple tenant matches for one email)

**202**, body is `WorkspaceSelectionRequiredResponse`, not `AuthSessionViewModel`:

```json
{
  "requires_workspace_selection": true,
  "login_challenge": "opaque string, 5-minute single-use",
  "workspaces": [
    { "slug": "acme", "displayName": "Acme Pvt Ltd" }
  ]
}
```

## Tenant-session-exchange branch (base host, single match, every gate cleared)

**202**, body is `TenantSessionExchangeViewModel` — no cookie is set on this host; the browser must follow `continue_url` to the tenant subdomain and call Session Exchange there:

```json
{
  "authenticated": true,
  "redirect_required": true,
  "user": { "email": "user@example.com" },
  "workspace": { "slug": "acme", "display_name": "Acme Pvt Ltd" },
  "continue_url": "https://acme.onevo.example/auth/continue?code=...",
  "expires_at": "2026-08-03T12:02:00Z"
}
```

Source: `src/ONEVO.Api/Contracts/Auth/AuthSessionViewModel.cs`, `TenantSessionExchangeViewModel.cs`, `WorkspaceSelectionRequiredResponse.cs`, `AuthViewModelMapper.cs`, `Controllers/Tenant/Auth/TenantAuthResponseWriter.cs`.
