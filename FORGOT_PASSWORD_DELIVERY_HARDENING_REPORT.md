# Forgot-Password Delivery Hardening Report

## Correction (RLS fix pass - base-domain 42501 violation)

This is the most recent pass. It fixes a real runtime failure that the two passes
below did not catch: `POST /api/v1/auth/forgot-password` on the base domain (e.g.
`http://localhost:5139/api/v1/auth/forgot-password`) returned a 500 caused by:

```
42501: new row violates row-level security policy for table "password_reset_tokens"
```

**Root cause:** the base-domain request starts in root/system tenant context - no
tenant is resolved yet, because `BaseForgotPasswordCommandHandler` looks up candidates
via `auth_internal.auth_lookup_base_login_candidates(...)` (a `SECURITY DEFINER`
function that runs independently of the caller's RLS session GUCs) rather than through
normal tenant-scoped repository access. The handler then went straight from that lookup
into creating `PasswordResetToken` rows and enqueueing `password_reset_email` outbox
rows - both tenant-owned tables protected by RLS (`TenantRlsInterceptor` sets
`app.current_tenant_id` / `app.tenant_context_mode` from `ITenantContext`, which was
still in `System` mode, tenant id empty) - while the request was still in that
root/system context. The restricted `onevo_app` runtime role has no RLS policy that
admits an insert with no tenant in scope, so every insert into `password_reset_tokens`
failed with 42501. `RequestPasswordResetCommandHandler` (the tenant-host handler) never
had this problem, because ASP.NET Core's `HostTenantResolutionMiddleware` has already
resolved and switched into a real tenant context by the time that handler runs.

**Fix:** `BaseForgotPasswordCommandHandler` now takes a dependency on the existing
`ITenantContextSwitcher` (the same service `SelectWorkspaceCommandHandler` already uses
to switch into a tenant after base-domain login picks a workspace) and, for each
candidate returned by the lookup, calls
`_tenantSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(candidate.TenantId,
candidate.Slug, TenantStatus.Active, null), ct)` **before** touching
`IPasswordResetTokenRepository` or `IOutboxWriter` for that candidate. Each candidate is
now a fully independent switch -> read-existing-tokens -> invalidate -> create-token ->
enqueue-email -> `SaveChangesAsync` unit, run one candidate at a time, instead of the old
shape (build every token/outbox row across every candidate up front, then call
`SaveChangesAsync` once in root context). This means:

- `SaveChangesAsync` is now called once per eligible candidate (was once for the whole
  request), because RLS only ever admits the currently-active session tenant's rows on a
  given save - a single cross-tenant save could never work correctly under RLS regardless
  of ordering.
- The candidate-overflow short-circuit (`candidateRows.Count > MaxEligibleCandidates`)
  and the zero-candidate short-circuit both still return `Result.Success()` before the
  per-candidate loop runs at all, so neither path ever calls `SwitchToTenantAsync`,
  touches `IPasswordResetTokenRepository`, calls `IOutboxWriter.EnqueueAsync`, or calls
  `SaveChangesAsync`. `BaseForgotPasswordCommandHandler_OverflowReturnsBeforeAnyTenantSwitchOrWrite`
  (new architecture test) pins the return's source position ahead of the loop.
- The public response is unchanged: still the generic 200
  `{ "message": "If the email exists, a reset link has been sent." }` in every branch.

**`TenantStatus.Active` choice, and why the DB function was not widened:**
`BaseLoginCandidateRow` (`IBaseLoginCandidateRepository.cs`) intentionally does not carry
tenant status - only `TenantId, UserId, Slug, DisplayName, PasswordHash`. Rather than add
a status column to the allowlisted `auth_internal.auth_lookup_base_login_candidates`
function (scope creep the task explicitly said to avoid unless absolutely necessary),
this pass inspected both the function migration and how the switched-to status is
actually consumed before picking a value:

- `AddAuthLookupBaseLoginCandidatesFunction`'s `Up()` SQL filters candidates with
  `t.status IN ('Active', 'Trial')` - every row this handler ever sees is therefore
  already known login-eligible by the same rule `BaseLoginFixedWorkVerifier` relies on.
- `TenantRlsInterceptor.ResolveTenantId`/`ResolveMode` (the only place the RLS session
  GUCs are set) read `ITenantContext.ContextMode` and `.TenantId` only - never `.Status`.
- `TenantContextAccessor.Resolve` stores `TenantRegistryEntry.Status` on the context, but
  a repo-wide search (`grep -rn "\.Status\b" src/ONEVO.Infrastructure/Persistence`) found
  no repository or write path that branches on `ITenantContext.Status` for anything in
  this request's path (the storefront-facing `TenantDatabaseTicketStore` checks a
  *freshly-queried* `Tenant.Status`, not the context's cached copy, and is unrelated to
  this handler).

