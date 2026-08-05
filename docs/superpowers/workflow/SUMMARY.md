# workflow/ — Summary

**Purpose:** Point-in-time, code-verified investigation/implementation reports. Unlike `project_ core/` (living architecture docs), these are dated snapshots of what was found or built for a specific task.

**Last updated:** 2026-08-03

## Files

- `authentication.md` — **Code-verified source of truth** for the full login/session/CSRF/authorization flow (base-domain login, workspace-selection challenge, exchange-code hand-off, MFA, legal-acceptance gate, logout), with an explicit "Current Gaps and Risks" section. Per [[PROCESS_RULES]] rule 4, this outranks the generic Authentication section in `ONEVO_Backend_Architecture_Document.md` wherever the two differ.
- `TENANT_SESSION_EXCHANGE_LOGIN_FLOW_REPORT.md` — implementation/verification report for the base-domain-to-tenant-host session-exchange mechanism (the one-time code hand-off).
- `LOGIN_WORKSPACE_RESPONSE_FIX_REPORT.md` — fix report for tenant/workspace fields missing from login response DTOs.
- `DEV_SMOKE_MULTI_TENANT_SEED_EXPANSION_REPORT.md` — report for expanding the Development/Test-only `DevSmokeTestTenantSeeder` to two tenants with multiple seeded users/roles/legal entities.
- `BACKEND_MKCERT_TENANT_SUBDOMAIN_HTTPS_REPORT.md` — fix report for local HTTPS/mkcert trust between the Angular dev server and the tenant-subdomain backend.
- `LEGAL_DOCUMENT_RICH_CONTENT_MANAGEMENT_REPORT.md` — implementation report for storing Terms/Privacy rich content in `legal_document_versions` plus admin CRUD/publish/archive and public read endpoints.
- `LEGAL_ENTITY_GENERAL_SETTINGS_BACKEND_AUDIT_PLAN.md` — Part 1 audit + plan (no code changed) for the Legal Entity / Company General Settings backend work.
- `LEGAL_ENTITY_GENERAL_SETTINGS_PART2A_SCHEMA_REPOSITORY_REPORT.md` — Part 2A: schema/entity/repository layer report for Legal Entity General Settings.
- `LEGAL_ENTITY_GENERAL_SETTINGS_PART2B_APPLICATION_CONTRACTS_REPORT.md` — Part 2B: Application-layer commands/queries/DTOs report for the same feature.
- `LEGAL_ENTITY_GENERAL_SETTINGS_PART2C_CONTROLLER_ENDPOINTS_REPORT.md` — Part 2C: controller endpoint wiring + HTTP/integration test report for the same feature.
- `LEGAL_ENTITY_GENERAL_SETTINGS_PART2D_POSTMAN_AND_HTTP_VALIDATION_REPORT.md` — Part 2D: Swagger/Postman re-verification report for the same feature.
- `LEGAL_ENTITY_POSTMAN_STALE_FOLDER_CLEANUP_REPORT.md` — Postman-collection-only cleanup report (no backend/doc changes).

## Open items

- This index was built from file titles/scope lines only; if a report is later superseded or found stale, mark it here rather than deleting it silently.
