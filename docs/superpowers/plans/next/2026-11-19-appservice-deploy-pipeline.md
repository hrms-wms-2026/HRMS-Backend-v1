# App Service Deploy Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A GitHub Actions workflow in HRMS-Backend-v1 that builds backend + web + admin into the single-host App Service package and deploys it to `onexso-api`. It runs on merges to `main` in any of the three repos and has a Run button, plus an opt-in migration step.

**Architecture:** One deploy workflow in the backend repo. It checks out the two frontend repos, runs the existing `ops/appservice/build-appservice-package.ps1` on `ubuntu-latest`, deploys with `azure/webapps-deploy@v3` and a publish profile, then polls `/health/ready`. Each frontend repo gets a tiny workflow that sends a `repository_dispatch` to the backend repo.

**Tech Stack:** GitHub Actions, PowerShell 7 (`pwsh` on ubuntu runners), .NET 10 SDK, Node 22 + Angular 21 CLI, EF Core migration bundles, `sqlcmd`.

**Spec:** `docs/superpowers/specs/next/2026-11-19-appservice-deploy-pipeline-design.md` (approved 2026-11-19).

## Global Constraints

- **Repos and branches.**
  - Org: `hrms-wms-2026`. Repos: `HRMS-Backend-v1`, `Hrms--Web-application---front-end---v1`, `HRMS-Platform-Administration-Front-End-v1`.
  - Deploy branch in all three: `main`.
- **Where to work.**
  - Backend: `C:\onevoNew\HRMS-Backend-v1-singlehost`, branch `feature/appservice-deploy-pipeline`, already cut from `origin/main`.
  - Frontends: their `-singlehost` clones, on a new branch `feature/appservice-deploy-trigger` cut from `origin/main`.
  - Never touch `C:\onevoNew\<repo>`. Never use git worktrees.
- **App:** `onexso-api`.
- **Names.**
  - Secrets: `AZURE_WEBAPP_PUBLISH_PROFILE`, `FRONTEND_READ_TOKEN`, `MIGRATION_CONNECTION_STRING` (backend); `BACKEND_DISPATCH_TOKEN` (frontends).
  - Repository variables: `APPSERVICE_HOST`, `PACKAGE_MODE` (default `SingleHost` when unset).
  - Dispatch event type: `deploy-appservice`. Concurrency group: `deploy-appservice`, with `cancel-in-progress: false`.
- **No tests in the deploy job.** `ci.yml` already tests PRs.
- **No secret values** in YAML, logs or committed files. Connection strings go to steps only through `env:` from `secrets.*`.
- **Pin every third-party action** to a major version tag (`actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/setup-node@v4`, `azure/webapps-deploy@v3`).
- **Migrations run only** when the manual input `apply_migrations` is `true`. Never on push or dispatch.

## Review Focus

1. **The package script on Linux.** It uses `$env:TEMP` (unset on Linux) and `'..\..'` path joins. It must run under `pwsh` on ubuntu and still on Windows. Task 1 runs it on Windows; Task 2's lint and the first real run cover Linux. If Docker is available, Task 1 also runs it in a `mcr.microsoft.com/powershell` container for a parse and path smoke test.
2. **A failed deploy that looks green.** `webapps-deploy` can succeed while the app crashes on start. The health step must fail the job after 5 minutes without a 200. Covered in Task 2.
3. **A secret printed to the log.** `MIGRATION_CONNECTION_STRING` must never be echoed, and the migration step must not use `set -x`. Task 3 checks this with a grep in its verification.
4. **Two merges in quick succession.** The second run waits; it never cancels a deploy halfway. This is set by concurrency in Task 2.
5. **Manual deploy of a non-main frontend ref.** The `web_ref` and `admin_ref` inputs are used for checkout, and the run summary shows the SHAs actually deployed. Task 2.

---

### Task 1: Make the package script CI-ready

**Files:**
- Modify: `ops/appservice/build-appservice-package.ps1`

**Interfaces — Produces:**
- Same parameters as today (`-Mode`, `-WebRepo`, `-AdminRepo`, `-OutDir`, `-ZipName`).
- The work folder comes from `[IO.Path]::GetTempPath()`.
- The backend root resolves via `Join-Path $PSScriptRoot '..' '..'` (or an equivalent that works on both separators).
- When `$env:GITHUB_OUTPUT` is set, the script appends `zip_path=<full path>` and `zip_sha256=<hex>` to it.
- When `$env:GITHUB_STEP_SUMMARY` is set, it appends a short markdown block: mode, zip name, SHA-256.

