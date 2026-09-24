# work_modes Permission/Grant Fix

## Root cause

**Test fixture bug, not a migration/grant/RLS gap.**

`OnboardingDraftsIntegrationTests.CreateRestrictedRoleAsync()`
(`tests/ONEVO.Tests.Integration/CoreHr/OnboardingDraft/OnboardingDraftsIntegrationTests.cs`)
creates its own dedicated Postgres role (`onboarding_drafts_rls_test_role`, not `onevo_app`) for
this test class and hand-grants it `SELECT` on an explicit, enumerated list of tables. That list
included `employment_types` and `employment_statuses` — siblings of `work_modes` created in the
same migration — but omitted `work_modes` itself:

```sql
GRANT SELECT ON tenants, legal_entities, departments, positions, employees,
    users, employment_types, employment_statuses, position_access_templates,
    tenant_subscriptions TO onboarding_drafts_rls_test_role;
```

`SaveOnboardingDraftCommandHandler.Handle` (line 50) calls
`_workModeRepository.ExistsActiveAsync(request.WorkModeId, ct)` before anything else, which runs
`SELECT ... FROM work_modes WHERE id = @p AND is_active` under the connection built from this
restricted role (`CreateContext(useRestrictedRole: true)`). With no grant on `work_modes`,
Postgres returned `42501: permission denied for table work_modes` — the exact error and exact
call site (`SaveOnboardingDraftCommandHandler.cs:50` → `IWorkModeRepository.ExistsActiveAsync`)
reported by the prior session.

## Is `work_modes` a global lookup, tenant-owned RLS table, or special reference table?

**Global lookup table, readable by the authenticated app role — same category as
`employment_statuses`, `employment_types`, `approval_statuses`, `severities`.**

Evidence:

- All five tables are created together, identically, in
  `src/ONEVO.Infrastructure/Migrations/20260519061316_AddLookupTables.cs`: `int id` PK, `code`
  (unique), `label` — no `tenant_id` column on any of them.
- Searched every RLS-related migration (`20260515022320_AddRlsPolicies.cs`,
  `20260719180411_AddMissingRlsPolicies.cs`, `20260726174515_AddSessionKeyHashRlsLookupPolicy.cs`,
  etc.) for any of the five table names — no matches. None of the five lookup tables has
  `ENABLE ROW LEVEL SECURITY` or a `CREATE POLICY` anywhere in the migration history.
- `WorkModeConfiguration` (`src/ONEVO.Infrastructure/Persistence/Configurations/Lookups/Common/WorkModeConfiguration.cs`)
  is a plain EF config: no tenant discriminator, no query filter.
- Production/dev grant path (`ops/postgres/local-bootstrap-roles.sql`) grants `onevo_app`
  `SELECT, INSERT, UPDATE, DELETE` on **all** tables in `public` via
  `GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO onevo_app` plus
  `ALTER DEFAULT PRIVILEGES FOR ROLE onevo_migrator ... GRANT SELECT ... TO onevo_app` for
  future tables — this covers all five lookup tables uniformly. There is **no** product-side
  gap and no special-case grant pattern for any one lookup table.

So `work_modes` needed no new migration, no RLS policy, and no elevated/admin query mode —
exactly the outcome the task's constraints pointed at.

## Why this only affected this one test class

`IntegrationDatabaseBootstrap` (used by most other integration suites) migrates using the
Testcontainers **admin/superuser** connection, not `onevo_migrator`, and never replicates
`ops/postgres/local-bootstrap-roles.sql`'s `ALTER DEFAULT PRIVILEGES` / blanket
`GRANT ... ON ALL TABLES` statements — `PrivilegedRoleTestBootstrap.EnsureRolesExistAsync` only
`CREATE ROLE`s `onevo_app`/`onevo_migrator`/`onevo_auth_base_login_fn_owner`, it grants nothing.
Several integration test classes (this one, `EmployeesListIntegrationTests`,
`PositionAssignmentRlsIntegrationTests`, `RestrictedRoleRlsEnforcementTests`,
`FileStorageIntegrationTests`) instead hand-roll a **second**, test-specific restricted role with
an explicit per-table `GRANT SELECT` allowlist to exercise RLS/permission behavior realistically.
That hand-rolled, enumerated-allowlist pattern is what's omission-prone — `work_modes` was simply
missed when this fixture's list was written (it already existed before the new
`GET /api/v1/work-modes` endpoint work; `ExistsActiveAsync` was already on this call path).

`EmployeesListIntegrationTests.cs` (lines 276-279) has the same grant list shape and also omits
`work_modes`, but that suite never queries `WorkMode` under its restricted role today, so it's a
latent risk, not a live bug — **not fixed here**, flagged only.

## Fix applied

One line, in the test fixture only — no production code, no migration:

`tests/ONEVO.Tests.Integration/CoreHr/OnboardingDraft/OnboardingDraftsIntegrationTests.cs`,
`CreateRestrictedRoleAsync()`:

```diff
- GRANT SELECT ON tenants, legal_entities, departments, positions, employees,
-     users, employment_types, employment_statuses, position_access_templates,
-     tenant_subscriptions TO {RestrictedRoleName};
+ GRANT SELECT ON tenants, legal_entities, departments, positions, employees,
+     users, employment_types, employment_statuses, position_access_templates,
+     tenant_subscriptions, work_modes TO {RestrictedRoleName};
```

