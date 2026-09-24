# Tenant Session Exchange Login Flow - Implementation Report

Status: implemented and verified locally. Not committed or pushed (per instruction).

## 1. Goal

Base-domain login (onevo.com / localhost) must never create the final tenant session cookie on
the base host. After every gate clears (credentials or Google identity, workspace resolution, MFA
if enabled, legal acceptance if required), the base host issues a short-lived, single-use, opaque
exchange code and a `continue_url` pointing at the tenant subdomain. Only the tenant host's new
`POST /api/v1/auth/session-exchange` endpoint consumes that code and creates the real,
host-scoped `onevo_session` / `onevo_csrf` cookies. No shared parent-domain cookie is used.

This replaces the `Domain=.{RootDomain}` cookie-sharing approach built earlier in this session,
which this task's instructions explicitly rejected ("Do not use shared parent-domain onevo_session
cookies as the fix"). That work has been reverted; see Section 6.

## 2. Step 1 findings (current behavior, before this change)

- `/api/v1/auth/me` (`AuthSessionController.Me`) is a read-only query
  (`GetCurrentSessionQuery` / `GetCurrentSessionQueryHandler`). It never calls `SignInAsync`,
  never touches `TenantDatabaseTicketStore`, and never creates a session. It is gated by
  `[Authorize(Policy = "TenantPolicy")]`, which requires an already-valid `onevo_session` cookie
  on a tenant host. Sliding renewal of that cookie, when the ASP.NET Core cookie middleware
  decides renewal is due, happens automatically via `TenantDatabaseTicketStore.RenewAsync` on any
  authenticated request (including `/me`) - `/me` participates in that only as an ordinary
  authenticated request, not as a manual "refresh" call. `/me` cannot be used to transfer a
  session from one host to another: `TenantEnforcementMiddleware` and `HostTenantResolutionMiddleware`
  require the request's own Host header to already resolve to the tenant whose session cookie was
  presented.
- Before this change, `BaseLoginCommandHandler`, `BaseGoogleLoginCommandHandler`,
  `SelectWorkspaceCommandHandler` all ran exclusively on the base host and, once every gate
  cleared, ended in `LoginContinuationService.FinishAuthenticatedLoginAsync` calling
  `HttpContext.SignInAsync("TenantScheme", ...)` directly on that base-host request via
  `TenantAuthResponseWriter.SignInAsync`, setting `onevo_session`/`onevo_csrf` there. That cookie
  could not safely reach a tenant subdomain without either a shared parent-domain `Domain=`
  cookie (rejected) or the exchange-code handoff implemented here.
- Two more entry points were discovered during Step 1 that were **not** in the original 17-file
  list but also call the same `FinishAuthenticatedLoginAsync` tail:
  `AcceptInvitationPasswordCommandHandler` and `AcceptInvitationGoogleCommandHandler`
  (`src/ONEVO.Application/Features/Auth/Invite/Commands/...`), plus
  `ForcePasswordChangeCommandHandler`. All three require
  `ITenantContext.ContextMode == TenantContextMode.Tenant` before they run at all - they only
  ever execute on an already-correct tenant host (invite links and forced-password-change are
  tenant-scoped), so they must **not** be routed through the exchange hand-off; doing so would be
  an unnecessary, incorrect extra hop. This is addressed by the explicit
  `LoginFinalizationMode` described in Section 4.

## 3. New browser-facing flow

1. `POST /api/v1/auth/login` (base host). Backend verifies credentials/Google identity, resolves
   workspace, gates on MFA/legal exactly as before.
2. Once every gate clears, the base host returns (202 Accepted):
   ```
   {
     "authenticated": false,
     "redirect_required": true,
     "user": { "email": "..." },
     "workspace": { "slug": "acme", "display_name": "Acme" },
     "continue_url": "https://acme.onevo.com/auth/continue?code=<opaque-code>",
     "expires_at": "..."
   }
   ```
   No `onevo_session` / `onevo_csrf` cookie is ever set on this response.
