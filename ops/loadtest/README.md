# Load test

`journeys.js` is a k6 script for the main user journeys (task 29). The gate it enforces and the latest results:
[docs/deployment/release-gate.md](../../docs/deployment/release-gate.md).

## Profiles

| `PROFILE` | Virtual users | Length | Gate |
|---|---|---|---|
| `smoke` | 1, two passes | seconds | every request 2xx, p95 < 2 s (a cold instance) |
| `normal` | `NORMAL_VUS` (20) | 6.5 min | p95 <= 200 ms per journey, < 1 % errors |
| `peak` | 3 x `NORMAL_VUS` | 7.5 min | < 1 % errors |
| `soak` | `NORMAL_VUS` | `SOAK_MINUTES` (60) + 3 min | < 1 % errors, p95 < 400 ms |

Each virtual user signs in once with its own test account, then repeats a session: dashboard (`auth/me`,
`dashboards/me`, bootstrap, unread count, inbox counts), task board (my projects, work graph), employee list,
attendance today, leave balances, inbox (counts, chats), with 1 to 3 seconds between pages.

## Test accounts

- One account per virtual user works best: the API limits each signed-in user to about 10 requests a second, so
  many users sharing one account measure the rate limiter, not the API. Accounts are reused round robin.
- Use test accounts only, never a real person's. Locally: the seeded `@dapi.test` users (password from the seeders).
- A test account that never signed in still owes the legal documents. `ACCEPT_TEST_LEGAL=1` accepts them during
  sign-in, through the app's own step. Use it only for test accounts.

## Run locally

Start the API in Release with quiet logging (`backend-api-loadtest` in `.claude/launch.json`, or):

```powershell
dotnet run -c Release --project src/ONEVO.Api/ONEVO.Api.csproj --launch-profile https -- --Serilog:MinimumLevel:Default=Warning --Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore=Warning --ForwardedHeaders:KnownNetworks:0=172.16.0.0/12 --ForwardedHeaders:KnownNetworks:1=192.168.0.0/16
```

The two `KnownNetworks` entries let the API trust `X-Forwarded-For` from the Docker network, so each virtual user
counts as its own client for the per-IP limits, as real users behind a proxy do. Local runs only.

Then run k6 from its image, with the mkcert root so TLS is verified (copy `rootCA.pem` from
`$env:LOCALAPPDATA\mkcert` and `journeys.js` into one folder first):

```powershell
docker run --rm -v "${PWD}:/work" -w /work --add-host crownus.test:host-gateway --add-host dapi.crownus.test:host-gateway -e SSL_CERT_FILE=/work/rootCA.pem -e PROFILE=normal -e ROOT_URL=https://crownus.test:7229 -e TENANT_URL=https://dapi.crownus.test:7229 -e USERS="a@dapi.test,b@dapi.test" -e PASSWORD="<test password>" -e ACCEPT_TEST_LEGAL=1 -e SUMMARY_DIR=/work grafana/k6:0.54.0 run journeys.js
```

`SUMMARY_DIR` writes `<profile>-summary.json` with every metric. The console shows p95 per endpoint and the gate.

## Run against UAT

**Ask the server owner first.** The UAT database sits on a SQL Server instance shared with other systems, so peak
and soak must run at an agreed time, out of office hours. Smoke and a short normal run are light.

1. Create test accounts in a UAT test tenant (one per virtual user), sign each in once or use `ACCEPT_TEST_LEGAL=1`.
2. UAT runs in single-host mode: `ROOT_URL` and `TENANT_URL` are both `https://<app service host>`, and
   `TENANT_HEADER=<tenant slug>` sends the tenant in `X-Crownus-Tenant`.
3. The load balancer's address is what the API sees, so `X-Forwarded-For` from k6 is ignored on UAT: all virtual
   users share one client address. The per-IP limit (pre-authentication, 6,000 a minute) is far above these runs;
   sign-in in setup is limited to 20 per 5 minutes per address, so use at most 20 accounts per 5 minutes.
4. Record CPU and memory from App Service metrics and p95 per route from Application Insights (task 17).

## Smoke test in the deploy pipeline

`.github/workflows/deploy-appservice.yml` runs `PROFILE=smoke` after the new build is live when the repository
variable `LOADTEST_SMOKE` is `true`. It needs:

| Setting | Kind | Value |
|---|---|---|
| `LOADTEST_SMOKE` | variable | `true` to turn the step on |
| `LOADTEST_TENANT` | variable | the UAT test tenant's slug |
| `LOADTEST_USERS` | secret | comma-separated test account emails (two are enough) |
| `LOADTEST_PASSWORD` | secret | their password |

The accounts must have accepted the legal documents already (the pipeline does not set `ACCEPT_TEST_LEGAL`).