Given the value is provably inert for every consumer on this request's path, and the
lookup function already restricts eligibility to `Active`/`Trial`, `TenantStatus.Active`
is used as a fixed placeholder rather than adding an extra `ITenantRepository` round trip
per candidate purely to look up a status nothing reads. This is documented directly in
the handler's inline comment at the `SwitchToTenantAsync` call site, not only here.

**Why RLS was not weakened:** no migration, policy, or role grant changed. The fix is
entirely about *when* the existing tenant context is switched, not about relaxing what
RLS allows. `onevo_app` still has no `BYPASSRLS`, `auth_lookup_base_login_candidates`
still returns no sensitive columns beyond what base-login already exposed, and
`ForgotPasswordHandlers_NeverUseAdminModeOrDisableRls` (new architecture test) pins that
neither forgot-password handler ever calls `SetAdminMode()`, `DisableRowLevelSecurity`,
or references `BYPASSRLS`.

**Exception handling:** no `try`/`catch` was added around the per-candidate loop. This
matches every other handler in this codebase (including the unchanged
`RequestPasswordResetCommandHandler`) - unhandled exceptions propagate to the
application's existing global exception-handling middleware rather than being swallowed
locally. An unexpected DB failure for one candidate therefore still surfaces (in tests
and in server logs, via the global handler) instead of silently producing a false-generic
200; no delivery-tradeoff was introduced by this choice, since it does not change how any
other handler in this codebase already behaves.

