# Tenant Session RLS Context Fix Report

## Root cause

`TenantDatabaseTicketStore` is registered as a **singleton** (`CookieAuthenticationOptions` requires
the ticket store instance up front). Every call to `StoreAsync` creates a brand-new DI scope via
`IServiceScopeFactory.CreateScope()`, which is a fresh root-level scope, unrelated to the HTTP
request's own scope.

That new scope resolves its own `ApplicationDbContext`, and therefore its own `TenantContextAccessor`
(the `ITenantContext`/`IWritableTenantContext` implementation), which always constructs in
`TenantContextMode.System` with an empty `TenantId`
(`src/ONEVO.Infrastructure/Identity/Tenancy/TenantContextAccessor.cs`). `TenantRlsInterceptor` reads
that same scoped `ITenantContext` when the connection opens and sets
`app.current_tenant_id = ''`, `app.tenant_context_mode = 'system'`
(`src/ONEVO.Infrastructure/Persistence/Interceptors/TenantRlsInterceptor.cs`).

The `sessions` table's RLS policy is:

```sql
CREATE POLICY tenant_isolation ON sessions
    USING      (tenant_id::text = current_setting('app.current_tenant_id', true))
    WITH CHECK (tenant_id::text = current_setting('app.current_tenant_id', true));
```

Any earlier call to `ITenantContextSwitcher.SwitchToTenantAsync` (e.g. from
`LoginContinuationService` after a base-domain login resolves a winning tenant) only mutates the
**request scope's** `TenantContextAccessor`. It has no effect on the ticket store's independent
scope. So the `INSERT INTO sessions (...)` in `StoreAsync` always runs with
`app.current_tenant_id = ''`, and `WITH CHECK` rejects it — this affects **both** base-domain and
tenant-host logins identically, since `StoreAsync` never looked at which host resolved the tenant,
only at `ticket.Properties.Items["tenant_id"]`.

## Why the existing integration tests didn't catch this

`BaseDomainLoginTestFactory`/`E2ETestFactory` rebind `ApplicationDbContext` to the raw Testcontainers
superuser connection string **and drop `.AddInterceptors(TenantRlsInterceptor)`** in their
`ConfigureServices` override. Superusers bypass RLS unconditionally (even under `FORCE ROW LEVEL
SECURITY`), and with the interceptor removed the GUCs are never even set. So every existing
HTTP-level login test in the suite runs with RLS invisible — a session insert succeeds identically
whether or not this bug is fixed. This is why `ExactOneMatch_LogsIn_...` and
`TenantHostPasswordLogin_MfaVerify_...` passed before any fix was applied (verified below).

## Files changed

- **`src/ONEVO.Infrastructure/Identity/Sessions/TenantDatabaseTicketStore.cs`**
  `StoreAsync` now resolves `ITenantRepository` and `ITenantContextSwitcher` from its own scope,
  looks up the tenant by the id already carried on `ticket.Properties.Items["tenant_id"]`, verifies
  `Active`/`Trial` status, and calls `ITenantContextSwitcher.SwitchToTenantAsync(...)` on that same
  scope **before** `AddAsync`/`SaveChangesAsync`. If the tenant is missing or inactive, it throws
  `InvalidOperationException` before any session is constructed or written — no session row, no
  swallowed failure. Mojibake box-drawing comment separators (`── ... ──`) were replaced with plain
  ASCII; no behavior change.