3. The SPA navigates the browser to `continue_url`.
4. The tenant frontend calls `POST /api/v1/auth/session-exchange` on the tenant host with
   `{ "code": "<opaque-code>" }`.
5. The backend atomically consumes the code, verifies it belongs to the resolved tenant (the host
   the request itself arrived on, resolved by `HostTenantResolutionMiddleware` - never taken from
   the request body), and creates the real, host-scoped session:
   ```
   {
     "authenticated": true,
     "user": { "email": "..." },
     "workspace": { "slug": "acme", "display_name": "Acme" },
     "permissions": [...],
     "active_modules": [...],
     "must_change_password": false,
     "mfa_required": false,
     "legal_acceptance_required": false,
     "expires_at": "..."
   }
   ```
   `onevo_session` (HttpOnly) and `onevo_csrf` (readable) are set on this response only, host-scoped
   to the tenant subdomain (no `Domain=` attribute).

MFA and legal acceptance challenges are unchanged: they still happen entirely on the base host
(or, for invite acceptance, the tenant host that was already correct) exactly as before. Only the
final "every gate cleared" outcome changed.

## 4. Explicit base-host vs tenant-host distinction

Per instruction ("Use explicit types or flags. Do not infer from random host strings deep inside
Application unless there is an existing clean abstraction."), a new enum,
`LoginFinalizationMode` (`ILoginContinuationService.cs`), was added:

- `BaseDomainExchange` - the caller runs on the base host (or a pre-tenant continuation of one):
  `BaseLoginCommandHandler`, `BaseGoogleLoginCommandHandler`, `SelectWorkspaceCommandHandler`
  (structurally base-host-only), and `VerifyMfaCommandHandler` /
  `AcceptPendingLegalDocumentsCommandHandler` when their own, pre-existing
  `_tenantContext.ContextMode == TenantContextMode.Tenant` check is false.
- `TenantHostDirect` - the caller already runs inside the correct tenant host's request:
  `AcceptInvitationPasswordCommandHandler`, `AcceptInvitationGoogleCommandHandler`,
  `ForcePasswordChangeCommandHandler` (always - each has its own hard `TenantContextMode.Tenant`
  guard), and `VerifyMfaCommandHandler` / `AcceptPendingLegalDocumentsCommandHandler` when their
  `isTenantContext` check is true.

Every caller passes the mode explicitly; `LoginContinuationService` never infers it from a host
string, `ITenantContext`, or any other ambient state. `FinishAuthenticatedLoginAsync` branches
only on this caller-supplied value:
- `BaseDomainExchange` -> `TenantSessionExchangeService.CreateAsync` (issues the exchange code,
  never signs in).
- `TenantHostDirect` -> `ILoginSessionMaterialFactory.PrepareAsync` + `HttpContext.SignInAsync`,
  the same direct sign-in every login used before this change - unaffected for these three
  handlers.

## 5. Files changed

### New files
- `src/ONEVO.Domain/Features/Auth/Entities/TenantSessionExchangeChallenge.cs` - entity (hash-only,
  no raw/plaintext code field).
- `src/ONEVO.Infrastructure/Persistence/Configurations/Auth/Login/TenantSessionExchangeChallengeConfiguration.cs`
- `src/ONEVO.Infrastructure/Migrations/20260729082336_AddTenantSessionExchangeChallenges.cs`
  (+ `.Designer.cs`) - creates `tenant_session_exchange_challenges`, RLS `tenant_isolation` policy.
- `src/ONEVO.Application/Features/Auth/Login/RepositoryInterfaces/ITenantSessionExchangeChallengeRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfTenantSessionExchangeChallengeRepository.cs`
- `src/ONEVO.Application/Features/Auth/Login/ServiceInterfaces/ITenantSessionExchangeService.cs`
- `src/ONEVO.Application/Features/Auth/Login/Services/TenantSessionExchangeService.cs`
- `src/ONEVO.Application/Features/Auth/Login/DTOs/Responses/TenantSessionExchangeResponseDto.cs`
- `src/ONEVO.Api/Contracts/Auth/TenantSessionExchangeRequest.cs`
- Tests: `EfTenantSessionExchangeChallengeRepositoryTests.cs`, `TenantSessionExchangeServiceTests.cs`,
  `TenantSessionExchangeEndpointTests.cs`, `TenantSessionExchangeArchitectureTests.cs`,
  `LoginWorkspaceResponseTests.cs` (pre-existing but untracked from an earlier uncommitted task in
  this session; extended here).

### Modified files (backend logic)
- `src/ONEVO.Application/Features/Auth/Login/DTOs/Responses/LoginResponseDto.cs` - added
  `TenantSessionExchangeMaterial` and `TenantSessionExchange` field,
  `RequiresTenantSessionExchange`, `ToTenantSessionExchangeResponse()`.
- `src/ONEVO.Application/Features/Auth/Login/ServiceInterfaces/ILoginContinuationService.cs` -
  added `LoginFinalizationMode`, `LoginContinuationRequest.FinalizationMode`,
  `FinishAuthenticatedLoginAsync(..., LoginFinalizationMode, ...)`.
- `src/ONEVO.Application/Features/Auth/Login/Services/LoginContinuationService.cs` - branches on
  `finalizationMode` instead of always signing in.
- `src/ONEVO.Api/Controllers/Tenant/Auth/TenantAuthResponseWriter.cs` -
  `HandleSessionResultAsync` returns the exchange response (202) before the direct sign-in branch;
  reverted all `Domain=` cookie attributes back to host-scoped only.
- `src/ONEVO.Api/Controllers/Tenant/Auth/AuthSessionController.cs` - new `POST session-exchange`
  endpoint (`[AllowAnonymous]`, 400 off tenant host).
- `src/ONEVO.Application/Features/Auth/Login/Commands/BaseLogin/BaseLoginCommandHandler.cs`,
  `.../BaseGoogleLogin/BaseGoogleLoginCommandHandler.cs`,
  `.../SelectWorkspace/SelectWorkspaceCommandHandler.cs` - pass `FinalizationMode: BaseDomainExchange`.
- `.../MfaVerify/VerifyMfaCommandHandler.cs`,
  `src/ONEVO.Application/Features/Auth/Legal/Commands/AcceptPendingLegalDocuments/AcceptPendingLegalDocumentsCommandHandler.cs`
  - pass `isTenantContext ? TenantHostDirect : BaseDomainExchange`.
- `src/ONEVO.Application/Features/Auth/Invite/Commands/AcceptInvitationPassword/AcceptInvitationPasswordCommandHandler.cs`,
  `.../AcceptInvitationGoogle/AcceptInvitationGoogleCommandHandler.cs`,
  `src/ONEVO.Application/Features/Auth/Login/Commands/ForcePasswordChange/ForcePasswordChangeCommandHandler.cs`
  - pass `TenantHostDirect` (unaffected behavior, kept as direct sign-in).
- `src/ONEVO.Application/Features/Auth/Login/Queries/GetCurrentSession/GetCurrentSessionQueryHandler.cs`
  - workspace slug now read from `ITenantContext.Slug` (already-resolved host context) instead of
    the freshly-queried tenant row; display name still comes from `ITenantRepository`.
- `src/ONEVO.Infrastructure/DependencyInjection.cs`,
  `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` - new DI registrations, new DbSet.

### Reverted (Domain-sharing cookie fix from earlier this session)
- `src/ONEVO.Api/Extensions/AuthenticationExtensions.cs` - `AddApiAuthentication` back to its
  original 2-parameter signature; `options.Cookie.Domain` removed (`onevo_session` is host-scoped
  only again).
- `src/ONEVO.Api/Program.cs` - call site reverted to match.
- `src/ONEVO.Api/Controllers/Tenant/Auth/TenantAuthResponseWriter.cs` - all `Domain=` cookie
  attributes removed from `onevo_csrf`, `onevo_mfa`, `onevo_legal_pending`, `onevo_legal_csrf`;
  `GetPendingCookieDomain`/`UsesRootDomainCookie` helpers deleted.
- `src/ONEVO.Api/Middleware/TenantEnforcementMiddleware.cs` - `Domain=` removed from both
  cookie-delete call sites; `GetRootDomain` helper deleted.
- `tests/ONEVO.Tests.Unit/Features/Auth/TenantCookieAuthenticationConfigurationTests.cs` - rewritten
  to assert `Cookie.Domain` is `null` (host-scoped) instead of `Tenancy:RootDomain`.

### Configuration
- `src/ONEVO.Api/appsettings.Development.json` and local `.env` (`Urls__AppBaseUrl`) - value
  changed from `http://localhost:5173` (stale, unused - `PlatformOptions.AppBaseUrl` is bound but
  never injected anywhere in `src/`) to `http://localhost:4200`, matching the real Angular dev
  server. `TenantSessionExchangeService.BuildContinueUrl` is the first real consumer of this
  setting. Verified via `grep` that no other feature (invite/reset email links use the separate
  `Email:AppBaseUrl` key) depends on the old value.
- `tests/ONEVO.Tests.Integration/Auth/BaseDomainLoginTestFactory.cs`,
  `tests/ONEVO.Tests.Integration/E2E/E2ETestFactory.cs` - added `Urls:AppBaseUrl` for the same
  reason.

### Test files updated for the new response shape (202 + redirect_required instead of 200 +
onevo_session, on the base host)
- `tests/ONEVO.Tests.Unit/Features/Auth/LoginContinuationServiceTests.cs`,
  `GetCurrentSessionQueryHandlerTests.cs`, `VerifyMfaCommandHandlerTests.cs`,
  `AcceptInvitationDirectoryTests.cs`, `ForcePasswordChangeLegalTests.cs`.
