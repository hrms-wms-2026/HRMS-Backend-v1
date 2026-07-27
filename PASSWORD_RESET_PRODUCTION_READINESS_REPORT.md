# Password Reset Production Readiness Report

Scope: HRMS-Backend-v1 only. This report covers the remaining production-readiness gaps in the
password-reset flow (password policy validation, atomic token consumption, delivery/rate-limit
confirmation). It does not redo the base-domain forgot-password RLS fix, which was already
completed and verified with restricted-role HTTP integration tests in a prior session.

## Files changed

New files:

- `src/ONEVO.Application/Features/Auth/Login/Validation/PasswordPolicy.cs`
- `src/ONEVO.Application/Features/Auth/Login/Commands/ResetPassword/ResetPasswordCommandValidator.cs`
- `src/ONEVO.Application/Features/Auth/Login/Commands/ForcePasswordChange/ForcePasswordChangeCommandValidator.cs`
- `tests/ONEVO.Tests.Unit/Features/Auth/ResetPasswordCommandValidatorTests.cs`
- `tests/ONEVO.Tests.Unit/Features/Auth/ForcePasswordChangeCommandValidatorTests.cs`
- `tests/ONEVO.Tests.Unit/Features/Auth/PasswordResetValidationPipelineTests.cs`
- `tests/ONEVO.Tests.Unit/Features/Auth/ResetPasswordCommandHandlerTests.cs`
- `tests/ONEVO.Tests.Architecture/PasswordResetHardeningArchitectureTests.cs`
- `tests/ONEVO.Tests.Integration/Auth/PasswordResetTokenRepositoryConcurrencyTests.cs`
- `tests/ONEVO.Tests.Integration/Auth/TenantHostPasswordResetFlowIntegrationTests.cs`

Modified files:

- `src/ONEVO.Application/Common/RepositoryInterfaces/IUnitOfWork.cs` - added `ExecuteInTransactionAsync`.
- `src/ONEVO.Infrastructure/Persistence/UnitOfWork.cs` - implemented `ExecuteInTransactionAsync`.
- `src/ONEVO.Application/Features/Auth/Login/RepositoryInterfaces/IPasswordResetTokenRepository.cs` - added `TryConsumeResetTokenAsync`.
- `src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs` - implemented `TryConsumeResetTokenAsync`.
- `src/ONEVO.Application/Features/Auth/Login/Commands/ResetPassword/ResetPasswordCommandHandler.cs` - rewritten around atomic consumption + one transaction.
- `src/ONEVO.Application/Features/Auth/Invite/Commands/AcceptInvitationPassword/AcceptInvitationPasswordCommandValidator.cs` - refactored to use the shared `PasswordPolicy` helper (no behavior change).
- `tests/ONEVO.Tests.Unit/Fakes/FakeUnitOfWork.cs` - added `ExecuteInTransactionAsync` to keep the fake implementing `IUnitOfWork`.

No other files in the working tree were touched by this task. The repository has other unrelated
uncommitted work in progress (MFA setup confirmation, legal-pending CSRF fix, etc., visible in
`git status`); none of it was created or modified as part of this task and none of it was reviewed
or altered here.

## Root problems fixed

1. **No password policy on reset/force-change.** `ResetPasswordCommand` and
   `ForcePasswordChangeCommand` had no `FluentValidation` validator at all, so `ValidationBehavior`
   (registered globally in `ONEVO.Application.DependencyInjection`) silently allowed any
   `NewPassword`, including an empty string, once the request reached the handler.
2. **Non-atomic token consumption.** `ResetPasswordCommandHandler` used to `GetResetTokenByHashAsync`
   (read), check `IsValid` in application code, then set `resetToken.UsedAt` and rely on a later
   `SaveChangesAsync`. Two concurrent requests could both read the token as valid before either
   write landed, letting both proceed.
3. **No shared password policy source of truth.** Invite signup's rules
   (`NotEmpty` + `MinimumLength(8)`) lived only inline in `AcceptInvitationPasswordCommandValidator`
   with no way to reuse them.

## Password policy validation

