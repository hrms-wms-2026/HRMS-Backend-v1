# Crownus Team-Test Deployment on vm-tic-incubato: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run Crownus (backend, web app, admin console) on the Azure VM `vm-tic-incubato` (9.223.115.62) for team testing, on its own SQL Server Express instance, without touching Oneverz.

**Architecture:** There are two small code PRs into `development`: a production opt-in for the first admin password (backend), and a same-origin API address for the admin console. The deployment pieces are built on the developer PC. On the server, IIS serves both Angular apps and proxies the API paths to the API, which runs in IIS on `127.0.0.1:5080`. The API connects to a new SQL Server Express named instance `CROWNUS` on `localhost,14330`. Each host gets a Let's Encrypt certificate through win-acme over HTTP-01.

**Tech Stack:** .NET 10 (ASP.NET Core in IIS through the Hosting Bundle), Angular 21, SQL Server Express, IIS with URL Rewrite, ARR and WebSockets, win-acme, sqlcmd, and the EF Core migration bundle.

**Spec:** this conversation's decisions, recorded in memory `project_crownus_server_migration` and `project_sqlserver_migration`. There is no separate design doc.

**Repos:** `HRMS-Backend-v1` (`development`), `Hrms--Web-application---front-end---v1` (`Development`) and `HRMS-Platform-Administration-Front-End-v1` (`development`). The tray app is out of scope (see Later).

## Global Constraints

- **Oneverz is off limits.** Never connect to, configure, restart or stop `MSSQL$MSSQLSERVER01`, `SQLAgent$MSSQLSERVER01` or their ports 51433 and 53024. Every server step that could touch them has a check before and after.
- **Owner approval comes first.** The developer does not manage the VM, so nothing in Phase C runs until the VM owner has approved the list in Task 3.
- The root domain is `9-223-115-62.sslip.io`. Hosts are the apex `9-223-115-62.sslip.io`, the admin console at `admin.9-223-115-62.sslip.io`, and tenants at `<slug>.9-223-115-62.sslip.io`.
- `ASPNETCORE_ENVIRONMENT=Production` on the server. Never `Development`, which runs the demo seeders and auto-migrates.
- **No demo data.** Only the reference-data seeders that already run in Production are allowed.
- **SQL Express instance `CROWNUS`:** TCP on fixed port 14330, SQL authentication enabled, SQL Browser left disabled, and no inbound firewall rule for 14330.
- **Required database settings:** collation `Latin1_General_100_CS_AS_SC`, `READ_COMMITTED_SNAPSHOT ON` and database-scoped `MAXDOP = 1`, all set by `ops/sqlserver/bootstrap-logins.sql`. The API refuses to start without them.
- **Database logins:** the runtime connection uses `onevo_app` and never `sa`. Migrations run as `onevo_migrator`.
- **Secrets:** passwords and keys are typed on the server or the developer PC only. They are never pasted into chat, never committed, and never put in URLs.
- **Commits:** no commits without the user's go-ahead, and no `--no-verify`.

## Review Focus

1. **Host header lost at the proxy.** ARR must preserve `Host`, or `HostTenantResolutionMiddleware` returns 400 for every API call. This is checked in Task 9, step 4.
2. **HTTPS scheme lost at the proxy.** Without `X-Forwarded-Proto: https`, `UseHttpsRedirection` loops and cookies lose `Secure`. This is checked in Task 9, step 4.
3. **The SQL installer changing Oneverz's instance.** A shared-component update or a forced reboot would affect it. The before and after snapshots in Tasks 5 and 6 must match.
4. **The first-admin password left switched on.** Leaving `AllowPasswordBootstrap` set after first login would let a restart re-create a credential if the stored one is revoked. Task 1's tests pin the strict flag, and Task 11 removes it.
5. **Certificates refused.** `sslip.io` is not on the Public Suffix List, so it probably shares one Let's Encrypt per-domain rate limit with every sslip.io user. The apex certificate is requested first, as a probe, in Task 10.
6. **SignalR not upgrading to WebSockets through ARR.** This is checked in Task 12, step 3.

