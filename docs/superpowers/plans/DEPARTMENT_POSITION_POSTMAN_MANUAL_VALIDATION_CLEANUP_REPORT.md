# Department + Position Postman / Manual Validation Cleanup

## Scope

This cleanup reconciles the Postman collection after Department Part 3 exposed
`headPositionId` on Department create/update contracts.

No backend source code, migrations, tests, frontend files, or OneVo-HR docs were changed.

## Files Changed

- `postman/environments/New Environment.environment.yaml`
- `postman/collections/ONEVO Organization Admin API/.resources/definition.yaml`
- `postman/collections/ONEVO Organization Admin API/07. Organization - Departments/`
- `postman/collections/ONEVO Organization Admin API/08. Organization - Positions/`
- `POSITION_FOUNDATION_PART2D_HTTPS_HTTP_POSTMAN_VALIDATION_REPORT.md`

## Environment Cleanup

The Postman environment is now aligned with the HTTPS-only local backend:

- `base_url = https://localhost:7229`
- `admin_base_url = https://admin.localhost:7229`
- `tenant_base_url = https://localhost:7229`
- `tenant_host = https://acme.localhost:7229`

## Collection Flow

Manual validation order is now:

1. Login and complete legal/session flow.
2. List or create Company and set `legal_entity_id`.
3. Create Department and set `department_id`.
4. Create Position under that Department and set `position_id`.
5. Update Department to set or clear `headPositionId`.

## Department Requests Added

- `List Departments`
- `List Department Tree`
- `Get Department`
- `Create Department`
- `Create Department With Head Should 409`
- `Update Department - Set Head Position`
- `Update Department - Clear Head Position`
- `Check Department Archive Blockers`
- `Archive Department`
- `Restore Department`
- `Deprecated DELETE Department Alias`

## Department Head Position Contract Captured

The Postman folder now reflects the current backend behavior:

- Create Department can send `headPositionId: null`.
- Create Department with a non-null `headPositionId` is a negative 409 case.
- Update Department can set `headPositionId` after a valid active position exists.
- Update Department can clear `headPositionId` by sending null.
- The body never sends `tenantId` or `legalEntityId`; legal entity is route-scoped.

## Position Folder Cleanup

The Position folder was moved from:

`07. Organization - Positions`

to:

`08. Organization - Positions`

This keeps the collection order aligned with the data dependency: positions require a
department id.

## Deferred Item Clarification

Position access/user assignment is still intentionally deferred. That is the future
employee/user-to-position and cross-legal-entity authority model. It should be built with
Employee/Position Assignment work, not mixed into the Department/Position foundation.

## Legal Entity Readiness Clarification

"Legal Entity backend is mostly ready for frontend except known country/logo follow-ups" means:

- The Company list/create/get/update/delete APIs exist for the General Settings screen.
- The current backend still persists country as `country_code`; docs now prefer `country_id`,
  but the countries reference table does not exist yet.
- Logo handling is intentionally not finalized here because asset/file ownership is pending
  separate asset work.

So frontend can build non-logo Company General Settings against the current backend, but
country reference-table migration and logo upload/asset integration are separate follow-ups.

## Verification

- Stale local HTTP port check: no `localhost:5139` or tenant-host login wording remains under
  `postman/`.
- Department folder exists with 11 request files.
- Position folder now exists as `08. Organization - Positions`.
- Department request bodies include `headPositionId` only where the backend contract expects it.
- `git diff --check -- postman` completed with no whitespace errors; only pre-existing Git
  LF/CRLF warnings were printed.

## Git Note

`postman/` is ignored by `.gitignore`. New Postman request files may not appear in normal
`git status`; use `git add -f` later if these Postman request files should be committed.
