# App Service settings for single-host test mode

Names and non-secret values for the Azure App Service **Configuration > Application settings** (they become environment variables; `__` is the .NET nested-key separator).

> **Cross-check before saving.** This list was built from the repository only (`appsettings*.json`, `.env.example`, `DotEnvLoader`, `ForwardedHeadersExtensions`, `SpaHostingExtensions`, `docs/deployment/*`). The VM's `C:\crownus\api\web.config` was deliberately not read. Compare the names below with the `<environmentVariable>` entries in that file and add anything the VM sets that is missing here (and drop anything you do not want to carry over).

Values written `<from user>` are secrets: copy them from the VM's `web.config` or your secret store. Do not paste them into chat, a commit or a doc.

`<host>` = `onexso-api-dthwhydjctg9bub8.uksouth-01.azurewebsites.net`

## Single-host overrides (required)

| Name | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Tenancy__Mode` | `SingleHost` |
| `Hosting__ServeSpas` | `true` |
| `Tenancy__RootDomain` | `onexso-api-dthwhydjctg9bub8.uksouth-01.azurewebsites.net` |
| `Urls__AppBaseUrl` | `https://onexso-api-dthwhydjctg9bub8.uksouth-01.azurewebsites.net` |
| `Urls__AdminConsoleBaseUrl` | `https://onexso-api-dthwhydjctg9bub8.uksouth-01.azurewebsites.net/admin-console` |
| `ForwardedHeaders__KnownNetworks__0` | `0.0.0.0/0` |
| `ForwardedHeaders__KnownNetworks__1` | `::/0` |

Optional: `Hosting__SpaRoot` (default `<ContentRoot>/wwwroot`; the package puts the apps in `wwwroot/web` and `wwwroot/admin-console`, so leave it unset). With `Hosting__ServeSpas=true` the API refuses to start if either folder is missing.

### ForwardedHeaders__KnownNetworks

`ForwardedHeadersExtensions` binds `ForwardedHeaders:KnownNetworks` as a `string[]` and parses each entry with `System.Net.IPNetwork.Parse`, so it takes CIDR strings (IPv4 or IPv6). `ForwardedHeaders:KnownProxies` takes single IPs (`IPAddress.Parse`). The default trust list (loopback) is never cleared; entries are only added. In an App Service array setting use one indexed key per entry: `__0`, `__1`.

App Service's front end is not a fixed address range, so `0.0.0.0/0` plus `::/0` trusts any caller's `X-Forwarded-*`. That is acceptable only because App Service is the sole ingress to the app (nothing can reach the process except through its front end). If the app were ever reachable another way, replace these with the real ranges, because `X-Forwarded-Host` selects the tenant and `X-Forwarded-For` keys the rate limiters.

With `Tenancy__RootDomain` set, forwarded hosts are limited to that host and `*.` that host.

## Database

Target: `CrownusDB_test` on `9.223.115.62,51433`.

`DotEnvLoader` builds the two connection strings from `CROWNUS_DB_*` values when the full `ConnectionStrings__*` variable is not already set. Either of these two forms works; use one.

**Form A: full connection strings (as the brief states).**

| Name | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | `Server=9.223.115.62,51433;Database=CrownusDB_test;User Id=<from user: app login>;Password=<from user>;<rest of the VM's options, e.g. TrustServerCertificate/Encrypt>` |
| `ConnectionStrings__MigrationConnection` | `Server=9.223.115.62,51433;Database=CrownusDB_test;User Id=<from user: migrator login>;Password=<from user>;<rest of the VM's options>` |

Copy the trailing options (Encrypt, TrustServerCertificate, timeouts) from the VM's existing strings. `DotEnvLoader` only sets `TrustServerCertificate` for localhost, so a non-local server with a self-signed certificate needs it written explicitly in Form A.

**Form B: atomic values.**

| Name | Value |
|---|---|
| `CROWNUS_DB_HOST` | `9.223.115.62` |
| `CROWNUS_DB_PORT` | `51433` |
| `CROWNUS_DB_NAME` | `CrownusDB_test` |
| `CROWNUS_DB_APP_USER` / `CROWNUS_DB_APP_PASSWORD` | `<from user>` |
| `CROWNUS_DB_MIGRATOR_USER` / `CROWNUS_DB_MIGRATOR_PASSWORD` | `<from user>` |
| `CROWNUS_DB_ADMIN_USER` / `CROWNUS_DB_ADMIN_PASSWORD` | `<from user>` (only if the VM sets them) |

## Secrets and identity (copy from the VM, never commit)

| Name | Value |
|---|---|
| `Encryption__MasterKey` | `<from user>` (32+ chars; must equal the VM's or existing encrypted data is unreadable) |
| `Jwt__Secret` | `<from user>` |
| `Jwt__AdminSecret` | `<from user>` |
| `TrayApp__Jwt__SigningKeys__0__Kid` | `<from user>` (required outside Development/Test; see `docs/deployment/tray-device-jwt.md`) |
| `TrayApp__Jwt__SigningKeys__0__PrivateKeyPem` | `<from user>` (PEM on one line, literal `\n` between lines) |
| `TrayApp__Jwt__ActiveKid` | `<from user>` |
| `TrayApp__Jwt__AcceptLegacyHs256Until`, `TrayApp__Jwt__Secret` | only if the VM still has them (legacy cut-over) |
| `PlatformBootstrap__SuperAdminEmail` | `<from user>` |
| `PlatformBootstrap__SuperAdminFullName` | `<from user>` |
| `PlatformBootstrap__AllowPasswordBootstrap` + `DevAdmin__Password` | only for a fresh DB's first admin; `<from user>`; remove right after first login |
| `TrayReleases__IngestToken` | `<from user>` if the VM sets it |

## Other settings (from `.env.example` / `appsettings.json`; copy whatever the VM sets)

| Name | Value |
|---|---|
| `Email__FromAddress` | `<from user>` |
| `Email__FromName` | as on the VM (default `CROWNUS`) |
| `Email__ReplyToEmail` | as on the VM, if set |
| `Planner__Enabled` | `true` if the VM enables the Planner |
| `ASPNETCORE__Swagger__Enabled` | leave unset (off) unless the VM sets it |
| `AllowedOrigins__0` ... | not needed when the SPAs are served same-origin; set only if the VM has extra origins |
| `AdminCookieDomain` | leave empty (host-only cookie) in single-host mode |
| `Urls__WebhookBaseUrl` | `https://<host>` if the VM sets it |
| `Urls__CalendarOAuthCallbackBaseUrl` | `https://<host>` if the VM sets it (and register that callback with the OAuth provider) |
| `Email__AppBaseUrl`, `Email__AdminConsoleBaseUrl` | same values as the `Urls__` pair if the VM sets them |
| `Kestrel__Certificates__Default__Path` / `__KeyPath` | do not set on App Service (TLS terminates at the front end) |

## Portal toggles (not app settings)

WebSockets On (SignalR), Always On if the tier allows, HTTPS Only On.
