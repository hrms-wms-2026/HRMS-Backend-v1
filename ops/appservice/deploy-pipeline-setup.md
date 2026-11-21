# App Service deploy pipeline: one-time setup

The workflow `.github/workflows/deploy-appservice.yml` builds the backend, web app and admin console into one zip and deploys it to the Azure App Service. It runs:

- on every merge to `main` in this repo
- on every merge to `main` in the web or admin repo, through their `trigger-appservice-deploy.yml`
- from the **Run workflow** button: Actions tab → *Deploy to App Service* → Run workflow

Design: `docs/superpowers/specs/next/2026-11-19-appservice-deploy-pipeline-design.md`.

> GitHub only runs `repository_dispatch` and shows the **Run workflow** button for workflows that are on the default branch (`main`). Nothing below works until this workflow is merged to `main`.

## 1. Azure portal (App Service)

1. **Configuration → General settings:**
   - Tick **SCM Basic Auth Publishing Credentials**.
   - Set **Always on** to On. Background jobs stop when the app idles without it.
   - Set **Web sockets** to On. Inbox live updates use SignalR.
   - Set **HTTPS Only** to On.
   - Click **Save**.
   - **Stack settings → Startup command:** `dotnet ONEVO.Api.dll`. The package contains other `.dll` files, so App Service must be told which one to start.
   - **Environment variables:** make sure `SCM_DO_BUILD_DURING_DEPLOYMENT` is absent or `false`. The package is already built, and App Service must not try to build it again.
2. **Overview → Download publish profile.** Keep the file private: it works like a password for the app. If it leaks, use **Reset publish profile** and update the GitHub secret.
3. **Environment variables:** enter the app settings from [`appservice-settings.md`](appservice-settings.md), once. The pipeline never changes settings.

## 2. GitHub tokens (fine-grained personal access tokens)

Create these at GitHub → Settings → Developer settings → Fine-grained tokens, with resource owner **hrms-wms-2026**. If the organisation requires approval, an org owner must approve them.

| Token | Repository access | Permissions | Used by |
|---|---|---|---|
| **frontend-read** | `Hrms--Web-application---front-end---v1`, `HRMS-Platform-Administration-Front-End-v1` | Contents: **Read-only** | Backend pipeline, to download the two frontends |
| **backend-dispatch** | `HRMS-Backend-v1` only | Contents: **Read and write** (GitHub requires this for repository dispatch) | Web and admin trigger workflows |

Tokens expire; set a reminder for the expiry date.

## 3. GitHub settings: backend repo (HRMS-Backend-v1)

Settings → Secrets and variables → Actions.

**Secrets**

| Name | Value |
|---|---|
| `AZURE_WEBAPP_PUBLISH_PROFILE` | The whole contents of the downloaded `.PublishSettings` file |
| `FRONTEND_READ_TOKEN` | The **frontend-read** token |
| `MIGRATION_CONNECTION_STRING` | Optional. `Server=9.223.115.62,51433;Database=CrownusDB_test;User ID=crownus_migrator;Password=<migrator password>;TrustServerCertificate=True`. Only needed if you use *apply migrations*. |

**Variables**

| Name | Value |
|---|---|
| `APPSERVICE_NAME` | The App Service's name, as shown in the portal |
| `APPSERVICE_HOST` | Its default domain, without `https://` (Overview → Default domain) |
| `PACKAGE_MODE` | `SingleHost`. Change it to `Subdomain` once a real domain is set up (see the runbook at the end of `docs/superpowers/plans/next/2026-11-19-single-host-tenancy-test-mode.md`). |

**Environment.** The deploy job uses the environment `appservice-test`, which GitHub creates on the first run. To require approval before every deploy (recommended once more people can press Run), go to Settings → Environments → `appservice-test` → **Required reviewers**.

## 4. GitHub settings: web and admin repos

In each repo: Settings → Secrets and variables → Actions → secret `BACKEND_DISPATCH_TOKEN` = the **backend-dispatch** token.

## 5. GitHub Actions minutes

The organisation's included Actions minutes ran out in November 2026. Runs fail within seconds until the monthly reset, or until someone raises the spending limit at Organisation → Settings → Billing. A deploy uses about 10 minutes.

## Using it

- **Normal deploys:** merge to `main` in any of the three repos. Watch Actions → *Deploy to App Service*. The run summary lists the three commits deployed and the package SHA-256.
- **Manual deploy:** Run workflow. You can pick branches, tags or SHAs for each repo.
- **Database migrations:** they are **not** applied automatically. When a release contains a new migration:
  1. Run workflow with **apply migrations** ticked. It runs the EF migration bundle as `crownus_migrator`.
  2. If the migration adds a new schema or stored procedure, also run `ops/sqlserver/post-migration-grants.sql` by hand with an admin login, because `crownus_migrator` can't grant permissions:

     `sqlcmd -S 9.223.115.62,51433 -U <admin login> -C -b -i ops/sqlserver/post-migration-grants.sql -v DB_NAME=CrownusDB_test`

     New `dbo` tables are already covered by the existing schema-wide grant.
- **How a deploy is confirmed:** each package carries a build id, which the site serves at `/build-id.txt`. The deploy job waits until the site serves *this* run's id, so it never mistakes the old version for the new one. Then it requires `/health/ready` to return 200, all within 10 minutes. If it fails, check App Service → Log stream. The usual causes are a missing app setting or a pending migration.
- **Queued deploys:** only one deploy runs at a time. While it runs, GitHub keeps just one more waiting and drops any older waiting run. For merges that's harmless, because the waiting run always builds the newest `main`. But a waiting **manual** run (custom branches or *apply migrations*) can be dropped by a later merge, and vice versa. Start manual deploys when nothing else is running, and check the Actions tab afterwards.