---

## Phase A: Code PRs into `development`

### Task 1: Production opt-in for the first admin password (backend)

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/PlatformAccessSeeder.cs`, which computes `allowCredentialBootstrap` in `StartAsync`, and the missing-password message in `BootstrapPasswordCredentialAsync`
- Modify: `.env.example`, adding a commented `PlatformBootstrap__AllowPasswordBootstrap` line
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/PlatformAccessSeederTests.cs`

**Interfaces:**
- Produces: `public static bool IsCredentialBootstrapAllowed(IHostEnvironment environment, IConfiguration configuration)` on `PlatformAccessSeeder`. It returns true in Development and Test, as today. In any other environment it returns true only when `PlatformBootstrap:AllowPasswordBootstrap` parses with `bool.TryParse` to `true`.
- Config keys used on the server: `PlatformBootstrap:AllowPasswordBootstrap`, `PlatformBootstrap:SuperAdminEmail`, `PlatformBootstrap:SuperAdminFullName` and `DevAdmin:Password`. The existing password key is kept as it is.

- [ ] **Step 1: Write the failing tests** (xUnit with FluentAssertions; use an `IHostEnvironment` mock or stub and an in-memory `ConfigurationBuilder`)
  - `IsCredentialBootstrapAllowed_Production_WithoutFlag_ReturnsFalse`
  - `IsCredentialBootstrapAllowed_Production_FlagTrue_ReturnsTrue` (the value is `"true"`)
  - `IsCredentialBootstrapAllowed_Production_FlagNotStrictlyTrue_ReturnsFalse` (`[Theory]` with `"yes"`, `"1"`, `""` and `"false"`)
  - `IsCredentialBootstrapAllowed_Development_ReturnsTrue` and `..._Test_ReturnsTrue`
  - `SeedAsync_AllowedWithExistingActiveCredential_DoesNotAddSecond`: seed once with password A, then seed with password B. There is exactly one active password credential, and it still verifies against A.
- [ ] **Step 2: Run the tests and confirm they fail**
  Run `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -c Release --filter "FullyQualifiedName~PlatformAccessSeederTests"`. Expected: compile failure, because `IsCredentialBootstrapAllowed` doesn't exist yet.
- [ ] **Step 3: Implement the change**
  - `StartAsync` calls `IsCredentialBootstrapAllowed`.
  - When it returns true outside Development and Test, log a warning: `"[BOOTSTRAP] Production password bootstrap is enabled. Remove PlatformBootstrap__AllowPasswordBootstrap and DevAdmin__Password after the first admin login."`.
  - Reword the missing-password exception so it names both keys.
- [ ] **Step 4: Run the tests and confirm they pass**
  Run the same command. Expected: all `PlatformAccessSeederTests` pass.
- [ ] **Step 5: Run the backend gate**
  Run `pwsh ../dev-env/scripts/verify.ps1 backend`. Expected: build, unit and architecture tests pass. If `pwsh` or `dev-env` is missing on this PC, run the raw `dotnet build` and `dotnet test` commands from `CLAUDE.md` and say so.
- [ ] **Step 6: Commit and open a PR into `development`, after the user's go-ahead**
  Branch `feature/prod-admin-bootstrap` from `origin/development`. Commit message: `feat: opt-in production password bootstrap for first platform admin`.

### Task 2: Same-origin API address for the admin console

**Files:**
- Modify: `src/environments/environment-url.helper.ts` (admin repo)
- Modify: `src/environments/environment.prod.ts` (admin repo)
- Test: `src/environments/environment-url.helper.spec.ts` (Vitest)

**Interfaces:**
- Produces: `buildAdminApiUrl(pageProtocol: string, hostname: string): string`, which returns `${pageProtocol}//${hostname}/admin/v1`.
- Produces: `buildAdminWsUrl(pageProtocol: string, hostname: string): string`, which returns `wss:` or `ws:` (matching the page protocol) + `//${hostname}/ws`.
- `environment.prod.ts` uses both with `window.location` and keeps `appName`. `environment.staging.ts` is left unchanged.

