# UAT configuration

Task 05 (`docs/Improvements/person-4-deployment-and-architecture/task-05 UAT settings and secrets.md`).
Every setting a UAT (or any deployed) API needs: where its value comes from, whether it is a secret, and what
the start-up check does with it.

How the image runs and how to try it locally: [container.md](container.md). App Service specific values:
[ops/appservice/appservice-settings.md](../../ops/appservice/appservice-settings.md).

## Rules

- **Run UAT as `ASPNETCORE_ENVIRONMENT=Staging`** (or `Production`). Never `Development`: that applies
  migrations on start, runs the demo seeders and generates a local certificate.
- **Every value comes from an environment variable** (`Section__Key`, array entries as `Key__0`, `Key__1`).
  On App Service they are Application settings; on a VM or container host they are the host's environment or
  secret store. There is no `appsettings.Staging.json`: UAT hosting and its domain are not decided yet, and
  a committed file would hold the wrong values for whichever host is chosen.
- **Secrets never go in a file in the repository** (the `.env` file is for local development only and is not
  in the container image). On Azure, prefer Key Vault references in App Service settings.
- **The API refuses to start** outside Development/Test when a required value below is missing, a placeholder
  (`<...>`, `CHANGE_ME`, `SET_VIA_ENV`, `PLACEHOLDER`) or still points at a local or example host
  (`localhost`, `127.0.0.1`, `example.com/.org/.net`, `*.test`, `*.localhost`, `*.local`, `*.example`,
  `*.invalid`). The error lists every problem by key, never by value. Code:
  `src/ONEVO.Infrastructure/Configuration/DeploymentConfigurationValidator.cs`.

## Required: the API does not start without these

| Variable | Secret | Where the value comes from | Check |
|---|---|---|---|
| `ConnectionStrings__DefaultConnection` | yes | Runtime login `crownus_app` on the UAT database. Alternatively `CROWNUS_DB_HOST`, `CROWNUS_DB_PORT`, `CROWNUS_DB_NAME`, `CROWNUS_DB_APP_USER`, `CROWNUS_DB_APP_PASSWORD`, which the API joins into the string (it sets `TrustServerCertificate` only for `localhost`) | not empty or placeholder |
| `Encryption__MasterKey` | yes | Generated once per environment, 32+ random characters, kept in the secret store. Must stay the same for the life of the database or encrypted data cannot be read | not empty or placeholder, 32+ characters |
| `Tenancy__RootDomain` | no | The UAT root domain. Tenants are `<slug>.<root>`, the admin console is `admin.<root>`. In single-host mode (`Tenancy__Mode=SingleHost`) it is the one host name | bare host name, not local or example |
| `Urls__AppBaseUrl` | no | `https://<root>`, used in invitation and password-reset links | absolute `https` URL, real host |
| `Urls__AdminConsoleBaseUrl` | no | `https://admin.<root>` (single-host: `https://<host>/admin-console`) | absolute `https` URL, real host |
| `Email__FromAddress` | no | Sender address on a domain the email provider is verified for | not empty or placeholder |
| `TrayApp__Jwt__SigningKeys__0__Kid`, `TrayApp__Jwt__SigningKeys__0__PrivateKeyPem`, `TrayApp__Jwt__ActiveKid` | key yes | ES256 tray signing key generated for UAT, PEM on one line with literal `\n`. See [tray-device-jwt.md](tray-device-jwt.md) | checked by the tray key loader |

## Checked when set

These may be left empty. When set, the start-up check rejects a placeholder or a local host.

| Variable | Secret | Value for UAT | Check |
|---|---|---|---|
| `AllowedOrigins__0`, `__1`, … | no | Usually none. Tenant origins (`https://*.<root>`) are allowed automatically. Add only an extra tool origin outside the root domain. The old placeholders (`http://localhost:5173`, `https://app.example.com`) were removed from `appsettings.json`: an environment variable can replace an array entry but never remove one | each an `https` URL on a real host |
| `AdminCookieDomain` | no | Empty (host-only cookies), or `.<root>` to share the admin cookie across subdomains | not local or example |
| `ForwardedHeaders__KnownProxies__0`, … | no | IP address of each load balancer or reverse proxy in front of the API | each a valid IP |
| `ForwardedHeaders__KnownNetworks__0`, … | no | CIDR of the load balancer subnet. App Service uses `0.0.0.0/0` and `::/0` because its front ends have no fixed address and the app is not reachable any other way | each a valid CIDR |
| `Urls__WebhookBaseUrl` | no | `https://<root>` once a provider calls back (Stripe, calendar) | `https` URL, real host |
| `Urls__CalendarOAuthCallbackBaseUrl` | no | `https://<root>`, registered with the OAuth provider | `https` URL, real host |
| `Email__AppBaseUrl`, `Email__AdminConsoleBaseUrl` | no | Same as the `Urls__` pair, if set | `https` URL, real host |
| `Swagger__Enabled` | no | Leave unset. Swagger has no sign-in of its own and lists every route of both APIs | must not be `true` |

Behind a load balancer the client IP and the tenant host come from `X-Forwarded-For` and
`X-Forwarded-Host`. They are trusted only from loopback plus the proxies and networks above, and forwarded
hosts are limited to the root domain and its subdomains. If the proxy is on the same machine (IIS with ARR
on the VM), loopback is already trusted and no entry is needed. If the proxy is not trusted, tenant
subdomains do not resolve and every caller shares the proxy's rate limit.

