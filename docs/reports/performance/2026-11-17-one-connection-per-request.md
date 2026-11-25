# One database connection per request: before and after

Improvements task 02. Design: `docs/superpowers/specs/next/2026-11-17-one-connection-per-request-design.md`.

## How it was measured

- **Setup:** a local Debug API on port 7300 with SQL command logging on, against the local SQL Server, signed in as the dapi tenant owner.
- **Builds compared:** the same branch with and without the change (`development` at `0bcdf8e3`), started one after the other against the same database.
- **Calls:** two warm-up calls, then 10 calls per endpoint, made one at a time. The table shows the median.

## Context round trips per request

These come from temporary counters in the interceptor on `development`, before the change:

| Endpoint | EF commands | Context commands before | Context commands after |
|---|---|---|---|
| `GET /calendar/my-timezone` | 13 | 12 | 1 |
| `GET /employees/me` | 23 | 22 | 1 |
| `GET /auth/me` | 20 | 19 | 1 |
| `GET /inbox/counts` | 949 | 948 | 1 |

- **Before:** one extra round trip per command.
- **After:** one round trip per request, plus one more each time the tenant context changes or a transaction ends. The integration test `A_kept_open_request_sets_the_context_once_and_again_only_when_it_changes` proves the "after" numbers.

## Response times (median of 10, local)

| Endpoint | Before | After | Change |
|---|---|---|---|
| `GET /auth/me` | 27.3 ms | 25.6 ms | -1.7 ms |
| `GET /employees/me` | 41.3 ms | 38.7 ms | -2.6 ms |
| `GET /calendar/my-timezone` | 23.6 ms | 22.0 ms | -1.6 ms |
| `GET /notifications/unread-count` | 21.5 ms | 21.4 ms | 0 |
| `GET /inbox/counts` | 758 ms | 531 ms | -227 ms (-30%) |

The database is on the same machine here, so one round trip costs a fraction of a millisecond. On a server where the database is across a network, each saved round trip saves that network latency. The gain then grows with the number of queries an endpoint runs: inbox counts saves 947 round trips per call.

## Errors

The API log shows the same startup seeder errors in both runs:

- the stale `crownus_app_provider` column in the local database;
- a duplicate `break_records` row from the demo history seeder.

Neither comes from this change. No request returned an error.