**Correction to this correction - `BaseDomainForgotPasswordIntegrationTests.cs` does
NOT exercise RLS:** an earlier draft of this section claimed that file "reproduce[s] the
42501 failure against the old handler." That claim was checked and is false, and is
struck through rather than left standing uncorrected. `BaseDomainForgotPasswordIntegrationTests`
runs on `BaseDomainLoginTestFactory`, which binds `ApplicationDbContext` directly to the
Testcontainers **superuser** connection string (`_postgres.GetConnectionString()`, the
`test`/`test` role Testcontainers itself creates) and never registers
`TenantRlsInterceptor`. Postgres superusers bypass RLS unconditionally, `FORCE ROW LEVEL
SECURITY` notwithstanding. This was caught empirically, not by inspection alone: with
`SwitchToTenantAsync` temporarily commented out of the handler, every
`BaseDomainForgotPasswordIntegrationTests` test still passed 6/6 - proving that file
cannot fail on 42501 regardless of whether the fix is present. This exact limitation was
already documented on a sibling class,
`TenantSessionRlsIntegrationTests` ("both [`BaseDomainLoginTestFactory`/`E2ETestFactory`]
bind `ApplicationDbContext` to the Testcontainers superuser connection and omit
`AddInterceptors(TenantRlsInterceptor)`, so RLS is invisible there"), which this pass had
initially failed to cross-reference before writing the now-corrected claim above.

**Real fix:** added `BaseForgotPasswordRlsIntegrationTests.cs`, a new test class that
mirrors `TenantSessionRlsIntegrationTests`'s technique exactly - a hand-wired,
production-like `IServiceScopeFactory` whose `ApplicationDbContext` connects as the real
`onevo_app` role (`NOSUPERUSER NOBYPASSRLS`) with `TenantRlsInterceptor` registered, and
`BaseForgotPasswordCommandHandler` resolved and invoked directly (bypassing
HTTP/`WebApplicationFactory` entirely, the same way `TenantDatabaseTicketStore` is tested
directly rather than through a controller). Seeding uses a separate admin `DbContext`
built on the raw superuser connection, matching the established pattern for setup rows
that must bypass RLS on purpose. This is the one place in the suite that actually proves
the 42501 fix:

- With the fix reverted (`SwitchToTenantAsync` call removed), this new suite's
  one-candidate and multi-tenant tests fail with the real error:
  `Npgsql.PostgresException (0x80004005): 42501: new row violates row-level security
  policy for table "password_reset_tokens"`, `Routine: ExecWithCheckOptions` - observed
  directly in a test run before the fix was restored, not asserted from theory.
- With the fix restored, all three tests pass: one eligible candidate (token + outbox row
  created, no exception), two same-email tenants (one token + one outbox row per tenant,
  no cross-tenant RLS interference from the first switch still being active when the
  second candidate's insert runs), and nine candidates (overflow - correctly still no
  writes, since overflow returns before the loop that switches tenant context at all,
  same behavior with or without RLS enforced).

`BaseDomainForgotPasswordIntegrationTests.cs` was left unchanged (per the task's existing
acceptance list) and still exercises the full HTTP stack end-to-end - genuinely useful
for proving routing, host resolution, response shape, and payload correctness - it simply
does not, and per its factory's design cannot, prove the RLS fix itself. That proof now
lives only in `BaseForgotPasswordRlsIntegrationTests.cs`.

**Tests added/updated in this pass:**
- Unit (`BaseForgotPasswordCommandHandlerTests.cs`): rewrote the one/multiple-candidate
  tests to assert `ITenantContextSwitcher.SwitchToTenantAsync` is called once per
  candidate, with an ordered-call-log assertion proving Switch -> List -> Add -> Enqueue
  -> Save happens once per tenant (not batched); the multi-candidate test now asserts
  `SaveChangesAsync` `Times.Exactly(2)`, not `Times.Once`; the 0-candidate and 9-candidate
  (overflow) tests now also assert `SwitchToTenantAsync` is never called.
- Integration (`BaseDomainForgotPasswordIntegrationTests.cs`): unchanged file. Still run
  against a real Postgres container over HTTP, but - see correction above - its factory
  connects as the Testcontainers superuser and does not exercise RLS, so it proves
  routing/response-shape/payload correctness, not the RLS fix.
- Integration (`BaseForgotPasswordRlsIntegrationTests.cs`, new): the test that actually
  proves the RLS fix - see "Real fix" above. Three tests: one candidate, multiple
  same-email tenants, nine-candidate overflow, all invoking the handler directly under a
  real `onevo_app`-connected, RLS-interceptor-wired `ApplicationDbContext`.
- Architecture (`ForgotPasswordDeliveryArchitectureTests.cs`): four new guards -
  `BaseForgotPasswordCommandHandler_SwitchesTenantContextBeforeAnyTenantOwnedWrite`
  (source-position check: `SwitchToTenantAsync` precedes `ListValidByUserIdAsync`,
  `AddAsync`, `EnqueueAsync`, and `SaveChangesAsync` inside the per-candidate loop),
  `BaseForgotPasswordCommandHandler_OverflowReturnsBeforeAnyTenantSwitchOrWrite`,
  `ForgotPasswordHandlers_NeverUseAdminModeOrDisableRls`, and
  `ForgotPasswordHandlers_NeverCallSendPasswordResetAsyncDirectly`.

**Verification (this pass):**

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
  -> Build succeeded, 0 Warning(s), 0 Error(s)

dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --filter "BaseForgotPassword|RequestPasswordReset|PasswordReset" --verbosity minimal
  -> Passed! Failed: 0, Passed: 19, Skipped: 0, Total: 19

dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 884, Skipped: 0, Total: 884

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "BaseDomainForgotPasswordIntegrationTests" --verbosity minimal
  -> Passed! Failed: 0, Passed: 6, Skipped: 0, Total: 6
  (proves routing/response-shape/payload correctness only - see correction above; does
   not exercise RLS)

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "BaseForgotPasswordRlsIntegrationTests" --verbosity minimal
  -- with SwitchToTenantAsync temporarily removed (RED, observed before restoring the fix):
  -> Failed! Failed: 2, Passed: 1, Total: 3
     Handle_OneEligibleCandidate...: Npgsql.PostgresException 42501: new row violates row-level
       security policy for table "password_reset_tokens" (Routine: ExecWithCheckOptions)
     Handle_MultipleEligibleTenants...: same 42501
     Handle_NineCandidates...Overflow...: Passed (overflow never touches tenant context either way)
  -- with the fix restored (GREEN):
  -> Passed! Failed: 0, Passed: 3, Skipped: 0, Total: 3

dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 241, Skipped: 0, Total: 241

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 93, Skipped: 0, Total: 93, Duration: 6m 1s
     (90 pre-existing + 3 new BaseForgotPasswordRlsIntegrationTests)

rg -n "DisableRowLevelSecurity|BYPASSRLS|SetAdminMode|admin mode|SendPasswordResetAsync" src\ONEVO.Application\Features\Auth\Login\Commands\BaseForgotPassword src\ONEVO.Application\Features\Auth\Login\Commands\RequestPasswordReset tests\ONEVO.Tests.Architecture
  -> matches only inside pre-existing/new architecture-test guard source (the string
    literals the guards search for) and unrelated DevSmokeTestTenantSeeder/AuthContract
    architecture tests; zero matches inside the two handler files themselves

rg -n -P "[^\x00-\x7F]" src\ONEVO.Application\Features\Auth\Login\Commands\BaseForgotPassword src\ONEVO.Application\Features\Auth\Login\Commands\RequestPasswordReset FORGOT_PASSWORD_DELIVERY_HARDENING_REPORT.md
  -> no matches (all ASCII-only)

git diff --check
  -> exit 0, no whitespace/conflict-marker errors (only pre-existing LF->CRLF autocrlf
    advisories from git itself, on files this pass did not touch, unrelated to content)
```

This correction only touches `BaseForgotPasswordCommandHandler.cs`,
`BaseForgotPasswordCommandHandlerTests.cs`, and
`ForgotPasswordDeliveryArchitectureTests.cs`. `RequestPasswordResetCommandHandler` (the
tenant-host handler) was already correct and is unchanged by this pass.

## Correction (follow-up pass)

The original version of this report (see table and limitations below) shipped
`RequestPasswordResetCommandHandler` enqueueing `PasswordResetEmailPayload(..., TenantSlug:
null)` for tenant-host forgot-password and called this "unchanged behavior, out of
scope." That was wrong, not merely incomplete:

- **Why it was wrong:** `EmailTemplateRenderer.RenderPasswordReset` only builds the
  tenant-bound `https://{tenantSlug}.{appHost}/auth/reset-password?token={token}` link
  when `TenantSlug` is present in the payload; with `TenantSlug: null` it falls back to
  the un-prefixed base-host URL. But `ResetPasswordCommandHandler` requires a resolved
  tenant context before it will accept a reset token - a token issued from a tenant host
  therefore produced an email whose link pointed at a host that can never redeem it.
  Tenant-host forgot-password was silently sending broken reset links.
- **Fix:** `RequestPasswordResetCommandHandler` now reads `ITenantContext.Slug` (the
  same resolved-tenant slug the middleware already populated via
  `TenantContextAccessor.Resolve`) and passes it straight into the payload instead of a
  hardcoded `null`. No tenant repository query was added - `ITenantContext` already
  exposes the slug that was resolved for the current request.
- **Fail-closed on a malformed context:** a tenant context can report `IsResolved` and
  `ContextMode == Tenant` while `Slug` is null/blank only if tenant resolution is
  internally inconsistent. Rather than issue a token whose email would still be
  unusable, the handler now returns `Result.Success()` immediately in that case - the
  same generic response as an unknown/inactive user - without querying the user
  repository, creating a token, enqueueing an outbox row, or calling
  `SaveChangesAsync`.
- **Base-domain unaffected:** `BaseForgotPasswordCommandHandler` already passed the
  candidate's real `Slug` (see Fix C / the table below) and required no change.

This correction only touches `RequestPasswordResetCommandHandler` and its tests; the
outbox/transactional-write model, `BaseForgotPasswordCommandHandler`, and MFA logic
described in the rest of this report are unchanged.

## Summary

Forgot-password (both tenant-host and base-domain) now creates its password reset
token(s) and enqueues the reset email through the existing transactional outbox in the
same database transaction, instead of firing an unawaited `IEmailService` call. The
base-domain handler now has an explicit, tested overflow rule for the
`auth_lookup_base_login_candidates` 9-row probe contract. A stale MFA controller comment
was corrected. No reset-password token validation semantics, MFA verification logic, or
the generic forgot-password client response were changed.

## Files changed

**New:**
- `src/ONEVO.Application/Features/Auth/Login/OutboxHandlers/PasswordResetEmailOutboxHandler.cs`
  - `PasswordResetEmailPayload` record + `IOutboxMessageHandler` that calls
  `IEmailService.SendPasswordResetAsync` from the outbox worker.
- `tests/ONEVO.Tests.Unit/Features/Auth/RequestPasswordResetCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Architecture/ForgotPasswordDeliveryArchitectureTests.cs`

**Modified:**
- `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs` -
  added `OutboxMessageTypes.PasswordResetEmail`.
- `src/ONEVO.Application/DependencyInjection.cs` - registered
  `PasswordResetEmailOutboxHandler`.
- `src/ONEVO.Application/Features/Auth/Login/Commands/BaseForgotPassword/BaseForgotPasswordCommandHandler.cs`
  - outbox delivery, explicit `candidateRows.Count > 8` overflow branch, safe hashed-email
  warning log, `ILogger` dependency added.
- `src/ONEVO.Application/Features/Auth/Login/Commands/RequestPasswordReset/RequestPasswordResetCommandHandler.cs`
  - outbox delivery, enqueue moved before the single `SaveChangesAsync`.
- `src/ONEVO.Api/Controllers/Tenant/Auth/AuthMfaController.cs` - corrected the
  `mfa/verify` XML doc comment (no logic change).
- `tests/ONEVO.Tests.Unit/Features/Auth/BaseForgotPasswordCommandHandlerTests.cs` -
  swapped the `IEmailService` mock for `IOutboxWriter`, added overflow and
  no-plaintext-email-in-log tests.
- `tests/ONEVO.Tests.Integration/Auth/BaseDomainForgotPasswordIntegrationTests.cs` -
  added outbox-payload assertions to existing tests, added a 9-tenant overflow test and
  a tenant-slug-binding assertion.

**New (this report):**
- `FORGOT_PASSWORD_DELIVERY_HARDENING_REPORT.md`

## Fix A: Delivery reliability - chosen model and reason

**Chosen: existing outbox/email pipeline (preferred implementation), not the
await-and-catch fallback.**

Both handlers now:
1. Create/invalidate `password_reset_token` row(s) in the `ApplicationDbContext`.
2. Call `IOutboxWriter.EnqueueAsync(OutboxMessageTypes.PasswordResetEmail, payload, tenantId, ct)`,
   which only adds an `OutboxMessage` row to the same tracked `DbContext` - it does not
   call `SaveChanges` itself (`src/ONEVO.Infrastructure/Services/SharedPlatform/Outbox/OutboxWriter.cs`).
3. Call `IUnitOfWork.SaveChangesAsync(ct)` **once**. `UnitOfWork` wraps the same
   `ApplicationDbContext` instance (`src/ONEVO.Infrastructure/Persistence/UnitOfWork.cs`),
   so the token row(s) and the outbox row(s) commit in a single transaction - a token can
   never exist without a durable, retryable email job, and vice versa.
4. `OutboxProcessor` (`BackgroundService`, `src/ONEVO.Infrastructure/Services/SharedPlatform/Outbox/OutboxProcessor.cs`)
   picks up pending rows and retries on failure with exponential backoff (up to 8 attempts,
   30s-1h), exactly like the pre-existing `tenant_owner_invite_email` message type.

This mirrors the tenant-owner-invite flow (`TenantOwnerInvitationService.QueueInviteEmailAsync`
+ `TenantOwnerInviteEmailOutboxHandler`) exactly, so it reuses an already-proven pattern
rather than inventing a new one.

**Raw token in the outbox payload:** the payload
(`PasswordResetEmailPayload(TenantId, UserId, Email, RawToken, TenantSlug)`) carries the
one-time plaintext reset token, the same way `TenantOwnerInviteEmailPayload` carries the
plaintext invite token today. `OutboxWriter.EnqueueAsync` AES-encrypts the serialized
JSON before it is persisted (`EncryptedPayload` column; see `OutboxMessageConfiguration`
and `AesEncryptionService`), and `OutboxProcessor` decrypts it in-memory only at dispatch
time. This is the existing security posture for one-time tokens in this outbox - not a
new or weaker design - so no blocker was raised. The reset token is still never logged
(see Fix B / architecture tests) and never appears in the HTTP response.

**Client response:** unchanged generic 200
`{ "message": "If the email exists, a reset link has been sent." }` regardless of
whether the outbox enqueue happened, how many tenants matched, or whether overflow
triggered. If no email provider is configured, `OutboxProcessor` will retry and
eventually mark the message `failed` after 8 attempts - this never surfaces to the
client and is only visible in `outbox_messages.last_error` / server logs.

**Remaining limitation:** this hardens *creation -> durable job* atomicity and adds
provider-outage retry. It does not add an idempotency guard against a user submitting
the forgot-password form twice in quick succession (each submission still invalidates
prior tokens and issues a new one/enqueues a new email, as before) - that was out of
scope for this task and reset-password token semantics were intentionally left
unchanged.

## Fix B: Base-domain overflow rule

`BaseForgotPasswordCommandHandler` now checks `candidateRows.Count > MaxEligibleCandidates`
(`MaxEligibleCandidates = 8`) **before any token or outbox work begins**, mirroring the
`BaseLoginFixedWorkVerifier`'s existing `SupportedMaximumCandidates = 8` treatment of the
9th row as a pure overflow probe, never a real candidate to serve.

On overflow:
- No `PasswordResetToken` rows are created (existing valid tokens for any of the 9 rows'
  users are also left untouched - `ListValidByUserIdAsync` is never even called).
- No outbox rows are enqueued.
- `SaveChangesAsync` is never called.
- The handler still returns `Result.Success()`, so the controller's response is the
  identical generic 200 as the 0-candidate and N-candidate (<=8) paths.
- A single safe warning is logged:
  `"Base-domain forgot-password candidate overflow for normalized email hash {EmailHash}."`
  where `{EmailHash}` is `Convert.ToHexString(SHA256.HashData(UTF8(normalizedEmail)))` -
  never the plaintext email. This fingerprint is only ever written to server logs; it is
  not returned to the client.

Guarded by:
- Unit tests: 9-candidate overflow (no tokens/no enqueue/no save/success) and a dedicated
  no-plaintext-email-in-log test (`BaseForgotPasswordCommandHandlerTests.cs`).
- Integration test: 9 real seeded tenants sharing one email against the real
  `auth_lookup_base_login_candidates` Postgres function returns generic 200 with zero
  token rows and zero outbox rows (`BaseDomainForgotPasswordIntegrationTests.cs`).
- Architecture test: source-scans that the overflow branch exists, returns
  `Result.Success()`, and contains neither `AddAsync` nor `EnqueueAsync`
  (`ForgotPasswordDeliveryArchitectureTests.cs`).

## Fix C: Token/email recipient correctness

Reviewed and **kept sending to `normalizedEmail`** for the base-domain handler - this was
a deliberate decision, not an oversight:

`IBaseLoginCandidateRepository` / `BaseLoginCandidateRow` intentionally does not expose
the user's email (only `TenantId, UserId, Slug, DisplayName, PasswordHash`). Its doc
comment states implementations "must never query users/tenants directly" beyond the
allowlisted `auth_lookup_base_login_candidates` function, which was deliberately scoped
to return only what base-domain login needs. Widening that function's output to add
`email` - when the caller already supplied and normalized the exact email that produced
this candidate row via `normalized_email` matching - would be adding unnecessary data to
a SECURITY DEFINER function's surface for no behavioral gain. Per the task's own
guidance, this was **not** done without explicit approval. `normalizedEmail` is
send-to-correct by construction (it is the value that matched `normalized_email` for
every returned row) and is documented as such directly in the handler's XML doc comment
and inline comments.

The tenant-host handler (`RequestPasswordResetCommandHandler`) already had the real
`user.Email` available from `IUserRepository.GetByTenantAndEmailAsync` and continues to
use it unchanged.

## Tenant-host vs base-domain behavior

| | Tenant-host (`RequestPasswordResetCommandHandler`) | Base-domain (`BaseForgotPasswordCommandHandler`) |
|---|---|---|
| Candidate source | `IUserRepository.GetByTenantAndEmailAsync(_tenantContext.TenantId, ...)` | `IBaseLoginCandidateRepository.GetCandidatesAsync` (allowlisted function only) |
| Cross-tenant leakage | None - scoped to the resolved tenant only | One user/email can match multiple tenants; each gets its own token + email |
| Overflow handling | N/A (single-tenant lookup has no overflow concept) | `Count > 8` -> full no-op, generic success |
| Email recipient | `user.Email` (real row) | `normalizedEmail` (see Fix C) |
| `TenantSlug` in payload | `ITenantContext.Slug` (link is tenant-bound; see Correction above) | Seeded tenant's `Slug` (link is tenant-bound) |
| Delivery model | Outbox, same as base-domain | Outbox |

## Exact reset-link host behavior

`EmailTemplateRenderer.RenderPasswordReset` / `ApplyTenantSlug`
(`src/ONEVO.Infrastructure/ExternalServices/Email/EmailTemplateRenderer.cs`) builds
`https://{tenantSlug}.{appHost}/auth/reset-password?token={token}` when a `TenantSlug` is
present in the payload, and falls back to the un-prefixed `Email:AppBaseUrl` host when it
is not. Base-domain forgot-password supplies the candidate's `Slug` in the outbox
payload (was previously only available at send-time via the old direct
`SendPasswordResetAsync(email, token, tenantSlug, ct)` call - behavior is preserved, just
relocated one layer). Tenant-host forgot-password now also supplies a slug
(`ITenantContext.Slug`) instead of `null` - see Correction above; this was the bug fixed
in this pass.

## Fix D: Stale MFA comment cleanup

`AuthMfaController.VerifyMfa`'s XML doc comment changed from
`"Verify TOTP code to complete MFA challenge or finish MFA setup."` to
`"Verify TOTP code for a login MFA challenge."` No route, authorization, or handler logic
was touched. `mfa/verify` still calls `IUserMfaRepository.GetTotpAsync(user.Id, isVerified: true, ...)`
and never loads `isVerified: false` records; `ConfirmMfaSetupCommandHandler` remains the
only place that sets `IsVerified = true` on a pending TOTP setup. Both are now guarded by
`ForgotPasswordDeliveryArchitectureTests`.

## Tests added/updated

**Unit - `BaseForgotPasswordCommandHandlerTests.cs`:**
- 0 candidates -> no token, no enqueue, no save (updated for `IOutboxWriter`).
- 1 candidate -> one token + one tenant-bound outbox enqueue (updated).
- 2 candidates -> one token + one enqueue per tenant (updated).
- Existing valid tokens invalidated before issuing new ones (unchanged, still non-overflow only).
- **New:** 9 candidates -> overflow: no `AddAsync`, no `ListValidByUserIdAsync` call, no
  `SaveChangesAsync`, no enqueue, `Result.Success()`.
- **New:** overflow warning log never contains the plaintext email.

**Unit - `RequestPasswordResetCommandHandlerTests.cs`:**
- Existing active user -> token created + outbox enqueue with payload `TenantSlug`
  equal to the resolved tenant's slug (updated from asserting `null`).
- Unknown user -> success, no token, no enqueue.
- Inactive user -> success, no token, no enqueue.
- Unresolved/non-tenant context -> success, no repository call, no enqueue.
- **New:** tenant context resolved but `Slug` is `null`/empty/whitespace (`[Theory]`,
  3 cases) -> success, no `AddAsync`, no `SaveChangesAsync`, no outbox enqueue -
  proves the fail-closed path added in the Correction pass.

**Integration - `BaseDomainForgotPasswordIntegrationTests.cs`:**
- One eligible user -> token + decrypted outbox payload assertions (`TenantId`, `Email`,
  `TenantSlug` matching the seeded tenant - proves the link will be tenant-bound).
- Unknown email -> generic 200, no token, no outbox row.
- Multiple eligible tenants -> one token + one outbox payload per tenant, each with its
  own `TenantSlug`.
- **New:** 9 eligible tenants (real Postgres `auth_lookup_base_login_candidates` call) ->
  generic 200, zero tokens, zero outbox rows.
- Tenant-host forgot-password -> token + outbox payload only for the resolved tenant,
  with the decrypted payload's `TenantSlug` equal to the resolved tenant's slug
  (updated in the Correction pass - this is the assertion that proves tenant-host reset
  links are tenant-bound too).
- Tenant-host same email in another tenant -> untouched (unchanged test).

**Architecture - `ForgotPasswordDeliveryArchitectureTests.cs` (new file):**
- Forgot-password response body contains only the generic `message` field (no
  `tenant_id`/`user_id`/`workspace`/`token_hash`/`password_hash`/`reset_token`).
- `BaseForgotPasswordCommandHandler` has an explicit overflow branch that creates no
  tokens and enqueues no emails.
- Neither forgot-password handler logs the raw reset token or a plaintext email.
- `PasswordResetEmailOutboxHandler` never logs anything (no `_logger` at all).
- `AuthMfaController`'s `mfa/verify` comment no longer claims to finish setup.
- `mfa/verify` still only loads `isVerified: true` TOTP records.
- `ConfirmMfaSetupCommandHandler` remains the only place `IsVerified = true` is set.

## Verification results

Original pass (before this Correction):

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
  -> Build succeeded, 0 Warning(s), 0 Error(s)

dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 878, Skipped: 0, Total: 878

dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 235, Skipped: 0, Total: 235

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "ForgotPassword|PasswordReset" --verbosity minimal
  -> Passed! Failed: 0, Passed: 6, Skipped: 0, Total: 6

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 86, Skipped: 0, Total: 86, Duration: 5m 30s

git diff --check
  -> exit 0, no whitespace/conflict-marker errors (only pre-existing LF->CRLF autocrlf
    advisories from git itself, unrelated to content)
```

Correction pass (this update - tenant-host `TenantSlug` fix + ASCII cleanup):

```
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --no-restore --verbosity minimal
  -> Build succeeded, 0 Warning(s), 0 Error(s)

dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --filter "RequestPasswordReset|BaseForgotPassword|PasswordReset" --verbosity minimal
  -> Passed! Failed: 0, Passed: 19, Skipped: 0, Total: 19

dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 884, Skipped: 0, Total: 884

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --filter "BaseDomainForgotPasswordIntegrationTests" --verbosity minimal
  -> Passed! Failed: 0, Passed: 6, Skipped: 0, Total: 6

dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 90, Skipped: 0, Total: 90, Duration: 7m 51s

dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal
  -> Passed! Failed: 0, Passed: 237, Skipped: 0, Total: 237

rg -n -P "[^\x00-\x7F]" src\ONEVO.Application\Features\Auth\Login\Commands\RequestPasswordReset\RequestPasswordResetCommandHandler.cs FORGOT_PASSWORD_DELIVERY_HARDENING_REPORT.md MFA_SETUP_CONFIRMATION_FLOW_REPORT.md
  -> no matches (all three files are ASCII-only)

git diff --check
  -> exit 0, no whitespace/conflict-marker errors (only pre-existing LF->CRLF autocrlf
    advisories from git itself, unrelated to content)
```

## Remaining limitations / blockers

- No blocker was hit: the outbox's existing encrypted-at-rest handling of one-time
  plaintext tokens (already used for tenant-owner invites) was judged acceptable for
  reset tokens too, per the task's own fallback criteria.
- No double-submit idempotency guard was added for forgot-password (out of scope; each
  submission still invalidates prior tokens and issues a fresh one, same as before this
  task).
- (Resolved in the Correction pass above) Tenant-host forgot-password's reset link is
  now tenant-slug-bound via `ITenantContext.Slug`; a resolved-but-slug-missing tenant
  context fails closed to the generic success response with no token/outbox side
  effects.
- `IEmailService` provider configuration/availability was not exercised end-to-end in
  automated tests (no real provider is configured in the integration test environment);
  delivery durability is verified via the outbox row/payload contract instead, consistent
  with how the existing `tenant_owner_invite_email` type is tested in this repo (no
  existing integration coverage decrypts and asserts on its payload either - this task
  adds the first such assertions for the password-reset message type).
