# Backend mkcert Tenant-Subdomain HTTPS Trust Fix Report

## Problem

The Angular frontend (now served over HTTPS via mkcert, see the frontend repo's own
`FRONTEND_MKCERT_DEV_SERVER_CERT_FIX_REPORT.md`) calls the tenant-host backend at
`https://acme.localhost:7229/api/v1/auth/session-exchange` after legal acceptance. The backend
was using the default `dotnet dev-certs` certificate, which browsers trust for `localhost` but
not necessarily for tenant subdomains (`acme.localhost`, `dapi.localhost`). A rejected/blocked
TLS handshake on that call surfaced as the misleading `"This sign-in link is invalid or has
expired."` message in the frontend, because the network/TLS failure was mapped to the same
fallback text as a genuinely invalid/expired exchange code.

## Files read (inspection, no changes)

- `src/ONEVO.Api/Properties/launchSettings.json`
- `src/ONEVO.Api/Program.cs`
- `src/ONEVO.Api/appsettings.json`
- `.env.example`
- `src/ONEVO.Infrastructure/Configuration/ConfigurationStartupValidator.cs`
- `src/ONEVO.Api/Configuration/DotEnvLoader.cs`
- `src/ONEVO.Api/Extensions/CorsExtensions.cs` (confirmed tenant-subdomain CORS already works via
  `Tenancy:RootDomain` + `{anything}.{RootDomain}` matching — out of scope, unchanged)
- `src/ONEVO.Api/ONEVO.Api.csproj` (confirmed `net10.0`, which natively supports PEM
  cert+key Kestrel configuration)
- Frontend: `src/app/core/auth/state/auth.store.ts`, `auth.store.spec.ts`,
  `session-exchange.component.ts`, `session-exchange.component.html`

## Files changed

Backend (`HRMS-Backend-v1`):

1. `src/ONEVO.Api/appsettings.Development.json` — added a `Kestrel:Certificates:Default`
   section pointing at the mkcert PEM cert/key (Development-only; not present in the base
   `appsettings.json`, so it has zero effect outside `ASPNETCORE_ENVIRONMENT=Development`).
2. `.gitignore` — added `.certs/`.
3. `BACKEND_MKCERT_TENANT_SUBDOMAIN_HTTPS_REPORT.md` — this report (new file).

Frontend (`Hrms--Web-application---front-end---v1`), per Step 5:

4. `src/app/core/auth/state/auth.store.ts` — `exchangeSession` now distinguishes a
   network/TLS/CORS failure (`HttpErrorResponse.status === 0` — request never reached the
   server) from a rejected/expired exchange code, using the same `status === 0` convention
   already established in `error.interceptor.ts`'s retry condition and
   `ErrorHandlerService.ERROR_TAXONOMY[0]`.
5. `src/app/core/auth/state/auth.store.spec.ts` — added a test covering the new status-0 path.

No other files were touched. Auth flow control logic, the API contract, login flow, and
production configuration were not changed.

### appsettings.Development.json diff

```diff
   "Tenancy": {
     "RootDomain": "localhost"
   },
+  "Kestrel": {
+    "Certificates": {
+      "Default": {
+        "Path": "../../.certs/backend-localhost-cert.pem",
+        "KeyPath": "../../.certs/backend-localhost-key.pem"
+      }
+    }
+  },
```