- `tests/ONEVO.Tests.Integration/Auth/BaseDomainLoginIntegrationTests.cs` - existing final-step
  assertions changed from "200 OK + onevo_session set here" to "202 Accepted + redirect_required,
  then a session-exchange call on the tenant host sets the cookies"; new tests added for the full
  single-tenant flow, multi-workspace wrong-tenant rejection, base-host rejection (400), single-use
  enforcement, and no-raw-code/no-PII-leak checks.
- `tests/ONEVO.Tests.Integration/E2E/TenantProvisioningE2ETests.cs` - owner base-domain login step
  updated to expect the exchange hand-off (invite acceptance itself is unaffected: it already runs
  on the tenant host and correctly uses `TenantHostDirect`).
- `tests/ONEVO.Tests.Integration/Features/DevPlatform/Compliance/LegalDocumentRichContentIntegrationTests.cs`
  - two base-host login assertions updated from `authenticated:true` (200) to `redirect_required:true`
  (202).
- `tests/ONEVO.Tests.Architecture/WorkspaceResponseArchitectureTests.cs` - two assertions updated to
  match the new source text (`_tenantSessionExchange.CreateAsync(` instead of
  `_sessionMaterialFactory.PrepareAsync(...)` directly; `_tenantContext.Slug!` instead of
  `tenant.Slug`).