**Source of truth:** `PasswordPolicy.ApplyPasswordPolicy<T>()`
(`src/ONEVO.Application/Features/Auth/Login/Validation/PasswordPolicy.cs`), a `FluentValidation`
rule-builder extension applying `NotEmpty()` + `MinimumLength(8)`.

This extracts, verbatim, the rule set that already existed in
`AcceptInvitationPasswordCommandValidator` before this change - it does not add uppercase, digit,
or symbol complexity requirements. That was a deliberate scope decision, not an oversight: the
existing invite-signup policy has no complexity rules, and unilaterally adding them to
reset/force-change while leaving invite signup at the old (weaker) bar would only create
inconsistency between the three password entry points the task asked to align. If the product wants
complexity rules (uppercase/lowercase/digit/symbol), that should be a separate, explicitly-scoped
change applied to `PasswordPolicy` once, so all three signup/reset/force-change paths tighten
together.

Applied to:

- `AcceptInvitationPasswordCommandValidator` (refactored to call the shared helper - same rules as before).
- `ResetPasswordCommandValidator` (new) - `Token` required, `NewPassword` via the shared policy.
- `ForcePasswordChangeCommandValidator` (new) - `Email`/`CurrentPassword` required, `NewPassword` via
  the shared policy, plus `NotEqual(x => x.CurrentPassword)` so a force-change cannot "change" the
  password to the same value.

**Confirmed wired into the pipeline, not just present:** `PasswordResetValidationPipelineTests`
builds a `ServiceCollection` with the same `AddValidatorsFromAssembly` call
`ONEVO.Application.DependencyInjection.AddApplication` uses in production, and asserts
`IValidator<ResetPasswordCommand>` and `IValidator<ForcePasswordChangeCommand>` resolve to the new
validator types. Combined with `ValidationBehavior` already being registered globally for every
MediatR request (unchanged, pre-existing), this proves the validators are not dead code - any
invalid request now throws `ValidationException` before the handler runs.

No API request/response body shapes were changed.

## Atomic reset-token consumption design

`EfAuthRepository.TryConsumeResetTokenAsync(tokenHash, tenantId, now, ct)`:

```sql
UPDATE password_reset_tokens
SET used_at = @now
WHERE token_hash = @tokenHash
  AND tenant_id = @tenantId
  AND used_at IS NULL
  AND expires_at > @now
```

executed via `Database.ExecuteSqlInterpolatedAsync` (FormattableString interpolation -> Npgsql
parameters; no string concatenation of user-controlled values). If the affected-row count is not
exactly 1, the method returns `null`. Otherwise it reads back the row's `UserId` (a second query on
the same connection/transaction) and returns it.

This is a single UPDATE statement with a `used_at IS NULL` guard, so under PostgreSQL's row-level
locking, at most one concurrent transaction's WHERE clause can match before the row is committed;
the loser's UPDATE affects zero rows. This is proven directly against real PostgreSQL (not SQLite -
see "Why not SQLite" below) in
`PasswordResetTokenRepositoryConcurrencyTests.TryConsumeResetTokenAsync_ParallelConsume_AllowsExactlyOneWinner`:
8 truly parallel callers (separate `ApplicationDbContext`/connections) attempt to consume the same
token; exactly one gets a non-null result, and the persisted row has `used_at` set.

Note on what exactly that proof covers: the concurrency test calls `TryConsumeResetTokenAsync`
directly, where the UPDATE runs in its own autocommitted statement. In production the same call runs
inside `ExecuteInTransactionAsync` (see below), so the row lock is held for the rest of the
transaction rather than released immediately after the UPDATE. The exactly-one-winner guarantee is
unaffected by this difference - it comes from PostgreSQL re-evaluating the `used_at IS NULL` WHERE
clause against the committed row once the lock is released, regardless of whether that release
happens at statement end or transaction end - but the production path holds the lock longer, which
is the correct tradeoff discussed under "Known residual limitation" and the BCrypt-hashing note
below.