- [ ] **Step 1: Write the failing tests**
  - `buildAdminApiUrl('https:', 'admin.9-223-115-62.sslip.io')` returns `'https://admin.9-223-115-62.sslip.io/admin/v1'`.
  - `buildAdminWsUrl('https:', 'admin.9-223-115-62.sslip.io')` returns `'wss://admin.9-223-115-62.sslip.io/ws'`.
  - `buildAdminWsUrl('http:', 'localhost')` returns `'ws://localhost/ws'`.
- [ ] **Step 2: Run them and confirm they fail.** Run the repo's Vitest command for this spec. Expected: the functions are not exported.
- [ ] **Step 3: Implement both functions and switch `environment.prod.ts` to them.**
- [ ] **Step 4: Run the spec, then the production build.** Run the spec, then `npm run build`. Expected: the tests pass, and the build succeeds with no `platform-administration.com` text left in the output (`grep -r platform-administration.com dist` finds nothing).
- [ ] **Step 5: Commit and open a PR into `development`, after the user's go-ahead.** Commit message: `feat: same-origin admin API URL for production builds`.

**Phase A exit:** both PRs are merged, and CI is green on backend `development`. CI on `development` was unverified on 2026-11-09, so the user confirms it on GitHub.

---

## Phase B: Build the deployment pieces (developer PC)

### Task 3: Build the release pieces and the owner approval request

**Files:** created outside the repos, in `C:\crownus-release\`. Nothing here is committed.

- [ ] **Step 1: Publish the API.** From backend `development` HEAD, run `dotnet publish src/ONEVO.Api/ONEVO.Api.csproj -c Release -r win-x64 --self-contained false -o C:\crownus-release\api`. Expected: `ONEVO.Api.dll` and `web.config` are in the folder.
- [ ] **Step 2: Build the migration bundle.** Run `dotnet ef migrations bundle -p src/ONEVO.Infrastructure -s src/ONEVO.Api -r win-x64 --self-contained -o C:\crownus-release\efbundle.exe`. Expected: `efbundle.exe` exists, and `efbundle.exe --help` runs.
- [ ] **Step 3: Copy the SQL scripts.** Copy `ops/sqlserver/bootstrap-logins.sql` and `ops/sqlserver/post-migration-grants.sql` to `C:\crownus-release\sql\`.
- [ ] **Step 4: Build the web app.** From web `Development` HEAD, run `npm ci; npm run build`. The default configuration is `production`. Copy the `dist/.../browser` output to `C:\crownus-release\web`. Expected: `index.html` is there.
- [ ] **Step 5: Build the admin console.** From admin `development` HEAD, after Task 2, run `npm ci; npm run build`. Copy the browser output to `C:\crownus-release\admin`.
- [ ] **Step 6: Zip everything.** Zip it as `crownus-release-<date>.zip` and record the three commit hashes in `C:\crownus-release\VERSIONS.txt`.
- [ ] **Step 7: Send the owner the approval request.** The user sends it. It asks for:
  - installing SQL Server Express as a named instance `CROWNUS`, with no change to `MSSQLSERVER01`
  - installing IIS (with WebSocket Protocol), the .NET 10 Hosting Bundle, URL Rewrite, ARR and win-acme
  - opening inbound 80 and 443 in Windows Firewall, the Azure NSG, and any load-balancer or NAT rule, and asking whether 9.223.115.62 is NAT'd, since RDP on 53389 suggests it is
  - a maintenance window, in case an installer asks for a reboot
  - an Azure snapshot of the VM before Phase C

  **Stop until the owner approves in writing.**

---

## Phase C: Server runbook (the user runs each step over RDP and pastes the output)

Every step has an expected result. If the output differs, stop and bring it back to the developer before going on.

### Task 4: Record the starting state

- [ ] **Step 1: Snapshot the services and ports.** Run:
  `Get-Service *SQL* | Format-Table Name,Status,StartType; Get-NetTCPConnection -State Listen | Select-Object LocalPort -Unique | Sort-Object LocalPort`
  Save the output as `before.txt`. Expected: `MSSQL$MSSQLSERVER01` is Running, and nothing is listening on 80, 443, 5080 or 14330.
- [ ] **Step 2: Read Oneverz's SQL Server version, without connecting.** Run:
  `Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\*\MSSQLServer\CurrentVersion' -ErrorAction SilentlyContinue | Select-Object PSPath,CurrentVersion`
  Note the major version (16 is SQL Server 2022, 17 is 2025). Choose SQL Server Express of the **same major version**, so the installer doesn't upgrade shared components.