## 6. Cookie rules (Step 8)

- `onevo_session` and `onevo_csrf` are set only by the tenant host's session-exchange success
  response, with no `Domain=` attribute - standard host-only cookies, `SameSite=Strict`,
  `HttpOnly` for `onevo_session`, readable for `onevo_csrf` (double-submit CSRF, unchanged).
- Because the exchange request itself runs on the tenant host (the SPA does a top-level navigation
  to `continue_url`, then makes a same-origin `POST /api/v1/auth/session-exchange` from that host),
  `SameSite=Strict` is correct and unaffected - there is no cross-origin POST anywhere in this flow.
- No cookie in this codebase now carries a shared parent-domain `Domain` attribute.

## 7. Security model

- The exchange code is generated by `ISecureTokenGenerator.GenerateOpaqueToken()` (existing,
  cryptographically random opaque token generator already used for MFA/legal/CSRF challenges).
- Only `SHA-256` hash of the code (`CodeHash`) is ever persisted or logged; the raw code exists
  only in the `continue_url` query string returned once to the browser.
- `TenantSessionExchangeChallengeRepository.TryConsumeAsync` is a single guarded
  `ExecuteUpdateAsync` (`WHERE code_hash = ... AND tenant_id = ... AND consumed_at IS NULL AND
  expires_at > now`), not a read-then-write round trip - two concurrent consume attempts can never
  both succeed.