**Transaction boundary:** `IUnitOfWork.ExecuteInTransactionAsync<TResult>` (new) wraps a delegate in
an explicit `BeginTransactionAsync`/`CommitAsync` pair, using `CreateExecutionStrategy().ExecuteAsync`
for retry-safety - the same pattern already used elsewhere in this codebase
(`EfAuthRepository.GetByKeyHashForTenantResolutionAsync`). `ResetPasswordCommandHandler.Handle` now
runs its entire body - consume token, load user, hash and set the new password, revoke refresh
tokens, increment the permission version, `SaveChangesAsync` - inside one call to
`ExecuteInTransactionAsync`. If anything in that delegate throws, the transaction rolls back and the
token consumption is undone (the token remains usable). If the delegate returns normally (success or
failure), the transaction commits.

**Documented tradeoff - user missing/inactive after consumption:** if `TryConsumeResetTokenAsync`
succeeds but the user is then found to be missing or inactive, the handler returns the same generic
`"Invalid or expired reset token."` failure *without throwing*, so the surrounding transaction still
commits and the token is burned even though no password change happened. This is intentional: an
inactive/deleted user cannot use the token anyway, and refusing to burn it would let the same token
be probed repeatedly. `ResetPasswordCommandHandlerTests.Handle_UserMissingAfterConsumption_BurnsTokenAndReturnsGenericError`
and the inactive-user counterpart assert this directly (token-consume call happens exactly once,
`SaveChangesAsync` is never called, and the caller-visible error is the generic one).

**Known residual limitation (documented, not engineered around):** `CreateExecutionStrategy().ExecuteAsync`
can, in rare cases, retry the delegate after a commit that was acknowledged-then-lost at the network
layer. If that happens here, the retry's `TryConsumeResetTokenAsync` call would find `used_at`
already set (from the commit that actually succeeded) and return the generic invalid-token error to
a user whose password *did* change. This is an inherent tradeoff of combining EF Core's
retrying execution strategy with a single-attempt atomic consume, not something introduced by this
change; it is called out here rather than silently accepted.

**Why not SQLite for the repository-level correctness tests:** an initial attempt used the existing
SQLite in-memory pattern (`EfAuthRepositorySupportCoreTests`-style). It failed: raw-SQL-interpolated
`DateTimeOffset` parameters are bound differently by `Microsoft.Data.Sqlite` than the value EF's
SQLite column converter stores, so `expires_at > @now` matched zero rows even for tokens that were
provably valid via the equivalent LINQ query. This is a SQLite ADO parameter-binding quirk, not a
defect in the production code path - the same assertions pass cleanly against real PostgreSQL (see
`PasswordResetTokenRepositoryConcurrencyTests`), which is the only database engine this code
actually runs against in production. The SQLite attempt was deleted rather than worked around, and
all `TryConsumeResetTokenAsync` correctness/concurrency proof now lives exclusively in the
Postgres/Testcontainers integration suite.

`ResetPasswordCommandHandler` was updated to:

1. Require `_tenantContext.IsResolved && ContextMode == Tenant`, same as before.
2. Hash the incoming raw token.
3. Call `TryConsumeResetTokenAsync(tokenHash, tenantId, now, ct)` inside the transaction.
4. On `null`, return the generic invalid/expired error (nothing was written).
5. On success, load the user; if missing/inactive, return the generic error (token still burned, see
   above).
6. Hash and set the new password, clear `MustChangePassword`/`PasswordSetByAdmin`/
   `TemporaryPasswordExpiresAt`, revoke all active refresh tokens, increment the permission version,
   `SaveChangesAsync`.
7. Commit.

## Tenant-host / base-domain compatibility

No changes were made to `BaseForgotPasswordCommandHandler`, `RequestPasswordResetCommandHandler`, or
`AuthPasswordController`. `TenantHostPasswordResetFlowIntegrationTests` proves, against real
PostgreSQL with the real `onevo_app` role and `TenantRlsInterceptor`:

- Tenant-host forgot-password -> outbox -> reset-password succeeds, and the same token cannot be
  reused a second time (generic failure).
- An expired token fails generically.
- A token issued for tenant A fails generically when the reset-password request resolves tenant B's
  context (the raw token is the same string; only the resolved tenant differs, simulating a
  different host).
