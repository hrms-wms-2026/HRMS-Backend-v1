# One database connection per request: design

Improvements task 02 (`docs/Improvements/person-2-performance-and-db/task-02 One database connection per request.md`).
Status: implemented on `perf/one-connection-per-request`. Timings: `docs/reports/performance/2026-11-17-one-connection-per-request.md`.

## 1. What was measured

Measured on 2026-11-17 against a local Debug API with the dapi owner session. Temporary counters were placed in `TenantRlsInterceptor` and removed afterwards.

| Endpoint | EF commands | Connection opens |
|---|---|---|
| `GET /calendar/my-timezone` | 13 | 12 |
| `GET /employees/me` | 23 | 22 |
| `GET /auth/me` | 20 | 19 |
| `GET /inbox/counts` | 949 | 948 |

EF opens and closes the connection around almost every command. Each open runs `ConnectionOpenedAsync`, which sends one extra command (two `sp_set_session_context` calls). So the cost is **two round trips per query**, not three as the task guessed:

- `ConnectionClosing` is overridden only in its synchronous form. EF closes asynchronously, so the clear-on-close command never runs.
- The pooled connection's `sp_reset_connection` clears the session context instead, which is fine.
- EF's own command log does not show the extra command, because the interceptor runs it as a raw `DbCommand`.

## 2. What today's code guarantees (must not change)

`TenantRlsInterceptor` sets the session context from `ITenantContext` **every time EF opens a connection**. EF opens a connection:

- **(a)** before a command outside a transaction;
- **(b)** when a transaction begins, keeping it open until the transaction commits or rolls back.

Code that relies on this:

1. **Host resolution.** `HostTenantResolutionMiddleware` reads `tenants` in system mode, then calls `Resolve(tenant)`. The next query must run as that tenant.
2. **Switching outside a transaction.** `TenantContextSwitcher.SwitchToTenantAsync` closes an open connection so the next open picks up the new tenant.
3. **Switching inside a transaction.** The switcher sets the context directly and records the old values. `TransactionRolledBack(Async)` puts those back, because `SESSION_CONTEXT` is not transactional.
4. **Pre-tenant login steps.** `PostgresMfaChallengeStore` and `EfLegalLoginChallengeRepository` close the connection around a pre-tenant read for the same reason.
5. **Meeting integrity check.** `MeetingIntegrityInterceptor` elevates to admin inside a transaction and restores the values it read. If the restore fails, it closes the connection.
6. **Separate scopes.** Background jobs, seeders, `TenantDatabaseTicketStore` and `WidgetDataOrchestrator` children each use their own scope and DbContext. They are out of scope and keep today's behaviour.

The invariant to keep: **every EF command runs with the session context that today's open-per-command behaviour would have given it.**

## 3. Design

### 3.1 Keep the request's connection open

Add a small middleware, `RequestDbConnectionMiddleware`, placed right after `UseRouting`. It applies only to `/api/` and `/admin/` routes. Hubs and health checks are left out.

- It calls `ApplicationDbContext.Database.OpenConnectionAsync()` once.
- EF does not close a connection the caller opened. The DbContext closes it when the request scope is disposed, so no explicit close is needed.
- Every authenticated request already reads the database, so opening eagerly costs no extra connection.

### 3.2 Re-apply the context exactly where today's code would have reopened

`TenantRlsInterceptor` also becomes an `IDbCommandInterceptor`. It keeps `_applied`: the (tenant id, mode) last written to this connection, or *unknown*.

Two places count as a "reopen point", matching (a) and (b) in section 2:

- **Before every command outside a transaction:** `ReaderExecuting`, `NonQueryExecuting` and `ScalarExecuting`, in both sync and async forms, when `eventData.Context.Database.CurrentTransaction` is null.
- **When a transaction starts:** `TransactionStarting` and `TransactionStartingAsync`.

