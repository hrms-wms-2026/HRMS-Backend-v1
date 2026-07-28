# Tenant Session RLS Context Fix Report

> ## ⚠ Deployment prerequisite — read before shipping
>
> This fix includes migration **`20260727012103_BackfillRolePermissionAndUserRoleTenantId`**, which
> is **not optional**. Deploying the code changes in this report without that migration having run
> first will return **empty permission sets for every pre-existing tenant, role, and user** the moment
> it ships — every role-derived permission (e.g. `roles:read`) will 403 for anyone whose
> `RolePermission`/`UserRole` rows were written before this fix, because those rows have
> `TenantId = Guid.Empty` and the code fix in this report is what starts correctly filtering by
> tenant for the first time. Module-auto-grant permissions are unaffected (they're resolved from
> `TenantSubscriptions`, filtered explicitly by tenant id, not from these rows). See "Additional
> pre-existing bugs found and fixed" and "Backfill migration" below for the full explanation and the
> exact SQL. **Apply the migration in the same deploy as this code, not after.**

## Root cause

`TenantDatabaseTicketStore` is registered as a **singleton** (`CookieAuthenticationOptions` requires
the ticket store instance up front). Every call to `StoreAsync`/`RetrieveAsync`/`RenewAsync`/
`RemoveAsync` creates a brand-new DI scope via `IServiceScopeFactory.CreateScope()`, which is a fresh
root-level scope, unrelated to the HTTP request's own scope.

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
scope. So the `INSERT INTO sessions (...)` in `StoreAsync` always ran with `app.current_tenant_id = ''`,
and `WITH CHECK` rejected it — this affects **both** base-domain and tenant-host logins identically,
since `StoreAsync` never looked at which host resolved the tenant, only at
`ticket.Properties.Items["tenant_id"]`.

`RetrieveAsync`/`RenewAsync`/`RemoveAsync` have the identical structural cause but failed silently
under RLS instead of throwing 42501: `RetrieveAsync`'s `SELECT` returned no row (session invisible,
so every request behaved as logged-out), and `RemoveAsync`'s revoke `UPDATE` silently matched zero
rows (logout appeared to succeed but the session stayed live). The scope of this fix was extended to
all four `ITicketStore` methods (user-approved) rather than `StoreAsync` alone, since leaving the
other three unfixed would have left session retrieval/renewal/revocation permanently broken by RLS
even after the reported `StoreAsync` bug was closed.

## Why the existing integration tests didn't catch this

`BaseDomainLoginTestFactory`/`E2ETestFactory` rebind `ApplicationDbContext` to the raw Testcontainers
superuser connection string **and drop `.AddInterceptors(TenantRlsInterceptor)`** in their
`ConfigureServices` override. Superusers bypass RLS unconditionally (even under `FORCE ROW LEVEL
SECURITY`), and with the interceptor removed the GUCs are never even set. So every existing
HTTP-level login test in the suite ran with RLS invisible — a session insert succeeded identically
whether or not this bug was fixed. This is why `ExactOneMatch_LogsIn_...` and
`TenantHostPasswordLogin_MfaVerify_...` passed before any fix was applied (verified via git-stash
before/after comparison).

## The bootstrap problem for RetrieveAsync/RemoveAsync, and how it was solved

`StoreAsync`/`RenewAsync` always have a tenant id available (on the ticket) before touching the
database, so they can resolve + switch tenant context up front, the same shape. `RetrieveAsync`/
`RemoveAsync` only ever have a session key (from the auth cookie) — never a tenant id — because the
whole point of `RetrieveAsync` is to look up which tenant/user a session key belongs to. There is no
tenant id to switch to before the very first lookup that would tell them one.

User-approved solution: a second, narrowly-scoped **permissive** RLS policy on `sessions`, `FOR
SELECT` only, matching on the session's own `key_hash` instead of `tenant_id`:

```sql
CREATE POLICY session_key_lookup ON sessions
    FOR SELECT
    USING (key_hash = current_setting('app.session_lookup_key_hash', true));
```