- A base-domain-issued token (via `BaseForgotPasswordCommandHandler`, unresolved -> switched-into
  tenant context) is later successfully consumed by `ResetPasswordCommandHandler` running under that
  tenant's resolved context - proving the two token-issuing paths and the one
  token-consuming path are compatible.

The forgot-password response body is unchanged:
`{ "message": "If the email exists, a reset link has been sent." }`. Reset-password now returns
exactly one generic error string, `"Invalid or expired reset token."`, on every failure path -
enforced by `PasswordResetHardeningArchitectureTests.ResetPasswordCommandHandler_EveryFailureReturnsTheSameGenericErrorLiteral`
(source-scans every `Result.Failure(...)` call in the handler and asserts they all reference the
same identifier).

## RLS / security confirmation

- No `SetAdminMode`, `BYPASSRLS`, or `DisableRowLevelSecurity` calls anywhere in
  `ResetPasswordCommandHandler` or `ForcePasswordChangeCommandHandler`
  (`PasswordResetHardeningArchitectureTests.PasswordResetHandlers_NeverUseAdminModeOrDisableRls`).
- `TryConsumeResetTokenAsync`'s raw UPDATE runs on the same `ApplicationDbContext`/connection as
  everything else in the request; `TenantRlsInterceptor` sets `app.current_tenant_id`/
  `app.tenant_context_mode` session GUCs once per connection-open (session-scoped, not
  transaction-local), so the RLS policy on `password_reset_tokens`
  (`tenant_isolation`, `FOR ALL`, from `AddRlsPolicies`) applies to the raw UPDATE exactly as it
  applies to any other write on that connection.
- Tenant isolation on `TryConsumeResetTokenAsync` is additionally enforced explicitly, in the SQL
  itself (`tenant_id = @tenantId`), independent of RLS - proven directly by
  `PasswordResetTokenRepositoryConcurrencyTests.TryConsumeResetTokenAsync_WrongTenant_ReturnsNull`
  and by `TenantHostPasswordResetFlowIntegrationTests.ResetPassword_TokenFromDifferentTenantHost_FailsGenerically`.
- No raw token, token hash, password, or password hash is logged anywhere in the changed code.
  `ResetPasswordCommandHandler` has no `_logger` field at all (verified by
  `PasswordResetHardeningArchitectureTests.ResetPasswordCommandHandler_NeverLogsAnything`); same for
  `ForcePasswordChangeCommandHandler`.
- The restricted-role/RLS tests already in the suite
  (`BaseForgotPasswordRestrictedRoleHttpIntegrationTests`, `BaseForgotPasswordRlsIntegrationTests`)
  were re-run unmodified and remain green (see Verification section).

## Email delivery / outbox confirmation

Not modified in this task; confirmed still true:

- `RequestPasswordResetCommandHandler` and `BaseForgotPasswordCommandHandler` enqueue via
  `IOutboxWriter.EnqueueAsync`, never call `IEmailService.SendPasswordResetAsync` directly
  (`ForgotPasswordDeliveryArchitectureTests.ForgotPasswordHandlers_NeverCallSendPasswordResetAsyncDirectly`,
  plus the new `PasswordResetHandlers_NeverCallSendPasswordResetAsyncDirectly` covering
  `ResetPasswordCommandHandler`/`ForcePasswordChangeCommandHandler`, which never call it either).
- Outbox payloads (including the raw reset token) are encrypted at rest:
  `OutboxWriter.EnqueueAsync` calls `_encryption.Encrypt(payloadJson)` (AES, via
  `IEncryptionService`) before storing `OutboxMessage.EncryptedPayload`; there is no plaintext
  column. Confirmed by reading `OutboxWriter.cs` directly and by the integration tests, which must
  decrypt the payload to read the raw token back out.
- Provider selection is DB-backed (`platform_providers` + `platform_service_keys`), not read from
  `Email:Provider`/`Email__Provider` config - confirmed empty by the required `rg` search (see
  Verification).

## Rate limiting