## Other settings

Not checked at start-up. Defaults come from `appsettings.json`.

| Variable | Secret | Notes |
|---|---|---|
| `ConnectionStrings__MigrationConnection` | yes | `crownus_migrator` login. Only the migration step needs it (task 09), not the running API |
| `Jwt__Secret`, `Jwt__TenantIssuer`, `Jwt__TenantAudience` | secret yes | MFA challenge and device tokens; a warning is logged when missing |
| `Email__FromName`, `Email__ReplyToEmail` | no | Default `CROWNUS` |
| `PlatformBootstrap__SuperAdminEmail`, `PlatformBootstrap__SuperAdminFullName` | no | First platform admin |
| `PlatformBootstrap__AllowPasswordBootstrap` + `DevAdmin__Password` | password yes | Only for a fresh database's first admin. Remove both right after the first sign-in |
| `TrayReleases__IngestToken` | yes | Lets the tray build pipeline publish releases |
| `AwsRekognition__Region`, `AwsRekognition__LivenessRoleArn` | no | Face check. The default role ARN is a placeholder (`000000000000`), so face liveness does not work until it is set |
| `Tenancy__Mode` | no | `SingleHost` only for the App Service test setup (tenant from the `X-Crownus-Tenant` header) |
| `Hosting__ServeSpas`, `Hosting__SpaRoot` | no | Serve the web and admin apps from the API (App Service package) |
| `Planner__Enabled` | no | Turns the day planner on |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | yes | Sends traces, metrics and logs to Application Insights. See [observability.md](observability.md) |
| `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS` | headers yes | Alternative: send telemetry to an OTLP backend |
| `BackgroundJobs__Enabled` | no | Default `true`. `false` runs no recurring jobs (web-only instance). Keep `true` until Redis is configured; see [background-jobs.md](background-jobs.md) |
| `Redis__ConnectionString` | yes | Needed for more than one instance: shared key ring, session evictions, presence, sign-in limits, MFA, SignalR backplane. See [shared-state.md](shared-state.md) |
| `DataProtection__KeyEncryptionCertificate`, `DataProtection__KeyEncryptionCertificatePassword` | yes | Encrypts the key ring at rest in Redis (base64 PFX and its password) |
| `RateLimiting__*` | no | See [rate-limits.md](rate-limits.md) |
| `Serilog__MinimumLevel__Default` | no | `Information` by default |

Provider credentials (email, storage, OAuth apps, payments) are not settings: they are saved through the
admin console into encrypted database tables.

## Host names

- `AllowedHosts` stays `*`. Host filtering is done by `HostTenantResolutionMiddleware`, which answers `400`
  to any host outside `Tenancy:RootDomain` except the two health URLs. Setting `AllowedHosts` to the root
  domain as well would add nothing, and would make the container health check (`localhost`) and
  load-balancer probes (by IP) fail.
- DNS needs the root, `admin.<root>` and a wildcard `*.<root>`, all pointing at the load balancer, with a
  certificate that covers the wildcard. Single-host mode needs only the one host.

## What happens on a bad value

Container started in `Staging` with `Tenancy__RootDomain=crownus.test`, `Urls__AppBaseUrl=http://localhost:4200`
and no admin URL or sender address (2026-11-19). It stops before the host is built:

```text
Unhandled exception. System.InvalidOperationException: The API cannot start in the 'Staging' environment: 4 setting(s) are missing or invalid.
 - Email:FromAddress is required and is empty or a placeholder.
 - Tenancy:RootDomain points at a local or example host.
 - Urls:AppBaseUrl must use https.
 - Urls:AdminConsoleBaseUrl is required and is empty or a placeholder.
Set them as environment variables (Section__Key). See docs/deployment/uat-configuration.md.
```

## Checked in a UAT-like container (2026-11-19)

Image from [container.md](container.md), `Staging`, local SQL Server, only the required settings above plus
`Tenancy__RootDomain=crownus-uat.com` and `ForwardedHeaders__KnownNetworks__0=172.16.0.0/12` (the Docker
network the requests arrive from, standing in for the load balancer). Requests were sent the way a load
balancer forwards them (`X-Forwarded-Host`, `X-Forwarded-Proto: https`) to
`POST /api/v1/auth/session-exchange`, which only runs on a tenant host:

| Forwarded host | Result | Meaning |
|---|---|---|
| `dapi.crownus-uat.com` | `401` "This sign-in link is invalid or has expired" | tenant `dapi` resolved, request reached the handler |
| `nosuch.crownus-uat.com` | `404` | unknown tenant |
| `crownus-uat.com` | `400` "only available on a tenant host" | root host, System mode |
| `dapi.crownus.test` | `400` | outside the root domain |
| none (`localhost`) | `400` | outside the root domain |
| `dapi.crownus-uat.com` with no `KnownNetworks` | `400` | header from an untrusted proxy ignored |

`/health` returned `200` 4.3 s after start, `/health/ready` `200`, and no error was logged. The only start-up
warnings were the optional `Jwt:Secret` and `Urls:WebhookBaseUrl`.