Postgres OR-combines multiple permissive policies for the same command, so this **coexists with**
(does not replace) `tenant_isolation` — either policy being satisfied grants row visibility for
`SELECT`. `tenant_isolation` alone still governs `INSERT`/`UPDATE`/`DELETE`; this new policy grants
no write access at all. The reasoning: `key_hash` is a SHA-256 hash of a 32-byte cryptographically
random value that only ever leaves the server inside an `HttpOnly` cookie — presenting it back is
itself the proof of authorization, independent of `tenant_id`. Only one caller
(`EfAuthRepository.GetByKeyHashForTenantResolutionAsync`) ever sets
`app.session_lookup_key_hash`, and it does so `is_local` inside a transaction (reverts automatically
at transaction end — never leaks onto the pooled physical connection for a later, unrelated caller to
see), exactly to satisfy this one bootstrap lookup. `TenantDatabaseTicketStore` then switches to the
resolved tenant context before doing anything else with the row.

## Files changed

- **`src/ONEVO.Infrastructure/Identity/Sessions/TenantDatabaseTicketStore.cs`**
  All four `ITicketStore` methods now resolve `ITenantRepository`/`ITenantContextSwitcher` from their
  own scope and call `SwitchToTenantAsync` before doing anything tenant-scoped:
  - `StoreAsync`: looks up the tenant from `ticket.Properties.Items["tenant_id"]`, rejects
    (`InvalidOperationException`, no session written) only if the tenant is **`Suspended` or
    `Cancelled`** — a deliberate denylist, not an `Active`/`Trial` allowlist (see "Suspended/Cancelled
    denylist, not Active/Trial allowlist" below) — then switches, then writes the session.
  - `RenewAsync`: reads the tenant id already present on the ticket, switches, then proceeds with the
    existing renewal logic. No-ops (returns without renewing) if the ticket has no tenant id.
  - `RetrieveAsync`: calls the new `GetByKeyHashForTenantResolutionAsync` (satisfies
    `session_key_lookup`), resolves the tenant from the returned session's `TenantId`, switches, then
    reads the user and resolves permissions. Returns `null` without switching if the tenant can't be
    resolved.
  - `RemoveAsync`: same lookup, resolves tenant, switches, then flips `session.IsRevoked = true` and
    saves (previously called `RevokeByKeyHashAsync`, replaced with direct entity mutation now that the
    session is already loaded).
  Mojibake box-drawing comment separators (`── ... ──`) were replaced with plain ASCII; no behavior
  change from that alone.
- **`src/ONEVO.Application/Features/Auth/Login/RepositoryInterfaces/ISessionRepository.cs`**
  Added `GetByKeyHashForTenantResolutionAsync(string keyHash, ct)` — the only lookup permitted to find
  a session before its tenant is known.
- **`src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs`**
  Implemented `GetByKeyHashForTenantResolutionAsync`: sets `app.session_lookup_key_hash` via
  `set_config(..., is_local: true)` inside an explicit transaction (wrapped in
  `_db.Database.CreateExecutionStrategy().ExecuteAsync(...)`, matching the existing
  `EfLegalLoginChallengeRepository` pattern — required because the runtime connection has
  `EnableRetryOnFailure` configured, and EF Core forbids a user-initiated `BeginTransactionAsync`
  under a retrying execution strategy unless it runs inside `ExecuteAsync`), then reads the session by
  `key_hash`, then commits (which reverts the `is_local` GUC).
- **`src/ONEVO.Infrastructure/Migrations/20260726174515_AddSessionKeyHashRlsLookupPolicy.cs`** (new)
  Adds the `session_key_lookup` policy described above. Raw SQL only, no EF model changes (the model
  snapshot is untouched).
- **`src/ONEVO.Infrastructure/Migrations/20260727012103_BackfillRolePermissionAndUserRoleTenantId.cs`**
  (new) Data-correction migration for the pre-existing `RolePermission`/`UserRole` bug described below
  — see "Backfill migration" for details. **Required at deploy time**, not optional.
- **`tests/ONEVO.Tests.Unit/Features/Auth/TenantDatabaseTicketStoreTests.cs`**
  Added `ITenantRepository`/`ITenantContextSwitcher` substitutes. New/updated tests cover: switch
  happens before save (`StoreAsync_SwitchesToTenantContext_BeforeSavingSession`, asserted via
  `Received.InOrder`); the denylist gate, both directions
  (`StoreAsync_TenantMissingOrDead_DoesNotCreateSession` for `Suspended`/`Cancelled`/missing,
  `StoreAsync_TenantNotDead_CreatesSession` for `Provisioning`/`Trial`/`Active`); `RetrieveAsync`
  switches before reading the user and permissions, and returns `null` without switching if the
  tenant can't be resolved; `RenewAsync` switches when the ticket carries a tenant id and no-ops when
  it doesn't; `RemoveAsync` switches and revokes, and does nothing if the session isn't found.
- **`tests/ONEVO.Tests.Integration/Auth/TenantSessionRlsIntegrationTests.cs`** (new)
  Drives all four `TenantDatabaseTicketStore` methods directly against a Testcontainers Postgres, but
  wires a small standalone `ServiceCollection` mirroring `ONEVO.Infrastructure.DependencyInjection`'s
  registrations — `ApplicationDbContext` + `TenantRlsInterceptor` + `EfAuthRepository` +
  `EfTenantRepository` + `TenantContextSwitcher` — pointed at the **real `onevo_app` role**
  (`NOSUPERUSER NOBYPASSRLS`), unlike the shared HTTP test factories. This is the one place in the
  suite that actually exercises RLS for session reads/writes. Six tests: successful insert under real
  RLS; `Cancelled` tenant → no session row, no bypass; `Provisioning` tenant → session **is** created
  (proves the denylist doesn't block invite-accept while a tenant is still provisioning); retrieve
  returns the ticket a prior `StoreAsync` wrote; renew extends expiry without an RLS violation; remove
  revokes the session without an RLS violation.

  This test also had to grant `onevo_app` `SELECT/INSERT/UPDATE/DELETE` on `ALL TABLES IN SCHEMA
  public` after migrations. `IntegrationDatabaseBootstrap`/`PrivilegedRoleTestBootstrap` (existing test
  helpers, unmodified) run migrations as the Testcontainers superuser, not `onevo_migrator`, so
  production's `ALTER DEFAULT PRIVILEGES FOR ROLE onevo_migrator ... GRANT ... TO onevo_app`
  (`ops/postgres/local-bootstrap-roles.sql`) never fires in this harness. The new test reproduces only
  that same blanket grant, scoped to its own test file — it does not touch shared bootstrap helpers or
  weaken anything; it is an object-level ACL grant, not an RLS change, and `onevo_app` remains
  `NOBYPASSRLS` throughout.

### Additional pre-existing bugs found and fixed while closing out this work

Running the **full** (unfiltered) integration suite — not just the task's required filter — after the
above changes surfaced `TenantProvisioningE2ETests.Full_tenant_provisioning_flow` failing with
`403 Forbidden: Permission 'roles:read' required'` on `GET /api/v1/roles`, right after a successful
login. Root-caused via targeted diagnostic instrumentation (temporary, removed before final commit) in
`PermissionResolver`/`EfAuthRepository`, not by guessing:

- **`src/ONEVO.Infrastructure/Services/DevPlatform/Provisioning/TenantOwnerInvitationService.cs`**
  The `UserRole` created for a tenant's owner (during `CreateInviteRecordsAsync`, invoked by tenant
  provisioning) never set `TenantId`, defaulting it to `Guid.Empty`. Fixed: `TenantId = tenantId`
  added to the object initializer, matching the sibling `AcceptInvitationPasswordCommandHandler`/
  `AcceptInvitationGoogleCommandHandler` handlers, which already set it correctly.
- **`RolePermission` rows created with no `TenantId`** (same `Guid.Empty` defect, six separate call
  sites — only the dev-only `DevSmokeTestTenantSeeder` got this right):
  `src/ONEVO.Infrastructure/Services/DevPlatform/Tenancy/DefaultRoleSeeder.cs` (the Owner role seeded
  for every new tenant), `AdminCreateTenantRoleCommandHandler.cs`,
  `AdminAssignTenantRolePermissionsCommandHandler.cs`, `ApplyRoleTemplateCommandHandler.cs`,
  `CreateRoleCommandHandler.cs`, `AssignRolePermissionsCommandHandler.cs`. Each fixed the same way:
  added `TenantId = <the tenantId already in scope at that call site>` to the `RolePermission`
  initializer.

**Why this was a pre-existing bug, not a regression introduced by the RLS fix:** `RolePermission`/
`UserRole` both implement `ITenantOwnedEntity` and are subject to the same ambient EF Core ownership
filter (`ApplicationDbContext.IsTenantFilterActive`/`CurrentTenantId`) that governs RLS-style
in-process tenant scoping. Before this fix, `RetrieveAsync` never called `SwitchToTenantAsync` at all,
so every session-retrieval read — including the permission-resolution query
(`ListRolePermissionCodesWithModulesAsync`, which has no explicit tenant filter of its own and relies
entirely on that ambient filter) — ran with the filter **inactive**, returning rows regardless of
their `TenantId` value. The join is keyed on `RoleId` (globally unique), so the wrong `TenantId` value
on these rows was silently tolerated and produced the right answer by accident. Correctly activating
tenant-context switching in `RetrieveAsync` (this fix) activated that ambient filter for the first
time in this path, which then — correctly — excluded rows whose `TenantId` didn't match. The
`RolePermission`/`UserRole` bug was already live in production before this session; this fix's
correctness is what exposed it, and leaving it unfixed would have shipped a session fix that broke
RBAC permission resolution for every newly created tenant/role. Confirmed via instrumented diagnostic
run showing `TenantId = 00000000-0000-0000-0000-000000000000` on the raw (query-filter-bypassed) rows,
reverted after use; not present in the final diff.

No other files were modified besides the backfill migration described next.

## Backfill migration — required at deploy time

Fixing the six write paths above only stops *new* `RolePermission`/`UserRole` rows from being written
with `TenantId = Guid.Empty`. It does nothing for rows already written by the old code — and this
codebase has been live since at least `76fae71`/earlier commits, so production almost certainly has
such rows already (every tenant's seeded "Owner" role alone guarantees at least one). Once this fix's
`RetrieveAsync` change activates tenant-context switching in production, `ApplicationDbContext`'s
ambient tenant filter becomes active for permission resolution for the first time — and it will
(correctly) exclude every `RolePermission`/`UserRole` row still sitting at the `Guid.Empty` sentinel,
turning every affected user's role-derived permissions into an empty set. This is **not a hypothetical
edge case** — it reproduces for the "Owner" role of every tenant provisioned before this fix.

`src/ONEVO.Infrastructure/Migrations/20260727012103_BackfillRolePermissionAndUserRoleTenantId.cs`
fixes this by copying the correct `TenantId` from each row's owning `Role` (`Role.TenantId` has always
been set correctly — every `Role`-creation code path sets it explicitly, only the child
`RolePermission`/`UserRole` rows were missing it):

```sql
SET LOCAL app.tenant_context_mode = 'admin';

UPDATE role_permissions rp
SET tenant_id = r.tenant_id
FROM roles r
WHERE rp.role_id = r.id
  AND rp.tenant_id = '00000000-0000-0000-0000-000000000000';

UPDATE user_roles ur
SET tenant_id = r.tenant_id
FROM roles r
WHERE ur.role_id = r.id
  AND ur.tenant_id = '00000000-0000-0000-0000-000000000000';
```

**Why `SET LOCAL app.tenant_context_mode = 'admin'` is required, not optional:** migrations run as
`onevo_migrator`, which is `NOSUPERUSER NOBYPASSRLS` (`ops/postgres/local-bootstrap-roles.sql`), and
`role_permissions`/`user_roles` have `FORCE ROW LEVEL SECURITY` set
(`20260515022320_AddRlsPolicies.cs`) — `FORCE` means RLS applies even to `onevo_migrator` as the
tables' owner, which it otherwise wouldn't. The current `tenant_isolation` policy
(`20260520000000_UpdateRlsTenantContextMode.cs`) is
`current_setting('app.tenant_context_mode', true) = 'admin' OR (... 'tenant' AND tenant_id
matches)`. With neither GUC set — the normal state during migration execution, since nothing sets
them outside a running HTTP request — a plain `UPDATE`'s `USING` clause matches **zero rows** and the
backfill silently no-ops. `'admin'` is not a new bypass invented for this migration — it's the same
existing admin-mode branch the RLS policy has always granted to `IWritableTenantContext.SetAdminMode()`
callers elsewhere in the application (and is, per the earlier root-cause analysis, almost certainly how
the six write-path handlers' original bad inserts got past `WITH CHECK` in the first place); this
migration only sets the GUC directly, since migrations don't go through that C# API. `SET LOCAL` (not
session-wide `SET`) confines it to this migration's own transaction, so it cannot leak onto a pooled
connection afterward.

**This was verified empirically, not assumed** — the full integration suite cannot exercise this at
all, for two independent reasons: (1) `IntegrationDatabaseBootstrap` runs every migration as the
Testcontainers **superuser**, which bypasses RLS unconditionally regardless of whether the GUC fix is
present, so the suite would report success either way; (2) every test seeds its data through the
already-fixed write paths, so no `Guid.Empty` rows ever exist for the `UPDATE` to find and correct in
the first place — the backfill logic itself is never exercised by any automated test in this
repository. To close both gaps, a throwaway Postgres container was built by hand, replicating
`onevo_migrator`'s exact production privilege profile (`NOSUPERUSER NOBYPASSRLS`, table owner,
`FORCE ROW LEVEL SECURITY` active, same `tenant_isolation` policy text) with one `role_permissions` row
seeded at the `Guid.Empty` sentinel (itself only insertable under `SET LOCAL
app.tenant_context_mode = 'admin'`, confirming how the original bug's rows got written). Results:
  - The naive `UPDATE` (no GUC set) → **`UPDATE 0`** — reproduces the silent-no-op failure mode exactly
    as predicted.
  - The same `UPDATE` wrapped in `SET LOCAL app.tenant_context_mode = 'admin'` → **`UPDATE 1`**, and a
    follow-up superuser read confirmed `tenant_id` was corrected to the seeded role's real tenant id.

  The throwaway test wrapped the fixed statements in an explicit `BEGIN`/`COMMIT` to demonstrate
  `SET LOCAL`'s transaction-scoping; the actual migration relies on EF Core's default behavior of
  wrapping each migration's `Up()` in a single transaction on providers with transactional DDL
  (Npgsql qualifies, and this migration contains no operation — e.g. `CREATE INDEX CONCURRENTLY`
  — that would force EF to run outside one), so `SET LOCAL` is scoped and honored the same way in the
  real migration without needing an explicit `BEGIN`/`COMMIT` in its SQL.

Properties of this migration:
- **Idempotent.** Only rows still at the empty-guid sentinel are touched; running it twice, or after
  further such rows are somehow written, is safe and a no-op on rows already corrected.
- **Deterministic and complete.** Every `RolePermission`/`UserRole` row has exactly one owning `Role`
  via `RoleId` (a required, non-nullable foreign key), so every affected row has an unambiguous correct
  value to backfill from. There is no ambiguous or unrecoverable case.
- **`Down()` is a deliberate no-op.** There is no correct prior value to revert to — reverting
  `TenantId` back to `Guid.Empty` would only reintroduce the bug this migration exists to fix.
- **Builds clean and runs without error as part of the full integration suite** (see Test results),
  alongside every other migration in the project — but, per the verification note above, that only
  proves the SQL is syntactically valid, not that it corrects data under real RLS; the throwaway-cluster
  test above is what proves correction under real RLS, since nothing in the automated suite can.
- **This is a data-correction migration on tables that carry real tenant data.** It was written and
  reviewed as part of this fix, but — as with any production data migration — a human should review the
  exact SQL above (also visible in the migration file itself) before it runs against a production
  database, per this codebase's normal migration-review practice. It was not, and should not be, run
  against any environment beyond the local Testcontainers instances this session's automated tests
  spin up and tear down.

## Suspended/Cancelled denylist, not Active/Trial allowlist

An initial version of `StoreAsync`'s gate rejected any tenant that wasn't `Active` or `Trial`. Running
the **full** integration suite (not just the required filter) caught a real regression from this:
`TenantProvisioningE2ETests.Full_tenant_provisioning_flow` failed with `500` on
`POST /api/v1/auth/invitations/{token}/accept-password`, because invite-accept legitimately creates a
session while the tenant is still `Provisioning` (before admin confirmation flips it to `Trial`).
`StoreAsync` is a shared infrastructure chokepoint reached by invite-accept and password-reset flows
too, not just login. Fixed by changing the gate to a denylist —
`tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled` — since the stricter `Active`/
`Trial` business rule for login specifically already lives upstream in
`LoginContinuationService.ContinueAsync`; `StoreAsync`'s gate is only a backstop against writing a
session for a tenant that is definitively dead.

## Why this preserves RLS instead of bypassing it

- `onevo_app` is never granted `BYPASSRLS`; `SetAdminMode()`/`SetSystemMode()` are never called by the
  fix. The fix's only new behavior is calling the existing `ITenantContextSwitcher.SwitchToTenantAsync`
  — the same mechanism `LoginContinuationService` already uses — inside each method's own scope, so the
  interceptor sees the correct `app.current_tenant_id`/`app.tenant_context_mode` when the relevant
  query/insert's connection opens.
- The new `session_key_lookup` policy is `FOR SELECT` only and additive (OR-combined with
  `tenant_isolation`, never replacing it); it grants no `INSERT`/`UPDATE`/`DELETE` access, and the GUC
  it checks (`app.session_lookup_key_hash`) is `is_local` — set only inside one transaction, in one
  repository method, and gone the moment that transaction commits.
- The tenant id is never taken from the browser/request body. `StoreAsync`/`RenewAsync` take it from
  `ticket.Properties.Items["tenant_id"]`, set server-side after credential verification, before
  `SignInAsync` is ever called. `RetrieveAsync`/`RemoveAsync` take it from the session row itself,
  found only via the `HttpOnly`-cookie-only session key. No new input surface was introduced.
- `StoreAsync` re-verifies the tenant against the database (`ITenantRepository.GetByIdAsync`)
  immediately before the switch, rejecting `Suspended`/`Cancelled` tenants outright.
- `git diff --check` is clean; the `rg` architecture-guard grep for
  `SetAdminMode|BYPASSRLS|DISABLE ROW LEVEL SECURITY|ALTER TABLE sessions DISABLE|onevo_auth_base_login_fn_owner`
  across `src/ONEVO.Infrastructure/Identity/Sessions`, `src/ONEVO.Infrastructure/Migrations`, and
  `tests/ONEVO.Tests.Architecture` returns only pre-existing matches in unrelated migrations and
  architecture-test scaffolding — none in `TenantDatabaseTicketStore.cs` or the new migration. No RLS
  policy SQL was weakened or removed.

## Test results (final, after all fixes above)

| Suite | Command | Result |
|---|---|---|
| Build (API) | `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal` | 0 warnings, 0 errors |
| Unit | `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal` | **850/850 passed** |
| Architecture | `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal` | **221/221 passed** (includes pre-existing `TenantIsolationArchitectureTests`, which already fails the build on any new `BYPASSRLS`/`DISABLE ROW LEVEL SECURITY` outside the allow-listed migration) |
| Integration (required filter) | `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "BaseDomainLogin\|TenantLogin\|LegalAcceptance\|Session"` | **24/24 passed** |
| Integration (full, unfiltered suite) | `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --no-restore --no-build --verbosity minimal` | **80/80 passed**, 0 failed, 4m50s, no Docker flakiness — includes `TenantProvisioningE2ETests.Full_tenant_provisioning_flow`, which exercises the entire owner-invite → accept-password → provision-confirm → login → `GET /api/v1/roles` → CSRF checks → host isolation → logout chain end to end |

Reproduction (systematic-debugging Phase 1, before writing any fix code):
- Ran `TenantSessionRlsIntegrationTests` against the **unfixed** `StoreAsync` (temporarily reverted via
  `git stash`) → the successful-insert test failed with exactly the reported error:
  ```
  Npgsql.PostgresException (0x80004005): 42501: new row violates row-level security policy for table "sessions"
  ```
  Restored the fix, rebuilt, reran → passed. This confirms the new tests actually exercise the bug
  (unlike the pre-existing HTTP-level tests, which pass identically with or without the fix — see
  "Why the existing integration tests didn't catch this" above).
- For the `roles:read` regression, ran the single failing E2E test with temporary diagnostic logging
  (raw, query-filter-bypassed reads of `UserRoles`/`RolePermissions`) rather than guessing at the
  cause; the logged `TenantId = 00000000-0000-0000-0000-000000000000` on those rows directly pinpointed
  the seeding bug described above. Diagnostics were removed before the final build/test run.

## Manual Postman validation

No Postman client is available in this environment, so literal Postman screenshots aren't possible —
stating that plainly rather than claiming something I didn't do. The closest faithful equivalents:

- The existing `BaseDomainLoginIntegrationTests.ExactOneMatch_LogsIn_ReturnsLegalAcceptanceRequired_ThenAcceptingIssuesSessionAndCsrfCookies`
  test drives real HTTP requests (`HttpClient` → `WebApplicationFactory<Program>`'s `TestServer`, i.e.
  the actual ASP.NET Core pipeline, controllers, and middleware — not a hand-rolled call) against the
  two endpoints named in the task:
  - `POST /api/v1/auth/login` on `Host: localhost` → `202 Accepted`, `legal_acceptance_required: true`,
    `continue_url` pointing at `/api/v1/legal/acceptances/complete-login`.
  - `POST /api/v1/legal/acceptances/complete-login` with the returned `onevo_legal_pending`/
    `onevo_legal_csrf` cookies → `200 OK`, with `Set-Cookie: onevo_session=...` and
    `Set-Cookie: onevo_csrf=...` present in the response.
  This passes both before and after the fix (see caveat above: this factory's `ApplicationDbContext`
  runs as the Testcontainers superuser, so it doesn't independently prove the RLS fix — it proves the
  HTTP/JSON/cookie contract is unchanged).
- `TenantSessionRlsIntegrationTests.StoreAsync_UnderRealOnevoAppRoleWithRlsInterceptor_InsertsSessionWithoutRlsViolation`
  and its `RetrieveAsync`/`RenewAsync`/`RemoveAsync` counterparts are what actually prove the fix under
  real RLS: they drive `TenantDatabaseTicketStore`'s methods — the exact code paths `SignInAsync`/
  cookie-auth middleware call on every login, request, and logout — under the real `onevo_app` role
  with `TenantRlsInterceptor` wired, and confirm no `42501` at any point.
- `TenantProvisioningE2ETests.Full_tenant_provisioning_flow` is an end-to-end HTTP test that exercises
  the full real-world path this fix targets: owner invite → accept-password (session created) →
  provisioning confirm → fresh login (session created again) → `GET /api/v1/auth/me` (session
  retrieved) → `GET /api/v1/roles` (permissions resolved from the retrieved session) → role creation →
  host-isolation checks → logout (session revoked) → `GET /api/v1/auth/me` returns `401` (revocation
  confirmed). This now passes end to end, which is the strongest available substitute for a manual
  Postman run against a live server.

## Remaining risks

1. **Test-harness RLS blind spot.** Every existing `WebApplicationFactory`-based integration test
   (`BaseDomainLoginTestFactory`, `E2ETestFactory`, and presumably others following the same pattern)
   binds `ApplicationDbContext` to the Testcontainers superuser connection and omits
   `TenantRlsInterceptor`. `TenantSessionRlsIntegrationTests` now covers all four
   `TenantDatabaseTicketStore` methods under real RLS, closing the blind spot for this specific class,
   but the same blind spot still exists for any other code path that inserts/updates/selects
   RLS-protected tables — a future change elsewhere in the codebase with the same shape (fresh DI scope,
   no tenant-context switch) would not be caught by the shared HTTP test factories. Consider whether
   those factories should eventually run the app's own DbContext as `onevo_app` with the interceptor
   wired (a larger, separate effort — `DevSmokeTestTenantSeeder` and similar seeders insert into
   RLS-protected tables in system mode and would need rework first).
2. **The `RolePermission`/`UserRole` missing-`TenantId` bug may have other blast radius not covered by
   this session's tests.** All six `RolePermission` call sites and the one `UserRole` call site found
   via `grep "new RolePermission"`/`"new UserRole"` were fixed, and the full integration suite (80/80)
   passes, but this was a targeted fix for the specific failure surfaced by
   `TenantProvisioningE2ETests`, not an exhaustive audit of every tenant-scoped entity in the codebase
   for the same defect class. The backfill migration (see "Backfill migration" above) repairs existing
   `RolePermission`/`UserRole` rows specifically — if the same missing-`TenantId` pattern exists on some
   other `ITenantOwnedEntity` not covered by this session's investigation, that would need a separate
   audit and its own backfill.
3. **`docker`/Testcontainers is required** to run the new/updated integration tests; they were verified
   locally with Docker Desktop but were not run in this session's CI.

## Explicit confirmations

- **No RLS policies were weakened, disabled, or altered.** `tenant_isolation` on `sessions` is
  unchanged. The one new policy (`session_key_lookup`) is additive, `SELECT`-only, and OR-combined with
  the existing policy per Postgres's standard permissive-policy semantics.
- **No admin-mode or system-mode session write was introduced.** `SetAdminMode()`/`SetSystemMode()` are
  never called by the fix; every session write/read now happens after a tenant is resolved and
  `SwitchToTenantAsync` (`TenantContextMode.Tenant`) is applied, or (for `StoreAsync`) after the tenant
  is confirmed not `Suspended`/`Cancelled`.
- **No tenant id or slug is trusted from browser/request input** anywhere in this fix.
- **`onevo_app` remains `NOBYPASSRLS`** in both production tooling and the new test's setup.