- [ ] **Step 1:** Make the changes. Behaviour on Windows without those env vars is unchanged.
- [ ] **Step 2: Verify on Windows.** Run `pwsh ops/appservice/build-appservice-package.ps1 -OutDir <scratch> -ZipName test.zip`. Expect the zip to be created and the SHA printed. Inside the zip, `wwwroot/admin-console/index.html` has `<base href="/admin-console/">`, `wwwroot/web/index.html` has `<base href="/">`, and `ONEVO.Api.dll` is at the root.
- [ ] **Step 3: Verify the GitHub outputs.** Set `$env:GITHUB_OUTPUT` and `$env:GITHUB_STEP_SUMMARY` to temp files and rerun. Both files get the documented lines.
- [ ] **Step 4: Linux smoke test, if Docker runs.** `docker run --rm -v <repo>:/repo mcr.microsoft.com/powershell:7.4-ubuntu-22.04 pwsh -NoProfile -Command "& /repo/ops/appservice/build-appservice-package.ps1 -WebRepo /nope -AdminRepo /nope"`. Expect it to fail only with `Not an Angular repo (no angular.json): /nope`. That proves it parses and resolves paths on Linux. If Docker isn't running, record "skipped".
- [ ] **Step 5:** Run `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -c Release`. Expect it to pass: an architecture test scans `ops/` for machine-specific paths.
- [ ] **Step 6:** Commit `ops(appservice): package script runs on GitHub's Linux runners and reports zip path and hash`.

### Task 2: Deploy workflow

**Files:**
- Create: `.github/workflows/deploy-appservice.yml`

**Interfaces:** Consumes Task 1's `zip_path` / `zip_sha256` step outputs.

**Workflow shape:**
- **`on:`**
  - `push: branches: [main]`
  - `repository_dispatch: types: [deploy-appservice]`
  - `workflow_dispatch` with inputs:
    - `backend_ref`, `web_ref`, `admin_ref` (string, default `main`)
    - `apply_migrations` (boolean, default `false`)
- **`permissions: contents: read`**
- **One job, `deploy`:**
  - `runs-on: ubuntu-latest`, `environment: appservice-test`, `timeout-minutes: 40`
  - the concurrency block from Global Constraints
- **Steps:**
  1. Checkout the backend at `inputs.backend_ref || github.sha` into `backend/`.
  2. Checkout the web and admin repos at `inputs.web_ref || 'main'` / `inputs.admin_ref || 'main'` into `web/` and `admin/`, with `token: ${{ secrets.FRONTEND_READ_TOKEN }}`.
  3. Run `setup-dotnet` (`10.0.x`) and `setup-node` (`22`, npm cache keyed on both `package-lock.json` files).
  4. `npm ci --no-audit --no-fund` in `web/` and `admin/`.
  5. Run Task 3's migration steps (only when `inputs.apply_migrations`).
  6. `pwsh backend/ops/appservice/build-appservice-package.ps1 -Mode ${{ vars.PACKAGE_MODE || 'SingleHost' }} -WebRepo web -AdminRepo admin -OutDir ${{ runner.temp }} -ZipName crownus-appservice.zip` (with `id: package`).
  7. `azure/webapps-deploy@v3` with `app-name: onexso-api`, `publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}`, `package: ${{ steps.package.outputs.zip_path }}`.
  8. **Health check.** Poll `https://${{ vars.APPSERVICE_HOST }}/health/ready` every 15 s for up to 5 minutes (`curl -fsS -o /dev/null -w '%{http_code}'`). Exit 1 with a clear message if it never returns 200.
  9. **Summary.** Append the three checked-out SHAs (`git -C <dir> rev-parse HEAD`) and the trigger (`github.event_name`) to `$GITHUB_STEP_SUMMARY`.
- **Fail early with a clear message** if `APPSERVICE_HOST` or the publish profile secret is empty: a first step checks `vars`/`secrets` presence through `env`.

- [ ] **Step 1:** Write the workflow.
- [ ] **Step 2: Lint.** Run `docker run --rm -v "<repo>:/repo" -w /repo rhysd/actionlint:1.7.7 -color .github/workflows/deploy-appservice.yml`. Expect no errors. If Docker isn't running, use `npx --yes @action-validator/cli` or record "lint skipped" and review by hand against the shape above.
- [ ] **Step 3:** Commit `ci(appservice): deploy workflow for onexso-api on main merges and Run button`.