### Task 5: Install SQL Server Express (instance `CROWNUS`)

- [ ] **Step 1: Install.** Download the matching Express installer from Microsoft. Run setup unattended with:
  `/ACTION=Install /FEATURES=SQLEngine /INSTANCENAME=CROWNUS /SECURITYMODE=SQL /SQLCOLLATION=Latin1_General_100_CS_AS_SC /TCPENABLED=1 /BROWSERSVCSTARTUPTYPE=Disabled /SQLSVCSTARTUPTYPE=Automatic /UPDATEENABLED=False /SQLSYSADMINACCOUNTS="BUILTIN\Administrators" /IACCEPTSQLSERVERLICENSETERMS`
  Add `/SAPWD=<typed on the server>`.
  Expected: setup exits with code 0. If it asks for a reboot, stop and use the owner's window.
- [ ] **Step 2: Fix the TCP port.** In SQL Server Configuration Manager, for `CROWNUS` only: TCP/IP Enabled, IPAll TCP Port `14330`, dynamic ports blank. Then run `Restart-Service 'MSSQL$CROWNUS'`. Restart only that service.
- [ ] **Step 3: Verify.** Run `Get-NetTCPConnection -LocalPort 14330 -State Listen; Get-Service 'MSSQL$MSSQLSERVER01','SQLBrowser'`. Expected: 14330 is listening, `MSSQLSERVER01` is still Running, and `SQLBrowser` is still Stopped.
- [ ] **Step 4: Check `sqlcmd`.** Run `sqlcmd -?`. If it's missing, install Microsoft's `sqlcmd` (go-sqlcmd) and re-check.

### Task 6: Create the database, run the migrations, grant access

- [ ] **Step 1: Run the bootstrap script.** Run:
  `sqlcmd -S localhost,14330 -U sa -C -b -i sql\bootstrap-logins.sql -v "DB_NAME=crownus" "APP_PASSWORD=<new>" "MIGRATOR_PASSWORD=<new>"`
  Expected: no errors.
- [ ] **Step 2: Run the migrations.** Run:
  `.\efbundle.exe --connection "Server=localhost,14330;Database=crownus;User ID=onevo_migrator;Password=<migrator>;TrustServerCertificate=True"`
  Expected: the output ends with `Done.`
- [ ] **Step 3: Run the grants script.** Run `sqlcmd -S localhost,14330 -U sa -C -b -i sql\post-migration-grants.sql -v "DB_NAME=crownus"`. Expected: no errors.
- [ ] **Step 4: Compare with the starting state.** Re-run the Task 4 step 1 snapshot. It must match `before.txt`, apart from the new `MSSQL$CROWNUS` service and port 14330.

### Task 7: Install the web stack

