# App Service deploy pipeline — design

Date: 2026-11-19. Status: draft, awaiting approval.
Repos: HRMS-Backend-v1 (pipeline), Hrms--Web-application---front-end---v1 and HRMS-Platform-Administration-Front-End-v1 (trigger files only).

## Intent

Deploy Crownus to the Azure App Service `onexso-api` automatically, so nobody builds and uploads zips by hand. It runs automatically on every merge to `main` in any of the three repos, and there's a manual **Run** button. `main` was brought up to date with `development` on 2026-11-19 and is now the "what's on the test site" branch.

## Goals

- A merge to `main` in the backend, web or admin repo builds all three and deploys them to `onexso-api`.
- A **Run workflow** button deploys on demand.
- The job fails visibly if the deployed app doesn't answer `/health/ready` with 200.
- The package is the same one `ops/appservice/build-appservice-package.ps1` already builds and that was validated on 2026-11-19.

## Non-goals

- **App settings and secrets.** The database connection, `Tenancy__Mode`, keys and passwords stay in the App Service portal, entered once. The pipeline never reads or writes them.
- **Tests.** The existing `ci.yml` already tests PRs. The deploy job only builds, so it uses fewer Actions minutes.
- **Database migrations by default** (see below).
- **The tray app.**

## Anti-goals

- No password or connection string in YAML or logs.
- A deploy must never run while another deploy is in progress, so two quick merges don't overlap.
- A broken build must never reach the App Service. Deploy happens only after the package is built and checked.

## Approaches considered (user's choices)

1. **Azure login.**
   - Chosen: **publish profile**, stored as the GitHub secret `AZURE_WEBAPP_PUBLISH_PROFILE`. Quick, and needs no owner.
   - Considered: OIDC (more secure, nothing secret stored), but it needs the owner to grant a role.
   - Cost: the user must switch on **SCM Basic Auth Publishing Credentials** on `onexso-api` before downloading the profile.
2. **Runner.**
   - Chosen: GitHub-hosted `ubuntu-latest`, build and deploy only (my pick, not objected to).
   - Considered: self-hosted on the user's PC or the VM.
   - Cost: it can't run until the org's Actions minutes reset or the spending limit is raised. As of 2026-11-19 the included minutes are used up.
3. **Web and admin changes.**
   - Chosen: a small trigger workflow in each frontend repo that pokes the backend pipeline (my pick, not objected to).
   - Considered: Run button only.

## Design

### Backend repo: `.github/workflows/deploy-appservice.yml`

- **Triggers:**
  - `push` to `main`
  - `workflow_dispatch`, with inputs `backend_ref`, `web_ref` and `admin_ref` (default `main`)
  - `repository_dispatch` of type `deploy-appservice`, from the frontend repos
- **`concurrency: deploy-appservice`**, with `cancel-in-progress: false`. A new deploy waits for the running one.
- **Steps:**
  1. **Check out the backend.** Check out the web and admin repos into sibling folders using the secret `FRONTEND_READ_TOKEN`: a fine-grained token with *Contents: read* on those two repos only.
  2. **Set up .NET 10 and Node** (the version the web repo's Angular 21 needs; 22 LTS unless the repo pins one). Run `npm ci` in web and admin, with npm cache.
  3. **Build the package.** Run `pwsh ops/appservice/build-appservice-package.ps1 -Mode $PACKAGE_MODE`. The script must run on Linux: replace its `$env:TEMP` use with `[IO.Path]::GetTempPath()`, and keep forward-slash zip entries. `PACKAGE_MODE` is a repository variable, default `SingleHost`. Switching to subdomains later means setting it to `Subdomain`, with no YAML change.
  4. **Deploy** with `azure/webapps-deploy@v3`: `app-name: onexso-api`, the publish profile secret, the built zip.
  5. **Health check.** Retry `GET https://<default host>/health/ready` for up to 5 minutes. Fail the job if it never returns 200. The host is a repository variable `APPSERVICE_HOST`.
- **Run summary.** Write the three commit SHAs that were deployed and the zip's SHA-256.

### Frontend repos: `.github/workflows/trigger-appservice-deploy.yml`

- **Trigger:** `push` to `main` (web repo default branch `main`; the integration branch there is `Development`).
- **Step:** one `gh api repos/hrms-wms-2026/HRMS-Backend-v1/dispatches -f event_type=deploy-appservice` using the secret `BACKEND_DISPATCH_TOKEN`: a fine-grained token with *Contents: read and write* on HRMS-Backend-v1 only, which GitHub requires for repository dispatch.
- **Cost:** a few seconds of minutes.

### Database migrations (my call)

- **Default:** the pipeline does **not** touch the database. Production mode never auto-migrates, and applying migrations to a shared database on every merge is risky.
- **Optional input:** the Run button has `apply_migrations` (default false). When true, the job builds the EF migration bundle and runs it against the secret `MIGRATION_CONNECTION_STRING` (the `crownus_migrator` login), then runs `post-migration-grants.sql`.
- **Check:** the SQL port `9.223.115.62,51433` is reachable from the internet, so GitHub's runners can reach it.
- **If the user would rather not store the migrator password in GitHub,** drop this input and keep running migrations by hand.

## One-time setup (user)

1. `onexso-api` → Configuration → tick **SCM Basic Auth Publishing Credentials** → Save. Then Overview → **Download publish profile**.
2. Backend repo → Settings:
   - Secrets: `AZURE_WEBAPP_PUBLISH_PROFILE` (file contents), `FRONTEND_READ_TOKEN`, and optionally `MIGRATION_CONNECTION_STRING`.
   - Variables: `APPSERVICE_NAME`, `APPSERVICE_HOST`, `PACKAGE_MODE=SingleHost`. (The app name became a variable during the build: the visible-name guard test blocks the old product name in workflow files.)
3. Web and admin repos: secret `BACKEND_DISPATCH_TOKEN`.
4. Portal, once:
   - the App Service settings from `ops/appservice/appservice-settings.md`
   - **WebSockets On**, **Always On On**, **HTTPS Only On**
5. **Actions minutes:** wait for the reset or raise the org spending limit.

## Left to the builder

- Exact Node version.
- npm cache keys.
- How the script finds the sibling repo paths (it already takes `-WebRepo` / `-AdminRepo`).
- Retry timing for the health check.
- Whether `actionlint` runs in CI for the new files.
