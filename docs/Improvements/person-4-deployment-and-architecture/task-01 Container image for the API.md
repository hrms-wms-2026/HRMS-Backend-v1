# Task 01: Container image for the API

| | |
|---|---|
| Priority | 1 of 30 (lower number = build first) |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day |
| Area | Deployment |
| Depends on | nothing |

## What we found

There is no Dockerfile, no compose file and no publish script in the repository. The only automation is `.github/workflows/ci.yml`, which builds and tests. Nobody can deploy the API to UAT in a repeatable way today.

SkiaSharp is referenced with `SkiaSharp.NativeAssets.Linux.NoDependencies`, so a Linux image is the expected target.

## What to do

1. Add a multi-stage `Dockerfile` for `src/ONEVO.Api`: SDK image to restore and publish in Release, ASP.NET runtime image to run.
2. Run as a non-root user. Expose one HTTP port. TLS ends at the load balancer, not in the container.
3. Add a `.dockerignore` (bin, obj, tests, docs, `.env`, `.certs`).
4. Make sure `DevCertificateBootstrapper` and automatic migration stay inside the `IsDevelopment()` checks so the container never tries either.
5. Add a `HEALTHCHECK` that calls `/health`.
6. Write `docs/deployment/container.md`: build command, required environment variables (copy the list from `.env.example`), ports, health URLs.

## Done when

- `docker build` works from a clean clone.
- The container starts with only environment variables, connects to a SQL Server, and `/health` and `/health/ready` both return 200.
- Image size and start-up time are written in the doc.