- [ ] **Step 1: Install IIS.** Run `Install-WindowsFeature Web-Server,Web-WebSockets,Web-Mgmt-Console`. Expected: `Success True`. Note whether it says a restart is needed.
- [ ] **Step 2: Install the add-ons.** Install the .NET 10 Hosting Bundle, URL Rewrite 2.1 and ARR 3.0 from Microsoft. Then run `iisreset`. This affects IIS only, which is new.
- [ ] **Step 3: Configure ARR.** Run:
  `& $env:windir\system32\inetsrv\appcmd.exe set config -section:system.webServer/proxy /enabled:"True" /preserveHostHeader:"True" /commit:apphost`
  Then allow the server variable `HTTP_X_FORWARDED_PROTO` at the server level: URL Rewrite → View Server Variables, or `appcmd set config -section:system.webServer/rewrite/allowedServerVariables /+"[name='HTTP_X_FORWARDED_PROTO']" /commit:apphost`.

### Task 8: Deploy the API (IIS site `crownus-api` on `127.0.0.1:5080`)

- [ ] **Step 1: Set up the site.**
  - Unzip `api` to `C:\crownus\api`.
  - Create an app pool `crownus-api` with No Managed Code.
  - Create a site `crownus-api` with an http binding on `127.0.0.1:5080`, no host name.
- [ ] **Step 2: Add the settings to `web.config`.** In `C:\crownus\api\web.config`, inside `<aspNetCore>`, add `<environmentVariables>`.
  - Use `web.config` and not `.env`, because `DotEnvLoader` looks for a repository root, and the server has none.
  - Full connection strings are allowed here as process variables.
  - Restrict the file's access to Administrators and the app pool identity.

  | Variable | Value |
  |---|---|
  | `ASPNETCORE_ENVIRONMENT` | `Production` |
  | `ConnectionStrings__DefaultConnection` | `Server=localhost,14330;Database=crownus;User ID=onevo_app;Password=<app>;TrustServerCertificate=True` |
  | `Tenancy__RootDomain` | `9-223-115-62.sslip.io` |
  | `Urls__AppBaseUrl` | `https://9-223-115-62.sslip.io` |
  | `Urls__AdminConsoleBaseUrl` | `https://admin.9-223-115-62.sslip.io` |
  | `Encryption__MasterKey` | Freshly generated, and long enough to pass the length check in `ConfigurationStartupValidator` |
  | `Jwt__Secret`, `Jwt__AdminSecret`, `TrayApp__Jwt__Secret` | Fresh random values (`TrayApp__Jwt__Secret` at least the minimum the validator states, and not starting with `dev-only`) |
  | `Email__FromAddress` | The sending address |
  | `Email__FromName` | `CROWNUS` |
  | `PlatformBootstrap__SuperAdminEmail`, `PlatformBootstrap__SuperAdminFullName` | The first admin's details |
  | `PlatformBootstrap__AllowPasswordBootstrap` | `true` (temporary) |
  | `DevAdmin__Password` | A temporary password, typed on the server |
- [ ] **Step 3: Check the API starts.** Run `Invoke-WebRequest http://127.0.0.1:5080/health/ready -Headers @{Host='9-223-115-62.sslip.io'} -UseBasicParsing | Select-Object StatusCode`. Expected: `200`. If it isn't, enable `stdoutLogEnabled` in `web.config`, read the log, and bring it back. Expect the database-settings validator and `[CONFIG]` lines to be in it.

### Task 9: Deploy the two web sites

- [ ] **Step 1: The web app site.** Unzip `web` to `C:\crownus\web`. Create a site `crownus-web` with an http binding on port 80 for host `9-223-115-62.sslip.io`.
  Its `web.config` has these URL Rewrite rules, in order:
  1. Proxy `^(api/.*|hubs/.*|ws.*|health.*)` to `http://127.0.0.1:5080/{R:0}`, setting server variable `HTTP_X_FORWARDED_PROTO` to `https`.
  2. Serve the matching file if it exists.
  3. Otherwise rewrite to `/index.html`, the single-page-app fallback.