Kestrel binds its server options from the `Kestrel` configuration section automatically — no
`Program.cs` change was needed. The path is relative to the content root (the directory
`dotnet run` is launched from, `src/ONEVO.Api`), hence `../../.certs/...` to reach the
repo-root `.certs/` directory (matching the frontend's own `.certs/` convention).

### auth.store.ts diff (exchangeSession catch block)

```diff
       } catch (err) {
+        const isUnreachable = err instanceof HttpErrorResponse && err.status === 0;
         patchState(store, {
           loading: false,
-          error: extractErrorMessage(err, 'This sign-in link is invalid or has expired. Please sign in again.')
+          error: isUnreachable
+            ? 'Could not reach the tenant workspace. Check your local HTTPS certificate and try again.'
+            : extractErrorMessage(err, 'This sign-in link is invalid or has expired. Please sign in again.')
         });
         throw err;
       }
```

`login`, `acceptLegalDocuments`, and every other store method are unchanged. The API contract
(request/response shapes) is unchanged.

## Certificate details

Generated with:

```
C:\tools\mkcert\mkcert.exe -key-file .\.certs\backend-localhost-key.pem -cert-file .\.certs\backend-localhost-cert.pem localhost 127.0.0.1 ::1 acme.localhost dapi.localhost "*.localhost"
```

Files created (repo root, `HRMS-Backend-v1\.certs\`):
- `.certs\backend-localhost-cert.pem`
- `.certs\backend-localhost-key.pem`

**Exact names covered** (from the generated cert's SAN, confirmed via `openssl x509 -noout -ext
subjectAltName`):
```
DNS:localhost, DNS:acme.localhost, DNS:dapi.localhost, DNS:*.localhost,
IP Address:127.0.0.1, IP Address:0:0:0:0:0:0:0:1
```

Certificate issuer: `mkcert development CA` (same local CA already trusted in the Windows
`CurrentUser\Root` store from the frontend fix). Validity: through 3 Nov 2028.

**PFX**: not generated. .NET 10 Kestrel supports PEM certificate + separate key file directly
via `Certificates:Default:Path` + `Certificates:Default:KeyPath` configuration, so no PFX
conversion step was necessary. This is documented here rather than implemented as dead code.

No private key contents were printed at any point in this session.

## Backend listening URLs

Startup log (`dotnet run --launch-profile https` from `src/ONEVO.Api`):

```
[12:06:05 INF] Now listening on: https://localhost:7229
[12:06:05 INF] Application started. Press Ctrl+C to shut down.
[12:06:05 INF] Hosting environment: Development
[12:06:05 INF] Content root path: C:\onevoNew\HRMS-Backend-v1\src\ONEVO.Api
```

Only `https://localhost:7229` is listed. No `http://` URL and no port `5139` appear anywhere in
the startup log.

## Proof no HTTP/5139 fallback exists

- `src/ONEVO.Api/Properties/launchSettings.json` defines exactly one profile, `https`, with
  `"applicationUrl": "https://localhost:7229"` — no second HTTP URL, no `5139`.
- `Program.cs` calls `app.UseHttpsRedirection()` and `app.UseHsts()` only when
  `!app.Environment.IsDevelopment()`; it does not add any HTTP listen endpoint. It contains no
  hardcoded `5139` reference anywhere.
- The startup log (above) shows a single `Now listening on: https://localhost:7229` line and
  nothing else.
- `netstat` after startup showed the process bound only to `127.0.0.1:7229` / `[::1]:7229`.

## TLS verification

`curl`/`wget` are blocked by this environment's tooling policy, and this Windows machine's OS
resolver does not resolve `*.localhost` to loopback (only Chrome does that natively — there is
no hosts-file entry, and adding one was treated as out of scope: it's a system-level change,
and this task is scoped to local certificate wiring only). So DNS-dependent tools
(`Invoke-WebRequest`, `curl`) can reach `localhost` but not `acme.localhost` / `dapi.localhost`
without either a hosts-file entry or a browser that special-cases `*.localhost`.

To verify trust and hostname-matching for the tenant subdomains without touching DNS/hosts
config, a raw TLS handshake was performed from PowerShell: connect the TCP socket to
`127.0.0.1:7229` but request the target hostname via SNI/`SslStream.AuthenticateAsClient`. This
exercises exactly what a browser checks (chain trust against the OS store + SAN hostname match)
without needing the name to actually resolve:

```
=== localhost ===
TRUSTED (no exception) for hostname: localhost
Issuer: CN=mkcert TICS17\User@TICS17, OU=TICS17\User@TICS17, O=mkcert development CA
SAN: DNS Name=localhost, DNS Name=acme.localhost, DNS Name=dapi.localhost, DNS Name=*.localhost, IP Address=127.0.0.1, IP Address=::1

=== acme.localhost ===
TRUSTED (no exception) for hostname: acme.localhost
(same issuer/SAN)

=== dapi.localhost ===
TRUSTED (no exception) for hostname: dapi.localhost
(same issuer/SAN)
```

`AuthenticateAsClient` throws on any chain-trust or hostname-mismatch failure, so "no exception"
for all three hostnames is a direct pass/fail signal, not an inference. Additionally,
`Invoke-WebRequest -Uri https://localhost:7229/health` (which does resolve, being plain
`localhost`) returned `200 Healthy`, confirming the app layer behind the new cert works too, not
just the TLS handshake.

Note: an already-running `ONEVO.Api.exe` (PID 35104, started before this session's cert change)
was found holding port 7229 with the old certificate. The user confirmed stopping it; a fresh
`dotnet run --launch-profile https` was then started, which is the instance verified above.

## Manual browser verification status

**Not completed by the assistant.** The Claude-in-Chrome browser extension was not connected
in this environment (`tabs_context_mcp` returned "Browser extension is not connected"), the same
gap noted in the frontend report. The backend is left running at `https://localhost:7229` (and
the frontend dev server is already running at `https://localhost:4200` with active browser
connections) so the user can complete:

1. `https://localhost:7229/health`
2. `https://acme.localhost:7229/health`
3. `https://dapi.localhost:7229/health`
4. The full end-to-end login → legal acceptance → `acme.localhost:4200/auth/continue?code=...`
   → session-exchange → dashboard flow, using a **fresh** login attempt (exchange codes are
   one-time/expiring, so a stale `continue_url` will not work).

Given the TLS evidence above (OS-level trust + hostname match succeeds for all three names
using the same mkcert root CA the frontend already established as trusted in Chrome), these are
expected to pass without `NET::ERR_CERT_AUTHORITY_INVALID` or `ERR_SSL_PROTOCOL_ERROR`.

## Production model (unchanged, confirmed by inspection)

- `appsettings.json` (base, all environments) has no `Kestrel` section — the mkcert
  configuration lives only in `appsettings.Development.json` and has no effect in Production.
- No tenant hostname (e.g., `acme.localhost`, `dapi.localhost`, or any real tenant subdomain) is
  hardcoded anywhere in the changed files. The only hostnames added are to the local-only mkcert
  cert-generation command and the local-only `.certs/` files, which are gitignored.
- Tenant existence continues to be resolved from the database by
  `HostTenantResolutionMiddleware` after TLS completes — this middleware and its ordering in
  `Program.cs` were not touched.
- Production is expected to terminate TLS with a real certificate authority and a real wildcard
  cert (e.g., `onevo.com` + `*.onevo.com`), provisioned outside this repo's Development-only
  config — this fix does not touch or assume anything about that path.

## Test / build results (frontend, Step 5 touched it)

| Command | Result |
|---|---|
| `npm test` | ✅ 22 test files, 101 tests passed (100 existing + 1 new) |
| `npm run build` | ✅ Production build succeeded |
| `npm run build:staging` | ✅ Staging build succeeded |

Backend:

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal` | ✅ Build succeeded, 0 warnings, 0 errors |

## Remaining gaps

- Manual browser verification (Part 4/Step 6 browser checks and the full end-to-end auth flow)
  was not performed by the assistant — no connected browser tool in this environment. Both
  servers are left running for the user to complete this directly.
- OS-level (non-browser) resolution of `acme.localhost` / `dapi.localhost` requires either a
  hosts-file entry (not added — out of scope as a system-level change) or a browser that
  special-cases `*.localhost` (Chrome does; PowerShell/`curl` do not). This only affects
  scripted verification tooling, not the actual browser-based dev flow this fix targets.
- Each developer must generate their own local `.certs/backend-localhost-{cert,key}.pem` (this
  report documents the exact `mkcert` command) since `.certs/` is gitignored — `dotnet run` will
  throw a file-not-found error on `Kestrel:Certificates:Default:Path` until that's done. This is
  expected/standard for mkcert-based local setups and mirrors the frontend repo's own model.
- If the mkcert root CA is ever regenerated, both the frontend and backend local certs need
  regenerating to stay trusted.
