# Task 02: One database connection per request

| | |
|---|---|
| Priority | 2 of 30 (lower number = build first) |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | Performance, DB |
| Depends on | nothing |

## What we found

`TenantRlsInterceptor` sets the tenant session context when a connection opens and clears it when the connection closes. EF Core opens and closes the connection around every single query unless a transaction is open. Nothing in `ONEVO.Infrastructure` or `ONEVO.Api` calls `OpenConnectionAsync`.

So a handler that runs 6 queries probably makes 18 round trips to SQL Server: 6 to set context, 6 real queries, 6 to clear context. This affects every endpoint and is the most likely single reason many APIs feel slow.

## What to do

1. Confirm first. Turn on EF command logging in Development, call one read endpoint, and count the `sp_set_session_context` calls.
2. Open the connection once per DI scope and keep it open until the scope ends. A good place is the point where the tenant context is first resolved (`TenantContextSwitcher`), or a small scoped service that the DbContext registration uses.
3. Keep the tenant switch safe: when `ITenantContextSwitcher` changes tenant in the middle of a scope, the session context must be set again on the already open connection. Check the existing rollback restore logic still works.
4. Check background jobs and the ticket stores, which create their own scopes.
5. Run the architecture suite and the RLS integration tests.

## Done when

- A read endpoint with N queries shows 2 context calls in the log, not 2 times N.
- Before and after timings for five common endpoints are recorded in `docs/reports/performance/`.
- All tenant isolation tests pass.
