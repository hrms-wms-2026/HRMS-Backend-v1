# Google sign-in: Google Cloud Console setup

Tenant web login and the admin console use **Sign in with Google** (Google Identity Services) in
popup mode. The browser receives a Google **ID token** and posts it to the backend
(`POST /api/v1/auth/login/google`, admin: the admin Google login endpoint). The backend validates
signature, issuer, audience (our client ID) and expiry in `GoogleIdTokenValidator`. No access token
or refresh token is ever requested.

## One OAuth client per environment

Create a **separate OAuth client** for development and for production. Each environment's client ID
is set in **Admin → OAuth apps** (`platform_oauth_apps`, provider `google`). The web login page reads
it from `GET /api/v1/auth/google-config`, so changing it needs no code change or deploy.

## OAuth consent screen

| Setting | Value |
|---|---|
| User type | **External** |
| Publishing status | **In production** |
| Scopes | `openid`, `email`, `profile` only |

These scopes are not sensitive, so the app does not need Google verification. Do not add other scopes.

## OAuth client (type: Web application)

| Setting | Value |
|---|---|
| Authorized redirect URIs | **None.** The popup ID-token flow does not redirect. |
| Authorized JavaScript origins | **Required.** See below. |

Authorized JavaScript origins (scheme + host + port, no path, no trailing slash):

- Development client: `https://onexso.com:4200` (tenant root host) and the local admin console origin
  (for example `https://admin.crownus.test:4300`).
- Production client: the production tenant **root** domain and the production admin console origin.

Tenant subdomains are **not** needed: Google login runs only on the root host, then the user is
handed to their workspace subdomain through the session exchange.

## When unlisted accounts fail (R0 checklist)

With only `openid email profile`, Testing mode does not block accounts outside Test Users, so check
these first, in Chrome with DevTools open:

1. **Console / popup error from Google:** `origin_mismatch`, "The given origin is not allowed for the
   given client ID", or `access_blocked` → fix the JavaScript origins of the client in use.
2. **Which client ID is in use:** compare `GET /api/v1/auth/google-config` `clientId` with the client
   you edited in Google Cloud Console.
3. **Response of `POST /api/v1/auth/login/google`:**
   - `401` `code: google_auth_failed` → the token was rejected (wrong audience/client ID, expired,
     email not verified).
   - `403` `code: no_access` → Google worked; the account has no active user or invitation in any
     workspace, or a tenant policy blocks Google or the email domain. This is **not** a Google problem.

## What the backend allows

- A user is matched by the Google subject (`sub`) first; the verified email is only a fallback, and
  the link is saved when that first login fully completes (after MFA and workspace selection).
- An email match already linked to a different Google account is never signed in.
- `tenant_auth_policies.google_completion_allowed = 0` or a non-matching `allowed_login_domains_json`
  removes that workspace. No policy row means Google allowed for any domain.
- No user is ever created by Google login: access is by invitation or existing membership only.
