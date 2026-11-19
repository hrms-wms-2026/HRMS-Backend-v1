# ONEVO HRMS — Backend

.NET API for the ONEVO HRMS platform.

| Path | |
|---|---|
| `src/` | `ONEVO.Api`, `ONEVO.Application`, `ONEVO.Domain`, `ONEVO.Infrastructure` |
| `tests/` | Unit, integration and architecture test projects |
| `ops/` | Operational scripts (SQL Server) |
| `schema-snapshots/` | Per-domain database schema snapshots and their generator |
| `docs/` | All other documentation — start at [docs/README.md](docs/README.md) |

## Running locally

The database is SQL Server, with row level security for tenant isolation. To create a local database, see [ops/sqlserver/README.md](ops/sqlserver/README.md).

1. Copy `.env.example` to `.env` and fill in the values.
2. `dotnet run --project src/ONEVO.Api/ONEVO.Api.csproj --launch-profile https`

Ports for every app are listed in [docs/development/ports.md](docs/development/ports.md).