### Task 3: Opt-in database migration step

**Files:**
- Modify: `.github/workflows/deploy-appservice.yml` (steps guarded by `if: ${{ inputs.apply_migrations }}`, placed before packaging)
- Read first: `ops/sqlserver/post-migration-grants.sql`. Confirm the migrator login has the rights it needs. On the VM it was run with Windows auth; here it runs as `crownus_migrator`. If the grants need more than `db_owner` of `CrownusDB_test`, stop and report rather than widen anything.

**Steps the workflow runs:**
1. `dotnet tool install --global dotnet-ef` (pin the version matching the EF Core package in `src/ONEVO.Infrastructure`).
2. `dotnet ef migrations bundle -p backend/src/ONEVO.Infrastructure -s backend/src/ONEVO.Api -r linux-x64 --self-contained -o ${{ runner.temp }}/efbundle`.
3. Run the bundle from `backend/src/ONEVO.Api`, because the design-time factory needs `appsettings.json` there. Set `env: ConnectionStrings__MigrationConnection: ${{ secrets.MIGRATION_CONNECTION_STRING }}` and pass `--connection "$ConnectionStrings__MigrationConnection"`.
4. Grants: use `sqlcmd` (install `go-sqlcmd` if it's not on the runner image). Pass the server, user and password from the connection string via env, never on the logged command line, plus `-C -b -i backend/ops/sqlserver/post-migration-grants.sql -v DB_NAME=CrownusDB_test`. Prefer parsing the connection string in a `pwsh` step with `SqlConnectionStringBuilder`.

- [ ] **Step 1:** Add the steps.
- [ ] **Step 2: Lint** as in Task 2.
- [ ] **Step 3: Leak check.** `grep -nE 'set -x|echo .*MIGRATION|Write-Host .*Password' .github/workflows/deploy-appservice.yml` must return nothing.
- [ ] **Step 4: Local bundle check.** Run step 2's `dotnet ef migrations bundle` command locally with `-r win-x64`. Expect the bundle to be created and `--help` to run. Do **not** run it against any database: CLAUDE.md forbids applying migrations to a shared database.
- [ ] **Step 5:** Commit `ci(appservice): opt-in EF migration bundle and grants on manual deploy`.

### Task 4: Frontend trigger workflows

**Files:**
- Create in each frontend clone: `.github/workflows/trigger-appservice-deploy.yml`

**Shape:**
- `on: push: branches: [main]`, plus `workflow_dispatch`.
- `permissions: {}`.
- One job on `ubuntu-latest`, `timeout-minutes: 5`, one step: `gh api repos/hrms-wms-2026/HRMS-Backend-v1/dispatches -f event_type=deploy-appservice -f "client_payload[source]=${{ github.repository }}" -f "client_payload[sha]=${{ github.sha }}"` with `env: GH_TOKEN: ${{ secrets.BACKEND_DISPATCH_TOKEN }}`.
- The step fails with a clear message when the secret is empty.

- [ ] **Step 1:** Create the file in the web clone and the admin clone, on branch `feature/appservice-deploy-trigger` from `origin/main`.
- [ ] **Step 2: Lint** both with actionlint, as in Task 2.
- [ ] **Step 3:** Commit `ci: trigger the App Service deploy in HRMS-Backend-v1 on main merges` in each repo.

### Task 5: Setup guide, docs and push

**Files:**
- Create: `ops/appservice/deploy-pipeline-setup.md`. The one-time setup from the spec's "One-time setup", as numbered steps with the exact portal and GitHub screens:
  - SCM Basic Auth tick → download the publish profile
  - the two fine-grained tokens (repo access and permissions, exactly as in the spec)
  - the secrets and variables, including the `appservice-test` environment
  - WebSockets / Always On / HTTPS Only
  - the Actions-minutes note
  - how to run a manual deploy and what the run summary shows
  - how to switch `PACKAGE_MODE` to `Subdomain`
- Modify: `docs/superpowers/specs/next/SUMMARY.md` and `docs/superpowers/plans/next/SUMMARY.md` (one line each for this spec and plan).

- [ ] **Step 1:** Write the guide and the SUMMARY lines.
- [ ] **Step 2:** Commit `docs(appservice): deploy pipeline setup guide`, together with the spec and plan files.
- [ ] **Step 3:** Push `feature/appservice-deploy-pipeline` (backend) and `feature/appservice-deploy-trigger` (web, admin) to `origin`. Report the three compare links for the user to open PRs. Don't open PRs: there's no `gh` login here.