At a reopen point, compute the context from `ITenantContext` exactly as `ConnectionOpened` does today. If it differs from `_applied`, or `_applied` is unknown, send the existing `SetTenantCommand` and record it. If it is the same, send nothing. This is the whole saving.

Commands inside a transaction are never touched, so cases 3 and 5 behave exactly as today.

`_applied` becomes *unknown* (forcing the next reopen point to set the context) on:

| Event | Why |
|---|---|
| `ConnectionOpened` | set as today, then record |
| `ConnectionClosed` | a reopened connection starts clean |
| `TransactionCommitted` / `TransactionRolledBack` | today the connection closed here and the next command reopened it |
| `TenantContextSwitcher` writing the context directly (new `ForgetAppliedContext()`) | a write the interceptor did not make |

Raw ADO commands (`GetDbConnection().CreateCommand()`) do not pass through EF interceptors. The four places that use them were checked:

- the switcher, `MeetingIntegrityInterceptor` and `EfSessionRepository` use them only inside transactions, which is unchanged;
- the MFA and legal stores close the connection first, which marks the context *unknown* (case 4 still works).

### 3.3 Not changed

- `SetTenantCommand`, the session context keys and the RLS policies;
- the rollback restore logic;
- background jobs and other scopes;
- `ConnectionClosing`, which is kept as is (it only clears).

## 4. Risks and open questions

| # | Risk | Plan |
|---|---|---|
| R1 | A context change between commands is missed and a query runs under the wrong tenant | Reopen points mirror today's opens. Integration tests 2 to 6 below cover each writer in section 2 |
| R2 | Connections are held for the whole request, including time spent off the database (external calendar calls, uploads), which can exhaust the pool under load | Pool default is 100. Measure in the task 29 load run. Exclude long upload or download routes if needed |
| R3 | Retry on failure (`EnableRetryOnFailure(3)`) with a caller-opened connection after a broken connection | Integration test 8 kills the session mid-request. **Found in implementation:** SqlClient's idle connection resiliency (`ConnectRetryCount`) silently reconnected to a new session with an empty context, so reads returned nothing (failed closed). Fixed: the app connection string sets `ConnectRetryCount = 0` (`TenantRlsInterceptor.WithoutSilentReconnect`), so EF's retry reopens through `ConnectionOpened`. The interceptor also records the SqlClient `ClientConnectionId` with the applied context and writes again if it changes |
| R4 | A pooled connection keeps the context into the next request | `sp_reset_connection` clears it when the connection is reused. Test 7 checks this |
| R5 | Synchronous command paths | Sync overloads are implemented the same way. Task 26 removes the sync callers later |

## 5. Tests that must pass

Integration tests on SQL Server (Testcontainers). Tests 2 to 6 are written first against **today's code** as characterisation tests and must stay green after the change:

1. **Round trips:** one request with N reads and no context change sends one context command, not N.
2. **Host resolution:** system mode reads `tenants`, `Resolve(A)`, then reads only tenant A rows.
3. **Switch outside a transaction:** tenant A to tenant B, then reads only tenant B rows.
4. **Switch inside a transaction, then rollback:** the rows visible afterwards are the same as today.
5. **Meeting integrity elevation:** after commit, the next read is not in admin mode (RLS still filters).
6. **MFA and legal pre-tenant read:** the read, then the next tenant read, give correct rows.
7. **Pooled reuse:** two sequential scopes for tenants A and B on one pooled connection each see only their own rows.
8. **Broken connection:** with retry on, the next command reopens and runs with the right tenant.

Also required:

- the full unit and architecture suites;
- the existing RLS integration suite;
- before and after timings for five endpoints in `docs/reports/performance/`.

## 6. Expected gain

One extra round trip is saved per query outside a transaction. Locally that is the 12, 22 and 19 extra commands on the small endpoints above, and 948 on inbox counts. The real milliseconds will be measured after the change; this design does not guess them.