- **`tests/ONEVO.Tests.Unit/Features/Auth/TenantDatabaseTicketStoreTests.cs`**
  Added `ITenantRepository`/`ITenantContextSwitcher` substitutes; updated the existing
  `StoreAsync_PersistsSession_ReturnsRawKey` test to stub an active tenant; added
  `StoreAsync_SwitchesToTenantContext_BeforeSavingSession` (asserts `SwitchToTenantAsync` is called
  with the ticket's tenant id, in order before `AddAsync`/`SaveChangesAsync`) and
  `StoreAsync_TenantMissingOrInactive_DoesNotCreateSession` (theory: tenant not found, tenant
  cancelled — asserts `InvalidOperationException`, and that the switcher/session repo/unit-of-work
  are never invoked).
- **`tests/ONEVO.Tests.Integration/Auth/TenantSessionRlsIntegrationTests.cs`** (new)
  Drives `TenantDatabaseTicketStore.StoreAsync` directly against a Testcontainers Postgres, but wires
  a small standalone `ServiceCollection` mirroring `ONEVO.Infrastructure.DependencyInjection`'s
  registrations — `ApplicationDbContext` + `TenantRlsInterceptor` + `EfAuthRepository` +
  `EfTenantRepository` + `TenantContextSwitcher` — pointed at the **real `onevo_app` role**
  (`NOSUPERUSER NOBYPASSRLS`), unlike the shared HTTP test factories. This is the one place in the
  suite that actually exercises RLS for session writes. Two tests: a successful insert under real RLS,
  and "inactive tenant → no session row, no bypass."

  This test also had to grant `onevo_app` `SELECT/INSERT/UPDATE/DELETE` on `ALL TABLES IN SCHEMA
  public` after migrations. `IntegrationDatabaseBootstrap`/`PrivilegedRoleTestBootstrap` (existing test
  helpers, unmodified) run migrations as the Testcontainers superuser, not `onevo_migrator`, so
  production's `ALTER DEFAULT PRIVILEGES FOR ROLE onevo_migrator ... GRANT ... TO onevo_app`
  (`ops/postgres/local-bootstrap-roles.sql`) never fires in this harness. The new test reproduces only
  that same blanket grant, scoped to its own test file — it does not touch shared bootstrap helpers or
  weaken anything; it is an object-level ACL grant, not an RLS change, and `onevo_app` remains
  `NOBYPASSRLS` throughout.

No other files were modified.

## Why this preserves RLS instead of bypassing it

- `onevo_app` is never granted `BYPASSRLS`; `SetAdminMode()`/`SetSystemMode()` are never called by the
  fix. The fix's only new behavior is calling the existing `ITenantContextSwitcher.SwitchToTenantAsync`
  — the same mechanism `LoginContinuationService` already uses — inside the ticket store's own scope,
  so the interceptor sees the correct `app.current_tenant_id`/`app.tenant_context_mode` when the
  session insert's connection opens.
- The tenant id is never taken from the browser/request body. It comes only from
  `ticket.Properties.Items["tenant_id"]`, which is set server-side by `LoginContinuationService`/
  `VerifyMfaCommandHandler`/etc. after credential verification, before `SignInAsync` is ever called.
  No new input surface was introduced.
- The tenant is re-verified against the database (`ITenantRepository.GetByIdAsync`, must be
  `Active`/`Trial`) immediately before the switch — an attacker cannot force a session write for a
  cancelled/suspended tenant even if a stale ticket somehow referenced one.
- `git diff --check` is clean; `rg` for `SetAdminMode|BYPASSRLS|DISABLE ROW LEVEL SECURITY` inside
  `TenantDatabaseTicketStore.cs` and the new integration test returns zero matches other than
  `NOBYPASSRLS` in explanatory comments. No RLS policy SQL was touched.

## Test results

| Suite | Command | Result |
|---|---|---|
| Build (API) | `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal` | 0 warnings, 0 errors |
| Unit | `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal` | **843/843 passed** |
| Architecture | `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal` | **221/221 passed** (includes pre-existing `TenantIsolationArchitectureTests`, which already fails the build on any new `BYPASSRLS`/`DISABLE ROW LEVEL SECURITY` outside the allow-listed migration) |
| Integration (required filter) | `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "BaseDomainLogin\|TenantLogin\|LegalAcceptance\|Session"` | **20/20 passed** |

Reproduction (systematic-debugging Phase 1, before writing any fix code):
- Ran `TenantSessionRlsIntegrationTests` against the **unfixed** `StoreAsync` (temporarily reverted via
  `git stash`) → both new tests failed. The successful-insert test failed with exactly the reported
  error:
  ```
  Npgsql.PostgresException (0x80004005): 42501: new row violates row-level security policy for table "sessions"
  ```
  Restored the fix (`git stash pop`), rebuilt, reran → both tests pass. This confirms the new tests
  actually exercise the bug (unlike the pre-existing HTTP-level tests, which pass identically with or
  without the fix — see "Why the existing integration tests didn't catch this" above).

## Manual Postman validation

No Postman client is available in this environment, so literal Postman screenshots aren't possible —
stating that plainly rather than claiming something I didn't do. The closest faithful equivalent was
run instead:

- The existing `BaseDomainLoginIntegrationTests.ExactOneMatch_LogsIn_ReturnsLegalAcceptanceRequired_ThenAcceptingIssuesSessionAndCsrfCookies`
  test drives real HTTP requests (`HttpClient` → `WebApplicationFactory<Program>`'s `TestServer`,
  i.e. the actual ASP.NET Core pipeline, controllers, and middleware — not a hand-rolled call) against
  the two endpoints named in the task:
  - `POST /api/v1/auth/login` on `Host: localhost` → `202 Accepted`,
    `legal_acceptance_required: true`, `continue_url` pointing at
    `/api/v1/legal/acceptances/complete-login`.
  - `POST /api/v1/legal/acceptances/complete-login` with the returned `onevo_legal_pending`/
    `onevo_legal_csrf` cookies → `200 OK`, with `Set-Cookie: onevo_session=...` and
    `Set-Cookie: onevo_csrf=...` present in the response.
  This passed both before and after the fix (see caveat above: this factory's `ApplicationDbContext`
  runs as the Testcontainers superuser, so it doesn't independently prove the RLS fix — it proves the
  HTTP/JSON/cookie contract is unchanged).
- `TenantSessionRlsIntegrationTests.StoreAsync_UnderRealOnevoAppRoleWithRlsInterceptor_InsertsSessionWithoutRlsViolation`
  is the test that actually proves the fix under real RLS (see Test results above): it drives
  `TenantDatabaseTicketStore.StoreAsync` — the exact code path `SignInAsync` calls at the end of both
  `POST /api/v1/auth/login` (post-legal-acceptance) and tenant-host login — under the real `onevo_app`
  role with `TenantRlsInterceptor` wired, and confirms the session row lands with the correct
  `tenant_id` and no `42501`.

Together these cover the two endpoints' contract (via the HTTP test) and the RLS-critical internals
(via the dedicated test) that the HTTP test's superuser connection can't see.

## Remaining risks

1. **Test-harness RLS blind spot is why this bug reached production invisibly.** Every existing
   `WebApplicationFactory`-based integration test (`BaseDomainLoginTestFactory`, `E2ETestFactory`, and
   presumably others following the same pattern) binds `ApplicationDbContext` to the Testcontainers
   superuser connection and omits `TenantRlsInterceptor`. None of them would have caught this bug, and
   none of them will catch a *future* regression of it either — only the new
   `TenantSessionRlsIntegrationTests` does. If someone touches `TenantDatabaseTicketStore` again without
   running that specific test, RLS regressions in this path can slip back in silently. Consider
   whether the shared HTTP test factories should eventually run the app's own DbContext as `onevo_app`
   with the interceptor wired (out of scope here per the task's "no changes beyond the fix" direction,
   and a larger, separate effort — the existing seeders like `DevSmokeTestTenantSeeder` insert into
   RLS-protected tables in system mode and would need rework first).
2. **`RenewAsync`/`RemoveAsync`/`RetrieveAsync` were not changed.** `RemoveAsync`/`RenewAsync` only
   `UPDATE ... WHERE key_hash = ...` against a session already scoped by its own row, and `RetrieveAsync`
   only reads. None of them `INSERT`, so none hit the `WITH CHECK` clause the way `StoreAsync` does;
   this was confirmed against the RLS policy (`USING`/`WITH CHECK` both keyed on `tenant_id`), but they
   still run in System-mode scopes, so an `UPDATE`/`SELECT` against a different tenant's row would
   silently no-op/return nothing rather than throw — that's pre-existing behavior, out of scope for
   this fix per the task's constraints, and worth a follow-up if the team wants stronger read/renew
   isolation guarantees.
3. **`docker`/Testcontainers is required** to run the new/updated integration tests; they were verified
   locally with Docker Desktop but were not run in this session's CI.

## Explicit confirmations

- **No RLS policies were weakened, disabled, or altered.** `tenant_isolation` on `sessions` is
  unchanged.
- **No admin-mode or system-mode session write was introduced.** `SetAdminMode()`/`SetSystemMode()` are
  never called by the fix; the session insert always happens after a real `Active`/`Trial` tenant is
  resolved and `SwitchToTenantAsync` (`TenantContextMode.Tenant`) is applied.
- **`onevo_app` remains `NOBYPASSRLS`** in both production tooling and the new test's setup.
