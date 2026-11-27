# Task 15: Deny by default for every endpoint

| | |
|---|---|
| Priority | 15 of 30 (lower number = build first) |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | Security |
| Depends on | nothing |

## What we found

`AuthorizationExtensions` defines three named policies and no fallback policy. Any action that forgets `[Authorize]` is open to the internet. The project relies on a script (`dev-env/scripts/find-unauthorized-endpoints.ps1`) and on reviewers to catch this. With 755 actions across 145 controllers that is fragile.

The script currently lists the expected public endpoints: login, password reset, session exchange, legal documents, tray activation and device authorization, tray release check, and the Stripe webhooks.

## What to do

1. Set `FallbackPolicy` to require an authenticated user. Then every action is protected unless it says `[AllowAnonymous]`.
2. Add `[AllowAnonymous]` to each intended public action with a one-line comment that says how it protects itself (rate limit, signed token, webhook signature).
3. Check the health endpoints, the SignalR hubs and Swagger still behave as intended.
4. Add an architecture test: every controller action has either a permission attribute or `[AllowAnonymous]`. That replaces the manual script in CI.
5. For each public endpoint confirm: rate limited, no tenant data without a valid token, the same response whether an account exists or not.
6. Check the Stripe webhook verifies the signature before it reads the body as data.

## Done when

- Removing `[Authorize]` from a test controller makes the architecture test fail.
- The list of public endpoints is documented in `docs/api/public-endpoints.md` with the protection each one uses.
- Full unit and architecture suites pass.

## Status (2026-11-23)

Done. Merged to `development` in PR #240 on 2026-11-20, with the other person-1 tasks 15, 19, 19b, 23, 27, 32, 33, 39a and 44. Report: `docs/reports/security/2026-11-17-task-15-deny-by-default.md`.

Swagger (step 3) no longer exists: task 23 removed it in every environment.

Open findings, reported and not fixed (details in `docs/api/public-endpoints.md`, "Open findings"):

1. Stripe webhook routes are not rate limited.
2. Tray activation-code exchange has only the general per-IP limit. The right limit is a product decision.
3. Admin login and force-change-password skip password hashing when the email is unknown, so response timing can show whether an account exists.
4. The Stripe webhook error text tells "not configured" apart from "invalid signature".

Decided 2026-11-23: findings 1, 3 and 4 are fixed next as person-1 follow-up work. Finding 2, the tray activation limit, moves to person-2 with the admin-managed rate limits (tasks 45 to 49).

Update 2026-11-27: follow-up 15b is done for findings 3 and 4 (see below). Finding 1, the Stripe webhook rate limit, went to person-2 as well (tasks 45 to 49). It is on the local branch `claude/merge-development-1126`, not yet in `feature/testing` or `development`.

## Follow-up results (2026-11-23)

Branch `claude/improve-task-15b-stripe-admin-login`, from `origin/development` at `b6fabb74f` (after PR #240).
These are the open findings from `docs/api/public-endpoints.md` and
`docs/reports/security/2026-11-17-task-15-deny-by-default.md`. Tests were written first.

Done:

1. **Stripe webhook verifies the signature first.** `EventUtility.ConstructEvent` from the official Stripe SDK
   already checked the signature before the event data was read. The controller now also refuses a missing or
   blank `Stripe-Signature` header before the secret lookup. Tests use a test-only signing secret, and prove
   that a missing, malformed, wrong-secret or tampered-body signature returns 400, calls no handler, and writes
   nothing. A valid signature on `checkout.session.completed` dispatches exactly once.
2. **No Stripe or exception detail in responses.** Every webhook rejection returns the same `WebhookRejection`
   400 body: a generic detail plus the request's correlation id. The reason and the Stripe exception are
   logged server side under that correlation id. Before this, "Stripe webhook is not configured." and "Invalid
   Stripe signature." told the two cases apart. No other code calls the Stripe SDK, and
   `PaymentGatewayVerificationService` is still a stub with no Stripe calls. Unhandled exceptions already get
   the generic 500 from `ExceptionHandlerMiddleware`.
3. **Admin login timing.** `AdminLoginCommandHandler` verifies the password against `DummyPasswordHash`, a real
   BCrypt hash at the production work factor 12. It does this on every failure path that had no hash to check:
   unknown email, missing credential and locked account. A locked account never checks the stored hash. Unit
   tests prove the hasher runs exactly once on each failure path and that the dummy is a real cost-12 hash.
   There is no timing test.

Skipped (assigned to person 2, 3 or 4):

- Stripe webhook and tray rate limits: person 2, tasks 45–49.
- Stripe keys and secrets: person 4, task 05.
- The unversioned Stripe route: person 3, task 16.

Still open: `ForcePasswordChangeCommandHandler` has the same unknown-email timing gap. It is not in this
follow-up's scope.
