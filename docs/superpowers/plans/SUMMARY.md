# plans/ — Summary

**Purpose:** Dated implementation plans, one per feature/fix, following the header format in the `writing-plans` skill (Goal / Architecture / Tech Stack / Global Constraints / numbered tasks with checkboxes).

**Last updated:** 2026-08-04

## Files (chronological)

- `2026-07-27-forgot-password-restricted-role-http-rls-proof.md` — proves `POST /api/v1/auth/forgot-password` enforces PostgreSQL RLS end-to-end over real HTTP under the restricted `onevo_app` runtime role, closing a gap where existing HTTP tests ran against a Testcontainers superuser connection that never surfaced RLS violations.
- `2026-07-28-legal-document-rich-content.md` — stores Terms/Privacy legal document content directly in `legal_document_versions`, adds Developer Platform admin CRUD+publish/archive endpoints and public read endpoints, and wires content into the pending-legal-acceptance flow.
- `2026-07-28-tenant-host-password-login-retirement.md` — removes the dead tenant-host email/password login path (`LoginCommand`/`LoginCommandHandler`/`LoginCommandValidator`), leaving base-domain credential-first login as the only password login entry point.
- `2026-08-02-dev-smoke-multi-tenant-seed-expansion.md` — expands `DevSmokeTestTenantSeeder` (Development/Test only) to two tenants, multiple users/roles, and multiple legal entities, all idempotent.
- `2026-08-03-doc-audit-and-process-setup.md` — Sets up `rules/` + per-folder `SUMMARY.md` files, reconciles the Architecture doc's Authentication section against `workflow/authentication.md`, and queues the remaining architecture/tables audit as Phase 2/3.
- `2026-08-04-test-suite-audit-and-invite-coverage.md` — **this plan.** Full pass/fail baseline across both repos' test suites (backend unit/architecture/integration, frontend Vitest/Playwright), a verified backend test-coverage gap inventory, fixes for a stale `.env` key and a stale Playwright assertion (both live failures, not code regressions), and new unit tests closing the Invitation-flow coverage gap.

## Open items

- Phase 2 (remaining `ONEVO_Backend_Architecture_Document.md` sections: tenant isolation, caching, file handling, performance, testing, deployment) and Phase 3 (structural table-existence check of `phase1-table-inventory.md`) are queued in `2026-08-03-doc-audit-and-process-setup.md` Tasks 6-7 but not yet executed.
- Confirmed but explicitly deferred test-coverage gaps (user chose Invitation flow only for this pass): `PermissionsController` (zero coverage), Stripe webhook event processing (`ProcessStripeEventCommandHandler`), and a ~50-handler long tail in the DevPlatform admin backoffice. See `2026-08-04-test-suite-audit-and-invite-coverage.md` for the full inventory.
