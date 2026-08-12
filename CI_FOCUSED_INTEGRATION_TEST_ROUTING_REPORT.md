# CI focused integration test routing

## Files changed

- `.github/workflows/ci.yml` — rewritten (see "Old vs new CI behavior" below).
- `.github/scripts/select-integration-filter.ps1` (new) — pure decision script; does not run
  `dotnet test` itself, only computes which filter (if any) the caller should use.
- `CI_FOCUSED_INTEGRATION_TEST_ROUTING_REPORT.md` (this file).

**Nothing else was touched.** No file under `src/**`, `tests/**`, migrations, Postman files, or
`OneVo-HR` docs was modified for this task. `git status --short` at the time of writing:

```
 M .github/workflows/ci.yml
 M tests/ONEVO.Tests.Integration/CoreHr/OnboardingDraft/OnboardingDraftsIntegrationTests.cs
?? .github/scripts/
```

The second line, `OnboardingDraftsIntegrationTests.cs`, is **pre-existing dirty state from an
earlier task in this same session** (a one-assertion fix to a stale integration test expectation,
done at the user's explicit request in a prior turn) — it is not part of this CI task, was not
touched by this task, and was not reverted (per instructions: report unrelated dirty files,
don't revert them). Confirmed scoped correctly with the exact commands requested during review:

```
$ git diff --stat -- .github/workflows/ci.yml .github/scripts/select-integration-filter.ps1 CI_FOCUSED_INTEGRATION_TEST_ROUTING_REPORT.md
 .github/workflows/ci.yml | 118 +++++++++++++++++++++++++++++++++++++++++++----
 1 file changed, 109 insertions(+), 9 deletions(-)

$ git status --short
 M .github/workflows/ci.yml
 M tests/ONEVO.Tests.Integration/CoreHr/OnboardingDraft/OnboardingDraftsIntegrationTests.cs
?? .github/scripts/
?? CI_FOCUSED_INTEGRATION_TEST_ROUTING_REPORT.md
```

The scoped diff touches only `ci.yml` (the `.ps1` and this report are new/untracked, so `git diff`
against a tracked baseline shows nothing to diff for them — `git status` is what shows they exist).
**Nothing has been committed** — per standing instructions, this task does not commit or push;
when the user is ready, only `.github/workflows/ci.yml`, `.github/scripts/select-integration-filter.ps1`,
and `CI_FOCUSED_INTEGRATION_TEST_ROUTING_REPORT.md` should be staged, not
`OnboardingDraftsIntegrationTests.cs`.

## Corrections applied after initial review

Two gaps were flagged in review of the first version of this change; both are now fixed:

1. **CoreHr/Employee/Onboarding had no mapped area** and fell through to the expensive
   full-integration fallback on every Phase-1 change (the area under active development). Added
   as its own area exactly as specified — paths and filter below, not narrowed or reinterpreted.
2. **Unit/Architecture-only changes ran full integration**, which is wasteful: neither suite
   touches Testcontainers/Postgres, and `build-and-test` already covers them on every trigger.
   Added a skip rule specifically for this case — but only when there is truly no `src/**` and no
   real `tests/ONEVO.Tests.Integration/**` file in the same change set; if an integration test
   file changed alongside a unit test file, the integration side still routes to its own area
   normally, it does not get skipped just because a unit test file also changed.

**A third, self-discovered bug surfaced while adding regression tests for #1's `tests/**/CoreHr/**`
pattern** (not something the user flagged, found while verifying the fix): `tests/**/<Area>/**`
as a single wildcard form silently misses areas whose real folder sits *directly* under
`tests/ONEVO.Tests.Integration/`, because PowerShell `-like`'s `*` still requires the literal `/`
on both sides of `<Area>` to be present in the string, and a direct child has no leading `/`
before `<Area>` once the fixed project-root prefix is consumed. This affects `Auth`,
`DevPlatform`, `Monitoring`, `Storage`, and the new `CoreHr` area — not `LegalEntity`/`Department`/
`Position`, which happen to sit one level deeper under `OrgStructure/` and so worked by accident.
Fixed by generating **both** the direct-child and nested forms for every area
(`New-IntegrationTestPathPatterns` helper), with 3 new regression-guard self-tests
(`Auth`/`Monitoring`/`Storage`) added specifically to lock this in, since those three were
silently broken from the first version of this change and nothing had caught it until CoreHr's
own regression test exposed the pattern class of bug.

## Old CI behavior

`ci.yml` had two jobs:
- `build-and-test`: build API (implicit restore) → unit tests → architecture tests. Always ran
  on every `pull_request`/`push` to `main`/`testing`/`development`.
- `integration-tests`: always ran the **entire** `ONEVO.Tests.Integration` suite
  (Testcontainers-backed, real PostgreSQL) on every PR and push. `continue-on-error: true`
  because of a documented pre-existing CSRF-handling gap. No diagnostics beyond default console
  output; no artifacts; no hang protection.

## New CI behavior

Three jobs, plus new top-level `workflow_dispatch` and nightly `schedule` triggers:

1. **`build-and-test`** — unchanged in intent, made more literal: explicit `dotnet restore` steps
   before each `dotnet build`/`dotnet test --no-restore` (previously restore was implicit inside
   `dotnet build`/`dotnet test`). Runs on every trigger (PR, push, schedule, workflow_dispatch).

2. **`integration-routing`** (new) — runs only `if: github.event_name == 'pull_request'`.
   - Checks out with `fetch-depth: 0` (needed to resolve the PR's merge-base for an accurate
     diff).
   - Computes changed files via `git diff --name-only "<base.sha>...<head.sha>"` (three-dot,
     merge-base diff — not two-dot — so files changed on `main` after the branch point aren't
     misattributed to the PR).
   - Writes the changed-file list to the job log and to `$GITHUB_STEP_SUMMARY`.
   - Calls `.github/scripts/select-integration-filter.ps1 -ChangedFilesPath changed-files.txt`
     (`shell: pwsh` — GitHub's `ubuntu-latest` runner ships PowerShell Core), which outputs
     `skip`, `full_integration`, `filter`, and `reason` to `$GITHUB_OUTPUT`, and a routing
     summary to `$GITHUB_STEP_SUMMARY`.
   - Runs **one** `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj` with
     either the computed `--filter` (focused) or no filter (full-integration fallback) —
     whichever the script decided — unless `skip == true`, in which case no integration test
     step runs at all.
   - Diagnostics on that one test invocation: `--logger trx --results-directory TestResults
     --blame-hang --blame-hang-timeout 10m`.
   - `.trx` + blame output uploaded via `actions/upload-artifact@v4` (`if: always()`, so both
     pass and fail runs get an artifact), named `integration-test-results-pr-<PR number>`.
   - `continue-on-error: true` preserved (same pre-existing CSRF gap as before — not
     re-litigated by this task).

3. **`full-integration`** (renamed from `integration-tests`) — runs
   `if: github.event_name == 'schedule' || github.event_name == 'workflow_dispatch' ||
   github.event_name == 'push'`. Always runs the **entire** suite, no filter — same diagnostics
   and artifact upload as above, named `integration-test-results-full-<run id>`.
   `continue-on-error: true` preserved.

**Deliberate choice, not explicitly requested but consistent with the goal:** `push` events to
`main`/`testing`/`development` (typically merge commits landing on a protected branch) route to
`full-integration`, not to the new focused routing. The task's required behavior list is
specifically about *pull requests*; a push to a protected branch already represents integrated,
reviewed code, so keeping full coverage there — rather than trying to diff a merge commit against
its own parent and possibly under-testing it — was judged the safer default. This is called out
explicitly here per the report's own requirement to state such calls.

## Routing logic (`select-integration-filter.ps1`)

Order of evaluation:

1. **No changed files at all** → skip (defensive; not expected in a real PR).
2. **Skip rule (docs/report/Postman/config-only):** if no changed file is under `src/` or
   `tests/` → skip integration entirely. This also naturally covers `.github/**` (including this
   task's own routing files), `ops/**`, root `README`/config files, etc.
3. **Skip rule (Unit/Architecture-only):** if there is at least one `tests/**` file but **none**
   under `src/**` and **none** under `tests/ONEVO.Tests.Integration/**` → skip integration. This
   fires only when every backend-relevant file is confined to `tests/ONEVO.Tests.Unit/**` and/or
   `tests/ONEVO.Tests.Architecture/**` — the moment a single `src/**` or
   `tests/ONEVO.Tests.Integration/**` file is also present, this rule does not apply and routing
   falls through to steps 4+ normally.
4. **Mapping table** (below): every changed file is checked against every area's path patterns
   (`-like` wildcards) and keyword substrings; every area that matches contributes its filter
   string to a deduplicated list. Areas' `tests/**` patterns only ever match under
   `tests/ONEVO.Tests.Integration/**` (never Unit/Architecture) — see "Corrections applied" above
   for why that distinction matters and how the direct-child-vs-nested folder-depth bug was found
   and fixed.
5. **Migrations:** if any `src/ONEVO.Infrastructure/Migrations/*` file changed:
   - If **no other area matched** — the migration is the only signal, so its scope is
     genuinely uncertain — escalate to **full integration**.
   - If **at least one other area matched** — there's corroborating evidence of the migration's
     scope — add the conservative filter
     `FullyQualifiedName~ApiBoot|FullyQualifiedName~Migration|FullyQualifiedName~DbContext` to the
     already-matched filter(s) instead of escalating.
6. **Combine:** if any filters were collected (from step 4 and/or step 5), OR-join them with `|`
   into a single filter string and run exactly one focused `dotnet test` (never more than one
   command, never a duplicate filter fragment — verified by the self-test).
7. **Otherwise:** backend/test source changed but matched nothing in the mapping table → **full
   integration** (safe fallback), per the explicit instruction "if backend source changed but no
   mapping is confident, run full integration."

### Path-to-filter table

All path patterns and keywords below were checked against the real namespaces/classes in
`tests/ONEVO.Tests.Integration` (not guessed) — see the "Real integration test inventory" section.
`src/**` patterns are shown below; every area also carries a matching pair of
`tests/ONEVO.Tests.Integration/<Area>/*` (direct child) and `tests/ONEVO.Tests.Integration/*/<Area>/*`
(nested) patterns generated by the `New-IntegrationTestPathPatterns` helper, omitted from this
table for readability — see the script itself for the exact generated list.

| Area | `src/**` path patterns | Keyword triggers (substring, anywhere in path) | Filter |
|---|---|---|---|
| Auth/Legal/Session/Password/MFA | `src/ONEVO.Api/Controllers/*/Auth/*`, `src/ONEVO.Api/Controllers/*/Legal/*`, `src/ONEVO.Application/Features/Auth/*`, `src/ONEVO.Application/Features/Legal/*`, `src/ONEVO.Infrastructure/*/Auth/*` | `Session`, `Csrf`, `Ticket`, `PasswordReset`, `Mfa`, `LegalAcceptance` | `FullyQualifiedName~Auth\|FullyQualifiedName~Legal\|FullyQualifiedName~Password\|FullyQualifiedName~Mfa\|FullyQualifiedName~Session` |
| DevPlatform/Admin/TenantProvisioning | `src/ONEVO.Api/Controllers/Admin/*`, `src/ONEVO.Application/Features/DevPlatform/*`, `src/ONEVO.Infrastructure/Services/DevPlatform/*`, `src/ONEVO.Infrastructure/Persistence/Seeders/*` | — | `FullyQualifiedName~DevPlatform\|FullyQualifiedName~Admin\|FullyQualifiedName~TenantProvisioning\|FullyQualifiedName~ApiBoot` |
| Legal Entity | `src/*/LegalEntity/*`, `src/*/LegalEntities/*`, `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs` | — | `FullyQualifiedName~LegalEntit` |
| Department | `src/*/Department/*`, `src/*/Departments/*`, `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs` | — | `FullyQualifiedName~Department` |
| Position / management coverage | `src/*/Position/*`, `src/*/Positions/*`, `src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs` | `ManagementCoverage` | `FullyQualifiedName~Position` |
| Monitoring/Tray/Agent | `src/*/Monitoring/*`, `src/*/Tray/*` | `TrayActivation`, `TrayDevice`, `EmployeeCheckIn`, `MonitoringFaceScan` | `FullyQualifiedName~Monitoring\|FullyQualifiedName~Tray\|FullyQualifiedName~CheckIn` |
| Storage/File | `src/*/Storage/*`, `src/*/File/*` | `FileStorage`, `FileRecord`, `UploadReservation` | `FullyQualifiedName~Storage\|FullyQualifiedName~File` |
| **Core HR / Employee / Onboarding** *(added per correction)* | `src/**/CoreHr/**`, `src/**/Employee/**`, `src/**/Employees/**`, `src/**/Onboarding/**` | — | `FullyQualifiedName~CoreHr\|FullyQualifiedName~Employee\|FullyQualifiedName~Onboarding` |
| Migrations (conservative, always added when a migration changed) | `src/ONEVO.Infrastructure/Migrations/*` | — | `FullyQualifiedName~ApiBoot\|FullyQualifiedName~Migration\|FullyQualifiedName~DbContext` |

### Real integration test inventory the table was checked against

`tests/ONEVO.Tests.Integration` currently has these top-level namespaces/classes (non-exhaustive,
the ones each filter is grounded in):

- `ONEVO.Tests.Integration.Auth.*` — `BaseDomainLoginIntegrationTests`, `MfaChallengeStoreConcurrencyTests`,
  `PasswordResetTokenRepositoryConcurrencyTests`, `TenantSessionRlsIntegrationTests`,
  `PlatformAdminAuthIntegrationTests`, etc. — all match `~Auth`; `~Password`/`~Mfa`/`~Session`
  additionally catch class-name substrings.
- `ONEVO.Tests.Integration.Features.DevPlatform.Compliance.LegalDocumentRichContentIntegrationTests`
  — matches `~DevPlatform` (namespace) and `~Legal` (class name).
- `ONEVO.Tests.Integration.Security.AuthLookupBaseLoginCandidatesFunctionTests` — matches `~Auth`
  (class name), correctly pulled into the Auth area even though its namespace is `Security`.
- `ONEVO.Tests.Integration.DevPlatform.ConfigurationTemplateManagerIntegrationTests` — matches
  `~DevPlatform`.
- `ONEVO.Tests.Integration.Tenancy.TenantsAdminApiIntegrationTests` — matches `~Admin`.
- `ONEVO.Tests.Integration.E2E.TenantProvisioningE2ETests` — matches `~TenantProvisioning`.
- `ONEVO.Tests.Integration.ApiBootTests` — matches `~ApiBoot`.
- `ONEVO.Tests.Integration.OrgStructure.LegalEntity.LegalEntitiesIntegrationTests` — matches
  `~LegalEntit`.
- `ONEVO.Tests.Integration.OrgStructure.Department.DepartmentsIntegrationTests` — matches
  `~Department`.
- `ONEVO.Tests.Integration.OrgStructure.Position.PositionsIntegrationTests`,
  `PositionMigrationSafetyIntegrationTests`, and
  `ONEVO.Tests.Integration.CoreHr.PositionAssignment.PositionAssignmentRlsIntegrationTests` —
  all match `~Position`.
- `ONEVO.Tests.Integration.Monitoring.ActivityMonitoring.ActivityIngestIntegrationTests`,
  `Monitoring.TrayActivation.TrayActivationIntegrationTests`,
  `Monitoring.CheckIn.CheckInIntegrationTests` — match `~Monitoring`/`~Tray`/`~CheckIn`
  respectively.
- `ONEVO.Tests.Integration.Storage.StorageQuotaIntegrationTests`,
  `Storage.File.FileStorageIntegrationTests` — match `~Storage`/`~File`.
- `ONEVO.Tests.Integration.CoreHr.Employee.EmployeesListIntegrationTests`,
  `CoreHr.OnboardingDraft.OnboardingDraftsIntegrationTests`,
  `CoreHr.PositionAssignment.PositionAssignmentRlsIntegrationTests` — all match the new `~CoreHr`
  area (namespace); `EmployeesListIntegrationTests`/`OnboardingDraftsIntegrationTests` also match
  `~Employee`/`~Onboarding` by class name. `PositionAssignmentRlsIntegrationTests` additionally
  still matches `~Position` (both areas match; harmless overlap, deduplicated into one filter).

Not reachable by any mapped area today (only run under full integration):
`Features.WorkManagement.CreateProjectEndpointTests`,
`Integrations.UserIntegrationConnectionPersistenceTests`,
`Security.RestrictedRoleRlsEnforcementTests`, `Support.*`. This is expected — the task's mapping
table has no Work Management or generic-integrations area, so PRs touching only those paths fall
to the "backend source changed but no mapping is confident → full integration" rule. (CoreHr/
Employee/Onboarding, previously in this list, is now covered — see "Corrections applied" above.)

## Examples

| Scenario | Result |
|---|---|
| Only `src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs` changed | Focused: `FullyQualifiedName~Department` |
| `.../Department/Commands/CreateDepartment.cs` **and** `.../Position/Commands/CreatePosition.cs` changed | Focused, single command: `FullyQualifiedName~Department\|FullyQualifiedName~Position` |
| `src/ONEVO.Application/Features/Auth/Login/LoginCommandHandler.cs` changed | Focused: `FullyQualifiedName~Auth\|FullyQualifiedName~Legal\|FullyQualifiedName~Password\|FullyQualifiedName~Mfa\|FullyQualifiedName~Session` |
| `src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/.../ListOnboardingAccessGrantRequestsQueryHandler.cs` changed | Focused: `FullyQualifiedName~CoreHr\|FullyQualifiedName~Employee\|FullyQualifiedName~Onboarding` |
| Only `EMPLOYEE_ONBOARDING_APPROVE_SEND_INVITE_REPORT.md` + `ONEVO-HRMS.postman_collection.json` changed | **Skip** — no integration step runs at all |
| Only `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ListOnboardingAccessGrantRequestsQueryHandlerTests.cs` changed | **Skip** — build-and-test already covers unit tests, no Postgres involved |
| `tests/ONEVO.Tests.Unit/...` **and** `tests/ONEVO.Tests.Architecture/...` changed together (still nothing under `src/` or Integration) | **Skip** — same reasoning, neither suite needs integration |
| A real `tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs` change | Focused: `FullyQualifiedName~Department` — **not** skipped, this is an actual integration test file |
| Unit test file changed **and** a real Integration test file changed in the same PR | The Unit-only skip rule does **not** fire (an Integration file is present); routes normally to `FullyQualifiedName~Department` (or whichever area the Integration file belongs to) |
| Only `src/ONEVO.Infrastructure/Migrations/20260811000000_AddSomething.cs` changed (no other mapped path) | **Full integration** — migration scope uncertain |
| `src/ONEVO.Infrastructure/Migrations/20260811000000_AddDepartmentColumn.cs` **and** `DepartmentsController.cs` changed together | Focused: `FullyQualifiedName~Department\|FullyQualifiedName~ApiBoot\|FullyQualifiedName~Migration\|FullyQualifiedName~DbContext` |
| `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject.cs` changed (backend source, no mapped area) | **Full integration** — safe fallback |

All of the above are asserted by `select-integration-filter.ps1 -SelfTest` — **19 assertions
total** (up from 9 in the first version), including the 3 direct-child-folder regression guards
found while implementing the corrections. This is the "unit-like sanity check" required by the
task, run directly against the script's decision function with no git/CI context needed.

## Diagnostics and artifacts

Every `dotnet test tests/ONEVO.Tests.Integration/...` invocation (focused, full-fallback, or
nightly full) uses:
```
--logger trx --results-directory TestResults --blame-hang --blame-hang-timeout 10m
```
Verified this exact flag combination is accepted by the SDK in this environment (10.0.300) by
running it against `ONEVO.Tests.Unit` with a filter matching zero tests (fast, no Docker needed)
— it produced a `.trx` file and a clean "Blame" data-collector message with no argument-parsing
error.

`actions/upload-artifact@v4` runs with `if: always()`, so a `.trx` (and any hang-dump/sequence
files under `TestResults/`) is captured whether the run passes, fails, or times out — the
`--blame-hang` collector is specifically what makes a hang produce a diagnosable artifact instead
of just the job silently exceeding the runner's timeout.

## What was not changed

- No file under `src/**`, `tests/**`, migrations, Postman files (`ONEVO-HRMS.postman_collection.json`,
  `.postman/**`, `postman/**`), or `OneVo-HR` docs.
- No product/backend behavior.
- The pre-existing CSRF `continue-on-error: true` gap — carried forward unchanged on both
  `integration-routing` and `full-integration`, not re-investigated (out of scope for this task).
- `CODEOWNERS` — untouched, not relevant to this task.

## Validation

1. **PowerShell script syntax** — parsed (not executed) with the .NET parser directly, per the
   task's "parse the script with PowerShell parser, do not execute destructive commands"
   instruction:
   ```powershell
   [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
   ```
   Result: **0 syntax errors.** Only Windows PowerShell 5.1 (`powershell.exe`) is available in
   this local environment (no `pwsh`/PowerShell 7), so the script deliberately avoids PS7-only
   syntax (`??`, `?.`, ternary, `-AsHashtable`) that would parse-fail under 5.1 even though the
   CI runner (`ubuntu-latest`, which ships `pwsh`) would accept it — this was checked directly,
   re-verified again after the corrections (the `+` array-concatenation pattern used by the new
   `New-IntegrationTestPathPatterns` helper is 5.1-safe too).
   Additionally ran the script's own `-SelfTest` mode: **19/19 assertions passed** (grown from 9
   in the first version — 4 new assertions cover the CoreHr area and the Unit/Architecture-only
   skip rule directly from the correction, and 3 more are regression guards for the direct-child
   folder-depth bug described in "Corrections applied" above, found and fixed while validating
   the first two). The self-test harness itself was verified to actually fail correctly — a
   deliberately-wrong assertion was injected temporarily, confirmed it printed `FAIL` and the
   script exited with code 1, then reverted before re-running the real suite (needed because an
   early version of the harness had a variable-scoping bug that silently swallowed failures; this
   was caught and fixed before landing, prior to the correction round).

2. **YAML sanity check — no linter available, stated plainly as instructed.** Checked for
   `actionlint`, `yamllint`, `yq`, and `python3` on `PATH`: none present. Also checked whether
   `YamlDotNet` (a real YAML parser) was already sitting in the local NuGet cache from some other
   project's dependencies: not present either. What was actually done instead: visual structural
   review, plus confirming there are no tab characters anywhere in the file (`grep` for `\t`) and
   that indentation is consistent 2-space nesting throughout (spot-checked every `key:` line's
   indent level). This is **not** a substitute for real YAML/schema validation — the authoritative
   check is GitHub Actions' own parser when the workflow is pushed, which was not exercised here
   since this task does not commit or push.

3. **Required dotnet commands:**
   - `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal` → **0
     errors.**
   - `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --no-build
     --verbosity minimal` → **1923/1923 passed.**
   - `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore
     --no-build --verbosity minimal` → **555/555 passed.**
   - These numbers match the last verified state from earlier in this session — this task
     touched no `src/**`/`tests/**` file, so no change was expected or found.

4. **One focused integration sample (Docker was available in this environment):** built
   `tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj`, then ran the exact
   Department-filter command shape the new CI job would run:
   ```
   dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --no-build
     --filter "FullyQualifiedName~Department" --logger trx --results-directory TestResults-sample
     --blame-hang --blame-hang-timeout 10m
   ```
   Result: **60/60 passed** (17m21s — real Testcontainers PostgreSQL, not faked/InMemory; the
   duration includes container pull/startup). A `.trx` file was produced at
   `TestResults-sample/User_*.trx`, confirming the diagnostics flags produce real output. The
   `TestResults-sample/` directory was a local-only verification artifact and was deleted after
   confirming its contents — it is not part of the delivered change (`git status` confirms it left
   no trace).

5. **`git diff --check`** → exit 0; only pre-existing LF→CRLF warnings (Windows checkout
   artifact) on `.github/workflows/ci.yml` and the unrelated `OnboardingDraftsIntegrationTests.cs`
   file from the earlier session task — no real whitespace/conflict errors.

## Remaining risks

1. **No real YAML/schema validation was possible in this environment** (see Validation #2). A
   typo that's syntactically valid YAML but semantically wrong for the Actions schema (e.g. a
   misspelled `if:` expression) would not be caught until the workflow actually runs on GitHub.
2. **Push events to `main`/`testing`/`development` always run full integration**, not the new
   focused routing — a deliberate, documented choice (see "New CI behavior" above), but worth the
   user's explicit sign-off since it wasn't literally spelled out in the task.
3. **The pre-existing CSRF `continue-on-error: true` gap is unresolved** — unchanged by this
   task, exactly as instructed, but still means integration failures (focused or full) don't block
   merges today.
4. **`Features.WorkManagement.*`, `Integrations.*`, `Security.RestrictedRoleRlsEnforcementTests`,
   and `Support.*` integration tests still have no mapped area** and fall to the full-integration
   safe fallback whenever a PR touches only those paths. Not a correctness bug, just means those
   specific paths don't get the routing speed benefit — no user instruction covers adding areas
   for them, so left as-is rather than guessed at.
5. **The direct-child-vs-nested folder-depth bug described in "Corrections applied" was caught
   and fixed for all 8 areas in this change**, but if a *new* area is added later without using
   the `New-IntegrationTestPathPatterns` helper (e.g. someone hand-writes a single `*/<Name>/*`
   pattern the way the first version of this script did), the same silent-miss bug could recur.
   Documented directly in the helper function's own comment as the reason it exists, to make this
   easy to avoid next time.
