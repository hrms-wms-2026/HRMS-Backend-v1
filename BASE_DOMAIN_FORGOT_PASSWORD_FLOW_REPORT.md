# Base-Domain Forgot-Password Flow — Implementation Report

## Files changed

**Source**
- `src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs` — fixed `GetByNormalizedEmailAsync`, `GetActiveByNormalizedEmailAsync`, `GetByTenantAndEmailAsync` to compare `User.NormalizedEmail` instead of `User.Email`.
- `src/ONEVO.Application/Common/ServiceInterfaces/IEmailService.cs` — added optional `string? tenantSlug = null` parameter to `SendPasswordResetAsync`.
- `src/ONEVO.Infrastructure/ExternalServices/Email/TransactionalEmailService.cs` — passes `tenantSlug` through to the `password_reset` template as `tenant_slug`.
- `src/ONEVO.Infrastructure/ExternalServices/Email/EmailTemplateRenderer.cs` — `RenderPasswordReset` builds a tenant-scoped link (`{slug}.{appBaseUrlHost}/auth/reset-password?token=...`) when a `tenant_slug` is present; unchanged flat-URL behavior when absent.
- `src/ONEVO.Application/Features/Auth/Login/Commands/RequestPasswordReset/RequestPasswordResetCommandHandler.cs` — updated the one existing `SendPasswordResetAsync` call site for the new signature (`tenantSlug: null`, i.e. unchanged tenant-host link format).
- `src/ONEVO.Application/Features/Auth/Login/Commands/BaseForgotPassword/BaseForgotPasswordCommand.cs` (new) + `BaseForgotPasswordCommandHandler.cs` (new) — base-domain forgot-password handler.
- `src/ONEVO.Api/Controllers/Tenant/Auth/AuthPasswordController.cs` — `ForgotPassword` action now branches on `ITenantContext.ContextMode` (mirrors `AuthLoginController`'s tenant-vs-base pattern) and always returns the same generic 200.
- `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs` — `UserEmail` constant changed to `siyasiyamala932@gmail.com`; `SeedTenantUserAsync` now looks up the existing row by `Id` instead of `Email` (so re-seeding an existing dev DB updates the row in place instead of hitting a PK conflict); `SeedGlobalEmailDirectoryAsync` now deletes any stale `global_email_directory` row for the tenant under the old email before inserting the new one.

**Tests**
- `tests/ONEVO.Tests.Unit/Features/Auth/EfAuthRepositoryAuthCoreTests.cs` — added 3 mixed-case/whitespace regression tests.
- `tests/ONEVO.Tests.Unit/Features/SharedPlatform/Email/EmailTemplateRendererTests.cs` (new) — flat URL, tenant-scoped URL (dev + prod-shaped host), placeholder fallback.
- `tests/ONEVO.Tests.Unit/Features/Auth/BaseForgotPasswordCommandHandlerTests.cs` (new) — zero/one/multiple-candidate cases, token invalidation.
- `tests/ONEVO.Tests.Integration/Auth/BaseDomainForgotPasswordIntegrationTests.cs` (new) — 5 full-stack HTTP tests (real Postgres, real `HostTenantResolutionMiddleware`, real `auth_lookup_base_login_candidates` function).
- `tests/ONEVO.Tests.Integration/E2E/CapturingEmailService.cs` — updated test double for the new `IEmailService` signature.

No OneVo-HR docs were touched. No commit was made.

## Old behavior vs new behavior

| | Old | New |
|---|---|---|
| `POST /api/v1/auth/forgot-password` on `{slug}.localhost` | Resolves tenant from host, creates a reset token scoped to that tenant/user. | **Unchanged.** |
| `POST /api/v1/auth/forgot-password` on `localhost` (base) | `ITenantContext.ContextMode` is `System` (never `Tenant`), so `RequestPasswordResetCommandHandler` hit its `!IsResolved || ContextMode != Tenant` guard and silently returned `Result.Success()` — generic 200, **no token, no email, ever**. | Controller detects `ContextMode != Tenant` and sends `BaseForgotPasswordCommand` instead, which looks up eligible users via `IBaseLoginCandidateRepository` (the same allowlisted `auth_internal.auth_lookup_base_login_candidates` function base-domain login already uses) and creates a token + queues an email for each match. Response body is identical either way. |
| `EfAuthRepository` normalized-email lookups | Compared `u.Email == normalizedEmail` — case/whitespace-sensitive, so a stored email like `Owner@Acme.Test` would never match a normalized `owner@acme.test` lookup. | Compares `u.NormalizedEmail` (the DB-computed `lower(trim(email))` column), matching the fix already applied to `auth_lookup_base_login_candidates` in migration `AddUsersNormalizedEmail`. |
| Reset-email link | Always `{Email:AppBaseUrl}/auth/reset-password?token=...` (flat, no tenant in the URL). | Tenant-host sends: unchanged (flat URL, `tenantSlug: null`). Base-domain sends: `{slug}.{AppBaseUrl-host}/auth/reset-password?token=...`, so the link lands on the correct tenant host and `HostTenantResolutionMiddleware` resolves the same tenant the token was issued for. |
| Dev smoke tenant owner email | `owner@acme.test` | `siyasiyamala932@gmail.com` |

## Endpoint behavior (exact)

- **Base host** (`localhost`, or any host `HostTenantResolutionMiddleware` puts in `System`/`www`/`assets` mode): normalizes the email (trim + lowercase), looks up eligible candidates via `IBaseLoginCandidateRepository.GetCandidatesAsync` (eligibility = `is_active = true AND is_deleted = false AND tenant.status IN ('Active','Trial')`, enforced inside the SECURITY DEFINER function, capped at 9 rows). For each match: invalidates that user's existing valid reset tokens, creates one new `password_reset_tokens` row (`TenantId`, `UserId` from the candidate), then fire-and-forgets one `SendPasswordResetAsync` call with that tenant's slug. Always returns `200 {"message": "If the email exists, a reset link has been sent."}` — same shape whether 0, 1, or N tenants matched.
- **Tenant host** (`{slug}.{RootDomain}`): unchanged — `RequestPasswordResetCommand` still scopes the lookup to `_tenantContext.TenantId` via `GetByTenantAndEmailAsync`, so a match in another tenant is never visible or actionable from this host.
- Both branches share the exact same controller response — the client-visible payload cannot distinguish base-vs-tenant, 0-vs-1-vs-N matches, or which branch ran.

## Multi-tenant email decision

Went with the spec's **preferred** option: one `password_reset_tokens` row and one email per eligible tenant/user, each link tenant-bound via the token's existing `TenantId` column plus a tenant-scoped URL. This required no token-model change — `PasswordResetToken.TenantId` already existed and `ResetPasswordCommandHandler` already rejects `resetToken.TenantId != _tenantContext.TenantId` (line ~60) — the only gap was that email links had no way to point at a specific tenant host. Closed that gap by adding an optional `tenantSlug` to the email-send path and building `{slug}.{host}` off the existing `Email:AppBaseUrl` config (same subdomain-of-root shape `HostTenantResolutionMiddleware` already uses for the API, applied to the app's own configured base URL rather than inventing a second convention). No response ever discloses tenant slugs/names/count; each email itself only reveals its own tenant's context to the one recipient who already owns that mailbox.

The base-login overflow-probe mechanism (`IBaseLoginFixedWorkVerifier`, the 9th-row signal) was **not** reused here — it exists purely to keep password-verification timing constant across tenant counts. Forgot-password never verifies a password, so there's nothing to keep constant-time; the handler just processes every row the candidate function returns (up to its hard 9-row cap). An account eligible in 9+ tenants under one email is an extreme edge case that would silently get only the first 9 — flagged below as a known limit, not fixed here (would need a product decision, not a bug fix).

## Seed email change

`DevSmokeTestTenantSeeder.UserEmail` → `siyasiyamala932@gmail.com`. Beyond the constant, two related idempotency fixes were needed (found and confirmed via review before landing):
1. `SeedTenantUserAsync` matched the existing row by `(TenantId, Email)`. Against a dev DB seeded under the old email, that lookup would miss and fall into the insert branch with the same hardcoded `UserId` GUID → primary-key violation → `StartAsync` rethrows → app fails to start. Now matches by `Id` (the actual stable anchor) instead.
2. `SeedGlobalEmailDirectoryAsync` used `INSERT ... ON CONFLICT DO NOTHING`, which would leave a stale directory row under the old email after a reseed. It now deletes any other-email row for the same tenant first.

Grepped the whole repo for `owner@acme.test`: every other occurrence (in `BaseDomainLoginIntegrationTests.cs`, `AuthMfaControllerTests.cs`, `EnableMfaCommandHandlerTests.cs`, `GetCurrentSessionQueryHandlerTests.cs`, `TenantLoginControllerTests.cs`, `VerifyMfaCommandHandlerTests.cs`, `TransactionalEmailPlatformKeyTests.cs`) is a test's own self-contained fixture (mocked repositories or a locally-seeded `SeedActiveUserAsync` tenant) — none reference `DevSmokeTestTenantSeeder` or its constant. Per the task instruction ("only if tied to the dev smoke tenant seed"), none of these were changed. Platform admin email was not touched.

## Test results

All run against `C:\onevoNew\HRMS-Backend-v1` on 2026-07-27.

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
  → Build succeeded, 0 Warning(s), 0 Error(s)

dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
  → Passed! Failed: 0, Passed: 872, Skipped: 0, Total: 872

dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
  → Passed! Failed: 0, Passed: 228, Skipped: 0, Total: 228

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --verbosity minimal
  → Passed! Failed: 0, Passed: 85, Skipped: 0, Total: 85  (Duration: 5m 9s, real Postgres via Testcontainers)

git diff --check
  → exit 0 (no whitespace/conflict-marker errors; only pre-existing CRLF-normalization notices)
```

Focused filters:
- `ForgotPassword|PasswordReset|BaseDomainLogin|DevSmokeTestTenantSeeder` — Unit: 10/10 passed. Architecture: 5/5 passed.
- `BaseDomainForgotPasswordIntegrationTests` (run standalone before the full suite) — 5/5 passed:
  - one eligible user → one token created, generic response, no sensitive fields in body
  - unknown email → generic response, zero tokens
  - two eligible tenants → two tokens (one per tenant), response discloses no workspace names/slugs
  - tenant-host → token created only for that tenant's user
  - tenant-host → same email in a different tenant gets **no** token

## Remaining risks / blockers

- **Email delivery itself is not asserted in integration tests** — `SendPasswordResetAsync` is fire-and-forget by design (matches the pre-existing tenant-host handler's pattern), so integration tests assert the durable side effect (`password_reset_tokens` rows) rather than racing an unawaited send. The per-candidate email call (recipient, token, slug) is asserted deterministically at the unit level instead (`BaseForgotPasswordCommandHandlerTests`), since the handler calls `SendPasswordResetAsync` synchronously before returning even though the returned `Task` isn't awaited.
- **>9 tenants under one email**: only the first 9 (per the existing `auth_lookup_base_login_candidates` cap) get a reset token/email; this mirrors an existing base-login limit and wasn't introduced by this change, but is worth a product call if it's ever expected to happen in practice.
- **Local Docker daemon**: integration tests require Docker; they were fully executed in this session (Docker Desktop was started mid-session) — not just written-but-unrun.
- **Frontend not touched**: this task was scoped to the backend only. The tenant-scoped reset link assumes a frontend capable of serving `/auth/reset-password` off a `{slug}.{root}` origin — same assumption the pre-existing tenant-host flow already makes; no new assumption was introduced.
- Unrelated pre-existing uncommitted work (an MFA setup-confirmation flow: `MFA_SETUP_CONFIRMATION_FLOW_REPORT.md`, `ConfirmMfaSetupRequest.cs`, `MfaConfirmSetup/`, etc.) was present in the working tree before this task started and was left untouched.
