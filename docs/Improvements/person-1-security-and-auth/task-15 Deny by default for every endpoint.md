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
