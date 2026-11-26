# Endpoint timing baseline (task 06 Part B)

Times every tenant `GET` endpoint on a UAT-sized data set and writes
[docs/reports/performance/2026-11-baseline.md](../../docs/reports/performance/2026-11-baseline.md), slowest first, with
the SQL command count of each call.

## What it measures

- **Data:** `DapiPerformanceBaselineSeeder` fills the Dapi demo tenant to 500 employees (a five-level reporting line), 50
  projects, 20,000 tasks and 365 days of attendance. It runs only in Development and only when
  `Seeding:PerformanceBaseline:Enabled` is true.
- **Caller:** the Dapi owner, who sees the whole tenant. That is the widest scope, so most lists are at their largest.
- **Calls:** one at a time. Each endpoint gets one warm-up call, then 20 timed calls. p50 and p95 are server times from
  the request log line (`HTTP GET {route} responded {status} in {ms} ms ({n} SQL, {ms} ms)`).
- **Route parameters:** filled from earlier responses. `{id}` comes from the list endpoint above it, and named ids
  such as `{employeeId}` come from any response field with that name. Routes it cannot fill are listed as skipped.
- **Not covered:** the admin API, writes (`POST`/`PUT`/`DELETE`), concurrency (see the k6 load test in
  `ops/loadtest`) and network time to a remote database. The local database sits on the same machine.

## Run it

1. Create a **throwaway** database. Never use a shared one, or your everyday local one:

   ```powershell
   ./ops/sqlserver/setup-local-db.ps1 -Database CrownusDb_perf -RunMigrations
   ```

2. Sign in as the seeded Dapi owner. It is a test account, defined in `DevSmokeTestTenantSeeder`:

   ```powershell
   $env:BASELINE_EMAIL = "<Dapi owner email>"; $env:BASELINE_PASSWORD = "<its seeded password>"
   ```

3. Run the baseline:

   ```powershell
   ./ops/perf-baseline/run-baseline.ps1 -Database CrownusDb_perf
   ```

   The script builds the API, starts it on port 7339 and waits for the seed. The first seed takes several minutes;
   later runs only fill in new days. The script then times every endpoint, rewrites the table between the
   `baseline:start` and `baseline:end` markers in the report, and stops the API.

`-Only "<regex>"` times only the matching routes. `-Runs` changes the number of timed calls.

Needs hosts entries for `-RootHost` and `-TenantHost` (`crownus.test` and `dapi.crownus.test` by default), the backend certificate in `.certs` (or `-CertDir`), and
Python 3. A worktree has no `.env`; pass the main clone's file with `-EnvFile`.

Drop the database when you are done (`DROP DATABASE CrownusDb_perf`).