`AuthRateLimitingMiddleware` (unmodified) already has explicit rules for
`/api/v1/auth/forgot-password`, `/api/v1/auth/reset-password`, and
`/api/v1/auth/force-change-password` (both IP-scoped and field-scoped buckets per route). This is a
process-local, in-memory (`IMemoryCache`) limiter.

**This is acceptable only for single-instance Phase 1 deployment.** Before horizontal scaling to
more than one API instance, this must be replaced with a shared limiter (Redis or PostgreSQL-backed)
- a process-local `IMemoryCache` bucket is invisible to any other instance, so the effective rate
limit becomes `configured limit x instance count` once more than one instance is running. No
distributed rate limiter exists in this codebase yet and none was added here, per the task's
instruction not to replace it without already-approved design docs.
`PasswordResetHardeningArchitectureTests.AuthRateLimitingMiddleware_StillCoversForgotResetAndForceChangePassword`
locks in that the three rules stay present so this constraint does not silently regress.

## Verification commands and results

All commands run from `HRMS-Backend-v1` root.

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal
  -> Build succeeded. 0 Warning(s), 0 Error(s).

dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --no-build \
  --filter "ResetPassword|ForgotPassword|ForcePasswordChange|PasswordReset" --verbosity minimal
  -> Passed: 47, Failed: 0, Skipped: 0. Includes two tests added after an advisor review pass that
     invoke ValidationBehavior directly with a weak password and assert it throws
     FluentValidation.ValidationException - the exact type ExceptionHandlerMiddleware maps to HTTP
     400 - closing the gap between "the validator rejects weak input" (proven by
     ResetPasswordCommandValidatorTests) and "a weak-password HTTP request gets 400, not an
     unhandled 500" (this task added no new custom exception types, so no new middleware mapping was
     needed - only proving the existing one still applies here).

dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore --no-build \
  --verbosity minimal
  -> Passed: 255, Failed: 0, Skipped: 0. (Full architecture suite, not filtered - confirms no
     regression anywhere else in the codebase.)

dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --no-restore --no-build \
  --filter "PasswordReset|ForgotPassword|BaseForgotPasswordRestrictedRole" --verbosity minimal
  -> Passed: 22, Failed: 0, Skipped: 0. Ran against real PostgreSQL via Testcontainers (Docker
     confirmed available and used; nothing in this task was skipped for lack of Docker).