Before finalizing, the full `SaveOnboardingDraftCommandHandler` call path was traced end to end
(not just the table named in the stack trace) to confirm no other table was missing from the
grant list under the restricted role: `legal_entities`, `departments`, `positions` (incl.
`position_access_templates` via `GetAccessTemplateByPositionAsync`), `employees` (email/number
uniqueness checks), `tenant_subscriptions` + `employees` (via `SeatEntitlementService.EvaluateAsync`),
`onboarding_drafts` (draft repository read/write) — all already present. The test context uses
`DomainEventDispatchInterceptor(new NoOpPublisher())`, so no `outbox_messages` write occurs on
this path. `set_config`, used by `TenantRlsInterceptor` to set the RLS session variable, is a
Postgres built-in requiring no grant.

## Integration test result

`dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~OnboardingDraft"`

- **Before fix:** 4/4 failed, all with `Npgsql.PostgresException: 42501: permission denied for
  table work_modes` — as recorded in the prior session's
  `EMPLOYEE_ONBOARDING_WORK_MODE_LOOKUP_REPORT.md` (not re-run against the pre-fix state this
  session; the fix was applied first, per the task's instructions, based on that report plus this
  session's own trace of the grant list against the handler's call path).
- **After fix (measured this session):** 3/4 passed. The `42501` error is completely gone from
  all four tests.
  - `Handle_NeverCreatesAUserRow_WhenSavingADraft` — passed
  - `ResumingADraft_ReturnsTheLastSavedStep` — passed
  - `ConcurrentSaveWithStaleIfMatchVersion_ReturnsConflict` — passed
  - `Handle_AlwaysResultsInADraftStatus_NeverFinalized` — **failed, but on an unrelated,
    pre-existing assertion bug**, not a permission error (see below).

### Unrelated pre-existing failure surfaced by this fix (not fixed, out of scope)

`Handle_AlwaysResultsInADraftStatus_NeverFinalized` (same file, lines 96-106) asserts:

```csharp
result.Value.Status.Should().Be("waiting_for_seat", "the seat service always returns Undetermined today");
```

But `SaveOnboardingDraftCommandHandler` maps `SeatDecisionStatus.Undetermined` → status
`"draft"` (`OnboardingDraftStatus.Draft`, reason `SeatConfigurationRequired`); it only maps
`SeatDecisionStatus.Blocked` → `"waiting_for_seat"`. No `TenantSubscription` row is seeded for
this test's tenant, so `SeatEntitlementService.EvaluateAsync` correctly returns `Undetermined`
(matching the test's own comment) and the handler correctly returns `"draft"`. The test's
*assertion string* contradicts its own stated reasoning and the handler's actual, correct
mapping. This was invisible before because all 4 tests were failing earlier at the `work_modes`
permission check (line 50), before ever reaching this status assertion. This is a separate,
pre-existing test bug unrelated to grants/permissions/RLS — flagged here, left unfixed per the
task's scope (`work_modes` permission/grant issue only).

**Confirmed which side is stale, without changing it:**
`tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDraft/SaveOnboardingDraftCommandHandlerTests.cs`
has a passing, intentional unit test —
`Handle_SavesDraftWithSeatConfigurationRequired_WhenSeatDecisionIsUndetermined` — that asserts
`SeatDecisionStatus.Undetermined` maps to `OnboardingDraftStatus.Draft` +
`OnboardingDraftReason.SeatConfigurationRequired`, matching the handler exactly. The handler's
`Undetermined → "draft"` mapping is therefore deliberate and already covered. The integration
test's `.Should().Be("waiting_for_seat", ...)` on line 105 is the stale side; the concrete,
one-line follow-up fix (not applied here, out of scope) is changing that expected string to
`"draft"`.

## Other verification

- Focused onboarding + work-mode unit tests
  (`dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~OnboardingDraft|FullyQualifiedName~WorkMode"`):
  **62/62 passed.**
- Full architecture test suite (`dotnet test tests/ONEVO.Tests.Architecture`): **555/555 passed.**
- `git diff --check`: **exit code 0** — only pre-existing LF→CRLF warnings (Windows checkout
  artifact on already-modified files), no actual whitespace/conflict errors.

## Remaining risks

- `EmployeesListIntegrationTests.cs` has the same enumerated-grant-list pattern and also omits
  `work_modes`. Not currently exercised (no `WorkMode` query under its restricted role), so not a
  live bug, but will break the same way if that suite ever joins/queries `WorkMode`. Same fix
  shape would apply if/when it does.
- The underlying pattern — hand-rolled, per-test-class allowlists of `GRANT SELECT` statements
  instead of one shared helper mirroring `ops/postgres/local-bootstrap-roles.sql`'s blanket
  `GRANT ... ON ALL TABLES` — is inherently omission-prone. Not changed here (out of scope: the
  task asked for the smallest fix to `work_modes` specifically, and consolidating this pattern
  across ~5 test classes is a larger refactor with its own risk).
- `Handle_AlwaysResultsInADraftStatus_NeverFinalized`'s assertion bug (above) remains unfixed and
  will continue to fail focused/full integration runs of this test class until corrected
  separately.

No migration was added. No RLS policy was changed. No tenant RLS was weakened. The repository
check (`ExistsActiveAsync`) was not bypassed. No elevated/admin query mode was introduced.
