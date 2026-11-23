# Task 15: Deny by default for every endpoint

Date: 2026-11-17. Branch: `claude/improve-task-15-deny-by-default-a82b8a`.
Task: `docs/Improvements/person-1-security-and-auth/task-15 Deny by default for every endpoint.md`.

## What changed

- **Fallback policy.** `AuthorizationExtensions` now sets `FallbackPolicy`, which requires an
  authenticated user. An endpoint with no `[Authorize]`, policy or `[AllowAnonymous]` is no longer
  anonymous.
- **The fallback accepts the right scheme.** It authenticates with a new policy scheme,
  `FallbackScheme` (`Auth/FallbackAuthenticationScheme.cs`), which forwards to one real scheme:
  - a bearer token, or `access_token` on the agent hub, goes to `TrayDeviceScheme`
  - an `/admin` path goes to `AdminScheme`
  - anything else goes to `TenantScheme`

  Listing all three schemes on the policy was rejected: on failure, each cookie scheme would write
  its own JSON 401 body to the same response.
- **Endpoints outside controllers are explicit:**
  - `/health` and `/health/ready` use `.AllowAnonymous()`.
  - `/hubs/inbox` uses `.RequireAuthorization("TenantPolicy")`.
  - `/hubs/agent-commands` uses `.RequireAuthorization("TrayDevicePolicy")`.
  - Swagger is middleware ahead of routing, so the fallback never sees it. It stays limited to
    Development or `Swagger:Enabled`.
- **Every `[AllowAnonymous]`** (32 actions in 13 controllers) now has a one-line comment that
  says how the endpoint protects itself. None was found that should not be public.
- **Small gap fixed.** `AuthRateLimitingMiddleware` now limits `POST /api/v1/auth/session-exchange`
  and `POST /api/v1/legal/acceptances/complete-login` to 20 per 5 minutes per IP, the same as
  login. Before this they only had the general per-IP limit.
- **Architecture test** `ControllerAuthorizationCoverageArchitectureTests`:
  - Every controller action, with class and method attributes combined, has `[Authorize]`
    (any policy), `[RequirePermission]`, `[RequirePlatformPermission]` or `[AllowAnonymous]`.
    Every public instance method counts as an action, not only `[Http*]` ones.
  - The `[AllowAnonymous]` set is pinned to a reviewed list, so a new public endpoint fails the
    test until `docs/api/public-endpoints.md` and the list are updated together.
  - Fixture controllers prove the check reports a controller that drops its attribute. A manual
    check also passed: removing `[Authorize(Policy = "TenantPolicy")]` from
    `AttendanceCorrectionsController` failed the test and named its 4 unprotected actions, and
    the edit was reverted.
  - `ci.yml` is unchanged. Whether this test replaces the script step in CI is for the user and
    the repo owner.
- **Tests added:**
  - Unit tests for the scheme selector and for the two new rate-limit rules.
  - Integration tests in `ApiBootTests`:
    - `/health/ready` answers without a session.
    - Both hub negotiate endpoints return 401 without a session.
    - An unmapped `/api/v1` route and an unmapped `/admin/v1` route return 401 without a session.
      The admin one returns a single JSON body.
- **Docs.** `docs/api/public-endpoints.md` lists every public endpoint with its protection, rate
  limit and enumeration behaviour.

## Behaviour change to know about

An anonymous request to a route that does not exist now gets **401 instead of 404**. The 401
comes from the tenant or admin scheme, picked by path. Signed-in callers still get 404. This is
expected under deny by default, and it hides the route map from anonymous callers.

## Per-endpoint review (step 5)

- **Rate limited.** Every public endpoint under `/api/v1` and `/admin/v1` has at least the
  general per-IP limit, and every credential-taking POST is in the auth limiter. Exceptions are
  findings 1 and 2.
- **No tenant data without a valid token.**
  - Legal documents and tray release metadata hold no tenant data.
  - The invitation preview needs the invitation token.
  - Every other endpoint needs a token, a challenge cookie or a signature.
- **Same response whether the account exists or not.** Tenant and admin login, forgot-password
  and force-change-password return identical bodies. Response timing is finding 3.

## Stripe webhook (step 6)

`EventUtility.ConstructEvent` checks the signature and timestamp before the event data is read.
An invalid signature returns 400 and dispatches nothing. Before the check, the controller reads
the raw body as a string and does one secret lookup.

## Findings reported, not fixed

Details are in `docs/api/public-endpoints.md`, "Open findings".

1. **Stripe webhook routes are not rate limited.** `/api/payment-gateways/...` is outside
   `/api/v1`, and task 07's PR #237 has the same gap.
2. **Tray activation-code exchange has only the general per-IP limit.** The right limit depends
   on how many trays share one NAT address, which is a product decision.
3. **Login timing.** Admin login and force-change-password skip password hashing when the email
   is unknown.
4. **Stripe webhook error text** tells "not configured" apart from "invalid signature". Both
   messages are pinned by unit tests.

## Rate limiting note

`development` still runs `PublicApiRateLimitingMiddleware`. Task 07 (PR #237) replaces it, and
both PRs edit `Program.cs` in nearby lines (rate limiting vs hub and health mapping), so expect a
trivial merge.

## Verification

Run on 2026-11-18 at `fae8714c0`, which is up to date with `origin/development` (`846b92028`,
including task 07's PR #237).

- `verify.ps1 backend -Full`, all steps pass:

  | Step | Result |
  |---|---|
  | Build (Release) | Build succeeded |
  | Unit | 8031 passed, 0 failed, 0 skipped |
  | Architecture | 718 passed, 0 failed, 0 skipped |
  | Integration (Testcontainers, SQL Server) | 1256 passed, 0 failed, 1 skipped |

- `find-unauthorized-endpoints.ps1`: 0 actions without an authorization attribute, 32 explicit
  `[AllowAnonymous]`.
- `security-scan.ps1 backend -Baseline origin/development`: Semgrep reports 0 new findings. The
  gitleaks (git history), DevSkim, NuGet and workflow findings are repo-wide and already on
  `development`. None of them is in a file this branch changes.
