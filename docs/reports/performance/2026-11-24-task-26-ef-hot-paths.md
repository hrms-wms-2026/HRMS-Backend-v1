# Task 26: blocking calls and EF hot paths

Date: 2026-11-24. Branch `perf/task-26-async-ef-hot-paths`, stacked on task 02 (#238). Decisions: DEC-091, DEC-092.

## Changed

| Item | Before | After |
|---|---|---|
| `DomainEventDispatchInterceptor` sync path | `SavedChanges` ran the async event dispatch with `.GetAwaiter().GetResult()` | Synchronous `SaveChanges` is refused in `SavingChanges`, before anything is written. Nothing in `src` or `tests` calls it. |
| `MeetingIntegrityInterceptor` sync path | `TransactionCommitting` ran the async check with `.GetAwaiter().GetResult()` | A synchronous commit with meeting rows to check is refused. A synchronous commit with nothing to check passes through; `EnsureCreated` makes one. |
| `EfOnboardingPersistenceRepositories.ListByOffboardingRecordAsync`, `EfOffboardingTaskBypassRequestRepository.ListPendingByApproverAsync` | `.ContinueWith(t => t.Result, ct)` | `await`. The old form wrapped a failure in `AggregateException`, and a cancelled token cancelled the continuation even after the query had finished. |
| Guard | none | `NoBlockingAsyncArchitectureTests` fails on `.GetAwaiter().GetResult()`, `.Wait()` or `.ContinueWith(` in `src`. The only exception is `DevCertificateBootstrapper`, startup tooling. |

## Checked, no change needed

- **`CancellationToken` from controller to repository.** A scan that matches the full parameter list finds all 780 async controller actions already take a `CancellationToken`. The eight `CancellationToken.None` on request paths are deliberate, and each has a comment saying why:
  - meeting-link provisioning must finish once it has claimed a row;
  - attachment clean-up runs after a failure;
  - the session-lookup context is reset in a `finally`.

  The task's "20 slowest endpoints" list waits for the task 06 Part B baseline.

## Measured, not changed

Release build, SQLite in-memory (the EF overhead is the same on SQL Server), warm, `Stopwatch` over many iterations.

| Measure | Result |
|---|---|
| Create and dispose one `ApplicationDbContext` with its interceptors (the most pooling could save) | 57 µs per request (20,000 iterations) |
| Session by key hash, normal LINQ | 90–118 µs per query (5,000 iterations, two rounds) |
| Same query, `EF.CompileAsyncQuery` | 38–67 µs per query, about 52 µs saved |
| `/inbox/counts`, a typical request (task 02 timing run, 2026-11-23) | about 155 ms |

### DbContext pooling: not done

- **It is not possible as the code stands.** `ApplicationDbContext` takes scoped services in its constructor: three interceptors and `ITenantContext`. `AddDbContextPool` needs a constructor that takes only the options, plus state reset by hand on every rent. Pooling would mean moving tenant and user state out of the constructor and into a per-request reset. That is a risky change to the code that enforces tenant isolation.
- **The most it could save is about 57 µs per request**, about 0.04 % of a 155 ms request.
- Decision: keep `AddDbContext`. Look at this again only if a load test shows context creation in the profile.

### Compiled queries: not added (DEC-091)

- Compiling saves about 52 µs per query.
- The three lookups the task names all run inside `TenantDatabaseTicketStore.LoadAsync`: session by key hash, tenant by id, and permissions for the user. That method runs only when the task 03 ticket cache misses. A page's many requests reuse the cached ticket.
- So the saving is about 150 µs once per cache miss, not on each request. Not worth three hand-written query delegates.

### AsSplitQuery: not added

- Only one query includes two or more collections: `ModuleCatalogRepository.GetByKeyAsync`, which loads features, permission ownership and price history for one module.
- Local data, largest module (`monitoring`): the single query returns 39 joined rows (13 × 3). A split query would read 17 rows over 4 round trips.
- The endpoint is admin-only and loads one module, so the single query stays.
- Look at this again if price history grows into the tens of rows per module.

## Not done

- **Thread-pool starvation load run.** The task's "done when" asks for one. It belongs with the task 29 load test against UAT. No numbers here.