- [ ] **Step 2: The admin console site.** Unzip `admin` to `C:\crownus\admin`. Create a site `crownus-admin` with host `admin.9-223-115-62.sslip.io` on port 80. Use the same rules, but proxy `^(admin/.*|ws.*|health.*)`.
- [ ] **Step 3: Open the ports.** Run `New-NetFirewallRule -DisplayName "Crownus HTTP/HTTPS" -Direction Inbound -Protocol TCP -LocalPort 80,443 -Action Allow`. The owner opens the NSG, load-balancer and NAT rules.
- [ ] **Step 4: Proxy checks (Review Focus 1 and 2).** From the developer PC, run `curl -i http://9-223-115-62.sslip.io/health`. Expected: `200` for the API health check, not `400`. A `400` means the Host header was lost.

### Task 10: HTTPS certificates (win-acme over HTTP-01)

- [ ] **Step 1: Probe with the apex first (Review Focus 5).** Install win-acme from its official GitHub release. Request a certificate for `9-223-115-62.sslip.io` with IIS binding installation for `crownus-web`.
  - Expected: the certificate is issued, the `https` binding is added, and a renewal task is created.
  - **If Let's Encrypt refuses because of a rate limit, stop.** The fallback is buying a domain, which also allows a wildcard certificate. That decision goes back to the user.
- [ ] **Step 2: Repeat for the admin console** at `admin.9-223-115-62.sslip.io`, on `crownus-admin`.
- [ ] **Step 3: Verify.** Run `curl -I https://admin.9-223-115-62.sslip.io/health`. Expected: `200` with no redirect loop. A `Set-Cookie` from a login later must carry `Secure`.

---

## Phase D: First use

### Task 11: First admin login, then switch the bootstrap off

- [ ] **Step 1: Log in.** Open `https://admin.9-223-115-62.sslip.io` and log in with the bootstrap email and the temporary password. Set up MFA with an authenticator app. Admin MFA is TOTP, so no email is needed.
- [ ] **Step 2: Switch the bootstrap off.** Delete `PlatformBootstrap__AllowPasswordBootstrap` and `DevAdmin__Password` from `C:\crownus\api\web.config`, then recycle `crownus-api`.
- [ ] **Step 3: Confirm.** Log in again. It still works, because the credential is stored. The startup log has no `[BOOTSTRAP]` warning.
- [ ] **Step 4: Change the password** from the admin console if it offers that, since the temporary one was typed during setup.

### Task 12: Platform keys, the first tenant, smoke test

- [ ] **Step 1: Platform keys.** In the admin console, set the email provider and storage keys (platform service keys) before creating a tenant. The owner invite email needs the email provider.
- [ ] **Step 2: Create the tenant.** Create the team's tenant with an owner invite. Then request a win-acme certificate and add a host binding for `<slug>.9-223-115-62.sslip.io` on `crownus-web`. Every new tenant needs this step.
- [ ] **Step 3: Smoke test.**
  - The owner accepts the invite and logs in at `https://<slug>.9-223-115-62.sslip.io`.
  - In browser devtools, `/hubs/inbox` negotiates and upgrades to WebSocket (Review Focus 6).
- [ ] **Step 4: Isolation check.** Create a second throwaway tenant and confirm that data created in one never appears in the other. Then delete or disable the throwaway tenant.
- [ ] **Step 5: Compare with the starting state.** Re-run the Task 4 step 1 snapshot. `MSSQL$MSSQLSERVER01` is still Running and unchanged.

## Rollback

- Stop the sites `crownus-web`, `crownus-admin` and `crownus-api`.
- Run `Stop-Service 'MSSQL$CROWNUS'` and set it to Disabled.
- Remove the firewall rule.

None of these steps touch `MSSQLSERVER01`. Full removal means uninstalling the `CROWNUS` instance, IIS and the add-ons, with the owner's agreement, or restoring the snapshot.

## Later (not in this plan)

- **Tray app:** set `ApiBaseUrl` to `https://9-223-115-62.sslip.io` and `PortalRootDomain` to `9-223-115-62.sslip.io` in the agent configuration, then build a release.
- **Real domain:** move to a real domain with a wildcard certificate before real customers. That replaces the per-tenant certificate step.
- **Memory:** upgrade the VM to 16 GB before real customers.
