# Tray Employee Identity Closure - Execution Index

**Status:** Pending

**Goal:** Finish the real employee identity flow in the Windows TrayApp without rewriting the already-shipped Backend/Service transport.

**Specs:**

- `docs/superpowers/specs/2026-08-08-tray-login-employee-identity-design.md`
- `docs/superpowers/specs/next/2026-08-13-tray-monitoring-completion-roadmap-design.md` (Milestone 2)

## Entry gate

Before final Milestone 2 sign-off, the live AWS biometric enrollment record at
`C:\HR\tray_app_maui\docs\superpowers\plans\2026-08-13-task21-e2e-verification-record.md`
must show PASS. Provisioning uses the existing
`docs/superpowers/plans/2026-08-13-aws-biometric-infra-setup.md` runbook; no new
biometric application code belongs in this plan.

## Execution order

1. `part-1-identity-store.md` - add atomic cache ownership and stale-value removal.
2. `part-2-tray-screen-integration.md` - connect activation, onboarding, clock-in, and logout to that store.
3. `part-3-contract-and-validation.md` - lock HTTP/IPC contracts and run automated plus real Windows verification.

## Final outcome

The same server-derived identity appears on Prepare, Review, and Clock In;
reactivation cannot leak a prior employee number; Windows username is never
treated as employee identity; successful logout clears the cache; all results
are captured in a privacy-safe validation record.
