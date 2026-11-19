# Container image for the API

Task 01 (`docs/Improvements/person-4-deployment-and-architecture/task-01 Container image for the API.md`).
The image runs `src/ONEVO.Api` on Linux with only environment variables for settings.

## Build

From the repository root:

```bash
docker build -t crownus-api:<tag> .
```

- `Dockerfile` is multi-stage: the .NET 10 SDK image restores and publishes in Release for `linux-x64`,
  the ASP.NET 10 runtime image runs it.
- The restore step copies only the four `src/*/*.csproj` files first, so it is cached until a package
  reference changes.
- `.dockerignore` keeps `tests/`, `docs/`, `ops/`, `bin/`, `obj/`, `.env*`, `.certs/`, key files and
  `appsettings.Development.json` out of the build context. No secret or local setting is baked in.

## What the image does

| Item | Value |
|---|---|
| Base image | `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian) plus `curl` for the health check |
| User | non-root `app` (uid 1654, `$APP_UID` from the base image) |
| Port | `8080`, plain HTTP. TLS ends at the load balancer |
| Environment | `ASPNETCORE_ENVIRONMENT=Production` by default; override with `-e` (for example `Staging` for UAT) |
| Entry point | `dotnet ONEVO.Api.dll` |
| Health check | `curl http://localhost:8080/health` every 30 s, 60 s start period |

Outside `Development` the API never runs the local certificate bootstrapper, never applies migrations
on start and never runs the demo seeders. Migrations are a separate deploy step (task 09).
Reference-data seeders (permissions, roles, lookups, modules) still run on every start; task 13 moves
them into the deploy step.

## Health URLs

| URL | Checks | Use it for |
|---|---|---|
| `GET /health` | process only | liveness, the container `HEALTHCHECK` |
| `GET /health/ready` | SQL Server reachable (`SELECT 1`) | load-balancer readiness |

## Settings

All settings come from environment variables (`Section__Key`). Nothing is read from a `.env` file in
the container: the loader finds none, and `.dockerignore` keeps it out of the image.

Required to start outside Development:

| Variable | Secret | Notes |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | yes | Runtime login (`crownus_app`). Or set the atomic values below and let the API build it |
| `CROWNUS_DB_HOST`, `CROWNUS_DB_PORT`, `CROWNUS_DB_NAME`, `CROWNUS_DB_APP_USER`, `CROWNUS_DB_APP_PASSWORD` | password yes | Alternative to the full connection string. `TrustServerCertificate` is only set for `localhost`, so a server with a self-signed certificate needs the full connection string instead |
| `Encryption__MasterKey` | yes | 32+ characters. Must be the same value the database was written with |
| `TrayApp__Jwt__SigningKeys__0__Kid`, `TrayApp__Jwt__SigningKeys__0__PrivateKeyPem`, `TrayApp__Jwt__ActiveKid` | key yes | ES256 tray signing key, required outside Development/Test. See `docs/deployment/tray-device-jwt.md` |

Needed for a working environment (the API starts without them and logs a `[CONFIG]` warning):

| Variable | Secret | Notes |
|---|---|---|
| `Urls__AppBaseUrl`, `Urls__AdminConsoleBaseUrl`, `Urls__WebhookBaseUrl` | no | Links in emails, OAuth callbacks |
| `Tenancy__RootDomain` | no | Tenant subdomains resolve under it |
| `AllowedOrigins__0`, `AllowedOrigins__1`, … | no | Replace the placeholder origins in `appsettings.json` |
| `ForwardedHeaders__KnownProxies__0` / `ForwardedHeaders__KnownNetworks__0` | no | The load balancer, so client IP and host come from `X-Forwarded-*` |
| `Email__FromAddress`, `Email__FromName` | no | Provider keys are stored in the database, not here |
| `Jwt__Secret`, `Jwt__TenantIssuer`, `Jwt__TenantAudience` | secret yes | MFA challenge and device tokens |
| `ConnectionStrings__MigrationConnection` | yes | Only for the migration step, not the running API |
| `ASPNETCORE__Swagger__Enabled` | no | Leave unset (off) in UAT |

Task 05 turns this into the full UAT list and makes the API refuse to start on a placeholder.

## Run locally against the dev database

The local SQL Server container uses a self-signed certificate, so pass a full connection string with
`TrustServerCertificate=True`, and use `host.docker.internal` to reach the host:

```bash
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Staging \
  -e "ConnectionStrings__DefaultConnection=Server=host.docker.internal,1433;Database=CrownusDb;User Id=crownus_app;Password=<app-password>;TrustServerCertificate=True" \
  -e Encryption__MasterKey=<same-key-as-.env> \
  -e TrayApp__Jwt__SigningKeys__0__Kid=local-container \
  -e "TrayApp__Jwt__SigningKeys__0__PrivateKeyPem=<one-line PEM with \n>" \
  -e TrayApp__Jwt__ActiveKid=local-container \
  crownus-api:<tag>
```

Then `http://localhost:8080/health` and `http://localhost:8080/health/ready` should both return 200.

## Known limits (other tasks)

- **One instance only** until task 21: rate-limit counters, web and tray presence, SignalR and the
  ASP.NET data-protection key ring live in process memory. The key ring is also lost on restart, which
  only breaks OAuth sign-in flows that are in progress at that moment.
- Background jobs run inside every API container until task 13 adds `BackgroundJobs:Enabled`.
- Logs go to the console (stdout) only; the host collects them. Task 17 adds a log and metrics target.

## Measured

2026-11-19, Docker Desktop 29.6 on Windows 11 (WSL 2), local SQL Server container, `ASPNETCORE_ENVIRONMENT=Staging`:

| What | Result |
|---|---|
| Image size | 482 MB (the ASP.NET runtime is about 230 MB; Semantic Kernel, Stripe and SkiaSharp make up most of the rest) |
| First build, no cache | about 15 minutes, most of it downloading the SDK and runtime images |
| Rebuild after a code change (restore cached) | 48 s |
| Start-up, container start to first `200` from `/health` | 4.3 s |
| `GET /health` / `GET /health/ready` | `200` / `200 Healthy` |
| Docker health status | `healthy` |
| Container user | `1654` (non-root) |

The log shows one expected warning, `Failed to determine the https port for redirect`: there is no HTTPS
port in the container, so `UseHttpsRedirection` does nothing and the load balancer must redirect HTTP
to HTTPS. Five `[CONFIG]` warnings list the settings from the second table that this test left unset.

## Health probes and the host check

`HostTenantResolutionMiddleware` answers `400` to any host that is not `Tenancy:RootDomain` or one of its
subdomains. The container health check calls `localhost`, and load balancers usually probe by IP, so
before this task both health URLs failed with `400` in a container. Exactly `/health` and
`/health/ready` now skip the host check and run in System mode on any host; they read no tenant data.
Every other path keeps the check (unit tests in `HostTenantResolutionMiddlewareTests`).