- The tenant is bound at consumption time both by the challenge row's own `tenant_id` and by
  `HostTenantResolutionMiddleware`'s host-based resolution (`_tenantContext.TenantId`, from the
  request's own Host header) - never from anything client-supplied. A code presented on the wrong
  tenant host fails.
- `tenant_session_exchange_challenges` is RLS-protected with the standard `tenant_isolation`
  policy (admin bypass + tenant match), identical in shape to `legal_login_challenges` - no special
  pre-tenant lookup policy is needed because, unlike MFA/legal challenges, this table is only ever
  read/written once the request's tenant context is already resolved (created after
  `_tenantSwitcher.SwitchToTenantAsync` on the base-host side; consumed after
  `HostTenantResolutionMiddleware` resolves the tenant host on the exchange-endpoint side).
- Code lifetime is 2 minutes (shorter than the 10-minute MFA/legal challenge lifetimes, since
  nothing here waits on human input - the browser follows `continue_url` immediately).
- `expires_at`, `ip_address`, `user_agent` are recorded on the challenge row (same audit shape as
  MFA/legal challenges); no PII, session id, or password material is ever placed in the
  `continue_url` - only the opaque code, workspace slug, and display name.
- `LastLoginAt` is now updated at the moment the tenant-host session is actually established
  (`TenantSessionExchangeService.ConsumeAsync`), not when the exchange code is merely issued on
  the base host - a browser that never follows `continue_url` never updates `LastLoginAt`.

## 8. GDPR / privacy note

No personally identifiable information (email, name, permissions, tenant id, user id) is ever
placed in a URL, query string, or `continue_url` in this flow. The only value carried in
`continue_url` is the opaque, single-use, short-lived exchange code itself, which is meaningless
without server-side state and cannot be used to derive any user attribute. `TenantSessionExchangeResponseDto`
(the base-host response) carries the user's email in the JSON body (not the URL) for display
purposes only, consistent with the existing MFA/legal challenge responses, which already do the
same.

## 9. Test results

- `dotnet build src/ONEVO.Api/ONEVO.Api.csproj` - 0 errors (1 pre-existing, unrelated nullable
  warning in `AdminAuthController.cs`).
- Unit tests (`ONEVO.Tests.Unit`): 1022/1022 passing.
- Architecture tests (`ONEVO.Tests.Architecture`): 288/289 passing. The one failure,
  `CredentialOwnershipCompletionArchitectureTests.LocalEnvironment_WhenPresent_ContainsNoDeprecatedProviderSettings`,
  is pre-existing and unrelated (flags an `Email__Provider` key already present in the local
  `.env` file before this task; not touched by this change; already noted as pre-existing in
  `LOGIN_WORKSPACE_RESPONSE_FIX_REPORT.md` from an earlier task this session).
