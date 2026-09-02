# Monitoring Legal-Entity Default and Tray Profile Fix Report

## Outcome

Monitoring defaults are legal-entity scoped, and tray profile/policy resolution no longer selects an arbitrary Employee row. Tenant IDs remain server-derived and are not present in request DTOs or responses.

## Old behavior

- `monitoring_feature_toggles` allowed one row per tenant, so every legal entity inherited the same default.
- Effective tray policy resolved a user through `GetDefaultForUserAsync`, which could select one of several company Employee rows.
- `EfTrayActivationRepository.FindEmployeeProfileAsync` used unordered `FirstOrDefaultAsync` across every Employee row linked to the user.
- Activation/device records and device JWTs did not retain legal-entity context.

## New behavior

- `monitoring_feature_toggles.legal_entity_id` identifies an exact company default. A nullable tenant row is retained only as a documented backward-compatibility fallback.
- Reads prefer the exact `(tenant_id, legal_entity_id)` row, then the retained tenant fallback. Writes require the server-derived current legal entity and create/update only its exact row.
- The effective-policy resolver requires the device JWT's `legal_entity_id`, resolves one active Employee for that user and legal entity, and fails closed if it cannot do so.
- Legacy resolver/profile calls without legal-entity context return an employee only when exactly one active Employee exists.
- Activation codes and device approvals take legal-entity context from `ICurrentUser.LegalEntityId`; registrations and JWTs carry it forward. The tray does not invent or submit company context.
- Profile responses include `employee_profile_status`. Legacy ambiguous contexts use `company_context_required` and do not expose an arbitrary Employee profile.

## Migration and backfill

Migration: `20260828121043_ScopeMonitoringDefaultsAndTrayProfilesToLegalEntity`.

- Adds nullable legal-entity columns to monitoring defaults, activation codes, device authorizations, and device registrations.
- Adds restrictive foreign keys to `legal_entities`.
- Replaces tenant-only default uniqueness with:
  - unique `(tenant_id, legal_entity_id)` legal-entity defaults;
  - one optional null-scoped tenant fallback per tenant.
- Copies every existing tenant fallback to every active legal entity in that tenant, guarded by `NOT EXISTS`; explicit legal-entity rows are never overwritten.
- Keeps the original row as the documented tenant fallback.
- Backfills legacy tray rows only when a user has exactly one active Employee. Multi-company identities remain null and fail closed.
- Existing tenant RLS on `monitoring_feature_toggles` remains enforced; no unprotected table was introduced.

## API contract

- `GET /api/v1/attendance/monitoring/policy` (`monitoring:read`) returns the current session legal entity's default plus visible overrides.
- `PUT /api/v1/attendance/monitoring/policy/company` (`monitoring:configure`) updates the current session legal entity's exact default.
- Existing override PUT/DELETE routes remain unchanged and validate department/position targets against the current legal entity.
- `GET/PUT /api/v1/monitoring/settings` now also read/write the current session legal entity.
- No request body accepts `tenantId` or `legalEntityId`; both are derived from the authenticated session. No tenant ID is exposed in responses.
- Tray auth responses add `employee_profile_status` (`resolved`, `profile_unavailable`, or `company_context_required`).
- Tray device JWTs add the `legal_entity_id` claim.

## Precedence

The existing documented precedence is preserved:

1. Employee override
2. Role override
3. Position override
4. Department override
5. Legal-entity default
6. Retained tenant fallback
7. Safe default (`false`, or two minutes for idle threshold)

## Principal files changed

- Domain/configuration: monitoring toggles and tray activation entities/configurations.
- Application contracts/handlers: monitoring toggle repository contract, settings handlers, tray enrollment/token/current-device contracts, activation handlers, effective policy handler.
- Infrastructure: monitoring toggle repository/resolver/configuration service, tray repository/token/current-device services, transaction-safe tenant switcher.
- Migration/model snapshot: `20260828121043_ScopeMonitoringDefaultsAndTrayProfilesToLegalEntity*` and `ApplicationDbContextModelSnapshot.cs`.
- Tests: monitoring settings/policy unit tests, deterministic tray profile repository tests, legal-entity settings integration tests, policy and activation fixtures.

Unrelated pre-existing working-tree changes were preserved. The referenced earlier hardening reports were not present at the repository root when work began.

## Verification

- Release API build: passed.
- Focused backend unit command (`Monitoring|TrayActivation|EmployeeProfile`): 177 passed.
- Architecture tests: 714 passed.
- EF pending-model check: no pending model changes.
- PostgreSQL migration smoke test: passed.
- PostgreSQL focused cases passed: independent legal-entity defaults and duplicate rejection; multi-company resolver fail-closed/exact-company behavior; no-employee activation fail-closed; valid activation/profile exchange; legal-entity effective tray policy.
- Integration project build: passed.
- `git diff --check`: passed (line-ending conversion notices only).

Warnings observed but not caused by this change: known package vulnerability warnings (`SQLitePCLRaw.lib.e_sqlite3`, `SSH.NET`), existing nullable/duplicate-using warnings, and MediatR/FluentAssertions license notices in test output.

## Remaining risks

- The tenant fallback remains readable only for backward compatibility. It should be removed in a later migration after every production legal entity has an explicit row and fallback usage has been observed at zero.
- Legacy multi-company devices must reactivate from a selected company; this is deliberate fail-closed behavior.
- A full integration-suite run was not completed; focused PostgreSQL scenarios were run because the full monitoring subset starts a new container per test and was interrupted after it exposed outdated fixtures. Those fixtures were corrected and the affected scenarios rerun successfully.