git diff --check
  -> Exit code 0. No trailing-whitespace or conflict-marker errors. (Pre-existing CRLF/LF
     line-ending advisories on files outside this task's changes are warnings, not errors, and
     `git diff --check`'s exit code confirms that.)
```

Required `rg` searches:

```
rg -n "SetAdminMode|BYPASSRLS|DisableRowLevelSecurity" src/ONEVO.Application/Features/Auth/Login \
  src/ONEVO.Infrastructure/Persistence/Repositories/Auth
  -> src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Legal/EfLegalLoginChallengeRepository.cs:
     two SetAdminMode() calls. This file is unrelated to password reset (it is the legal
     login-challenge repository, not touched by this task); zero hits under
     Features/Auth/Login (forgot-password, reset-password, force-change-password, invite).

rg -n "SendPasswordResetAsync" src/ONEVO.Application/Features/Auth/Login/Commands
  -> No matches. No command handler calls the email sender directly.

rg -n "Log.*Token|Log.*Password|Log.*token|Log.*password|TokenHash|RawToken" \
  src/ONEVO.Application/Features/Auth/Login src/ONEVO.Infrastructure/ExternalServices/Email \
  tests/ONEVO.Tests.Architecture
  -> Matches are field/property names (TokenHash, RawToken as DTO members), method/class names,
     and architecture-test assertions guarding against unsafe logging. No _logger.Log*(...) call
     anywhere in scope includes a raw token, token hash, or password.

rg -n "Email__Provider|Email:Provider|SendGrid__ApiKey|Resend__ApiKey|Email__Resend__ApiKey|Email__Smtp__Password" \
  .env.example src/ONEVO.Api/appsettings.json src/ONEVO.Api/appsettings.Development.json src
  -> .env.example does not exist in this repository. No matches in appsettings.json,
     appsettings.Development.json. Matches under src are all inside test-only architecture guard
     assertions and pre-existing report markdown, not runtime config.

rg -n -P "[^\x00-\x7F]" src/ONEVO.Application/Features/Auth/Login tests/ONEVO.Tests.Unit/Features/Auth \
  tests/ONEVO.Tests.Architecture PASSWORD_RESET_PRODUCTION_READINESS_REPORT.md
  -> A handful of pre-existing em-dashes in comments in files this task did not create or modify
     (IBaseLoginCandidateRepository.cs, AuthSessionResponseSerializationTests.cs,
     PermissionResolverBoundaryTests.cs, AuthMfaControllerTests.cs,
     AdminDatabaseTicketStoreTests.cs). None of the files added or changed by this task contain
     non-ASCII characters. This report file itself is written in strict ASCII.
```

## Remaining limitations

- **Rate limiting is single-instance only.** See the Rate Limiting section above. This must be
  replaced with a distributed limiter before running more than one API instance.
- **Retry-after-lost-ack edge case in atomic consumption.** See "Known residual limitation" above -
  an extremely rare EF Core execution-strategy retry scenario can burn a token whose password change
  actually succeeded, surfacing a false "invalid token" error to that one request. Not fixed here;
  documented as an accepted risk given how rare it is (requires a commit-acknowledgment to be lost
  at the network layer specifically between PostgreSQL and the API process).
- **BCrypt hashing runs inside the transaction.** `_passwordHasher.Hash(...)` executes inside the
  `ExecuteInTransactionAsync` delegate, adding BCrypt's deliberate ~100-300ms cost to how long the
  row lock and database connection are held. This was a deliberate choice to keep the whole
  token-consume-and-password-update sequence atomic and simple to reason about, not an oversight;
  the per-token rate limit (5 attempts/15 min) bounds how much this can be exploited for connection
  exhaustion. If connection pool pressure becomes a real concern, hashing the new password before
  entering the transaction (a two-line change with identical semantics, since the hash does not
  depend on anything read inside the transaction) would shrink the lock window.
- **No complexity rules (uppercase/digit/symbol) in the password policy**, by design - see
  "Password policy validation" above. If required, this should be a single change to
  `PasswordPolicy` so invite signup, reset, and force-change tighten together, with its own test
  coverage and a decision on whether existing user passwords are grandfathered.
- **Provision/confirm-activation flows still bypass `tenant_status_histories`** and there is no read
  API for it - this is a pre-existing gap noted in prior session memory, out of scope for this task
  and not touched here.
- **Docker was available and used for every integration/concurrency test in this task** - nothing
  here is "not run"; all Testcontainers-backed tests in the Verification section above executed
  against real PostgreSQL.

## Production readiness statement

Given the above:

- Atomic token consumption is implemented (`TryConsumeResetTokenAsync`, single guarded UPDATE) and
  tested, including a true-concurrency proof against real PostgreSQL with exactly one winner among
  8 parallel callers.
- Password validators exist for reset and force-change, are proven wired into the real MediatR
  pipeline (not just present as unused classes), and all listed unit tests pass.
- Restricted-role/RLS tests (pre-existing, re-run unmodified) pass, and RLS is confirmed to still
  govern the new atomic UPDATE (session-scoped GUCs on the same connection, no admin-mode bypass
  anywhere in the changed code).
- Integration tests covering the full tenant-host and base-domain-to-tenant-host reset flows pass
  against real PostgreSQL; none were skipped.
- Email delivery remains DB-backed and outbox-based, with the payload (including the raw reset
  token) encrypted at rest.

The password-reset flow's token-consumption race and missing password validation - the two blockers
explicitly called out for this task - are fixed and verified. It is **not** unconditionally
"production ready" for a horizontally-scaled deployment: the rate limiter is scoped to a single
instance, and that constraint must be resolved (distributed limiter) before scaling out. Within a
single-instance Phase 1 deployment, the flow is production ready per the verification above.