- Integration tests (`ONEVO.Tests.Integration`, Docker available and used - real PostgreSQL via
  Testcontainers): first a targeted run of every file identified (via full-repository search) as
  touching the changed auth/session code paths -
  `Auth/BaseDomainLoginIntegrationTests.cs` (25 tests, including new coverage for the full
  base-login-to-exchange flow, multi-workspace wrong-tenant rejection, base-host rejection,
  single-use enforcement, and no-secret-leak checks),
  `E2E/TenantProvisioningE2ETests.cs` (1 test, full admin-provisioning-to-owner-login journey),
  `Features/DevPlatform/Compliance/LegalDocumentRichContentIntegrationTests.cs` (2 tests) - 28/28
  passing. Then the **entire** integration suite (all classes, including billing, roles,
  employees, and every other area unrelated to this change) was run to completion:
  **113/113 passing**, 12m35s, 0 failures.

## 10. Post-deployment fix: CSRF middleware blocked session-exchange with a stale cookie

Found via live manual testing after the initial implementation: `CsrfProtectionMiddleware` only
validates CSRF when the request carries an `onevo_session` cookie
(`ShouldValidate` -> `context.Request.Cookies.ContainsKey("onevo_session")`). `session-exchange` is
the endpoint that *creates* that cookie, so on a truly clean client this check is correctly
skipped. But if the client (browser or API client) happens to carry an **old, no-longer-valid**
`onevo_session` cookie - for example left over from testing done before this task reverted the
shared-parent-domain-cookie approach - the middleware sees the cookie, attempts to authenticate it
against `TenantScheme`, fails (the ticket is invalid/expired), and rejects with 403 "A valid CSRF
token is required for this request" - before the request ever reaches the session-exchange
handler. Every other pre-session, `[AllowAnonymous]` endpoint (`login`, `mfa/verify`,
`login/select-workspace`, `login/google`, `legal/acceptances/complete-login`) is already exempted
from this check for exactly this documented reason ("blocking them when a stale session cookie
exists breaks UX"); `session-exchange` was missing from that list.

Fix: added `/api/v1/auth/session-exchange` to `CsrfProtectionMiddleware.ExemptPaths`
(`src/ONEVO.Api/Middleware/CsrfProtectionMiddleware.cs`). This is safe: the endpoint's real
authorization is the opaque, single-use, hashed exchange code itself, not a CSRF token - a
cross-site request cannot succeed without already possessing a valid code, which is never
reachable by a malicious page.

Added `CsrfProtectionMiddlewareTests.UnsafeMethod_ExemptSessionExchangePath_IsNotBlocked`
(`tests/ONEVO.Tests.Unit/Features/Auth/CsrfProtectionMiddlewareTests.cs`), which specifically
simulates a stale/unrelated `onevo_session` cookie being present and asserts the request still
passes through. This exact scenario was not covered by the integration tests added earlier (they
never attached a cookie to the exchange request at all, so they could not have caught this).
Re-verified: unit tests 1023/1023 passing.

Separately, a live login response was reported showing `authenticated: false` with no
`legal_acceptance_required` step for a returning user - this is correct, expected behavior (that
user/tenant already has accepted legal documents on file from earlier testing), not a defect.

## 11. Remaining limitations / follow-ups

- Frontend (Angular) is not yet updated to consume `redirect_required`/`continue_url` or call the
  new `session-exchange` endpoint. This was explicitly out of scope for this task (backend-only,
  "Work only in HRMS-Backend-v1") and is tracked as the next step.
- `ForcePasswordChangeCommandHandler` requires `TenantContextMode.Tenant`, but
  `TenantAuthResponseWriter.WithContinueUrl` sets the `must_change_password` continue_url to "the
  host where login started" - which, for a base-domain password login, is the base host. This
  pre-existing inconsistency (not introduced by this task, and not exercised by any existing test)
  means a real `must_change_password` gate reached from base-domain login may point to an
  unreachable continue_url. Flagging per instruction not to silently skip; recommend a follow-up
  task specifically for the forced-password-change flow's host routing.
- The single unique index on `tenant_session_exchange_challenges.code_hash` is scoped to active
  (`consumed_at IS NULL`) rows only (a deliberate simplification - see the EF configuration file
  comment); SHA-256 makes a hash collision with a dead row practically impossible.
