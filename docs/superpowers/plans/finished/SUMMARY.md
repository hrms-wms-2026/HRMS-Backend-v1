# plans/finished/ — Summary

**Purpose:** Completed plans and point-in-time audit/fix reports. Everything here was moved from the flat `plans/` folder on 2026-08-06 as part of the `finished/` + `next/` restructure (see `docs/superpowers/rules/FILE_CREATION_RULES.md`). Files were **not** re-split or content-edited during the move — only relocated, per the migration rule (existing files get moved + status-tagged, not retroactively restructured into parts). The `plans/kajaa/` personal folder was also dissolved into this structure the same day — its 3 finished plans now live here too (see "From `kajaa/`" below).

**Last updated:** 2026-08-16

## Layout: date subfolders

Per user request, `finished/` is further split into one subfolder per completion date (`YYYY-MM-DD/`), each holding the files completed that day. This date-folder split exists **only inside `finished/`** — `next/` stays flat (see `FILE_CREATION_RULES.md` rule 7).

For the 16 files with no date in their filename (the `*_REPORT.md`/`*_AUDIT_PLAN.md` ones), the date was determined from a `**Date:**` header where present, or otherwise inferred from content (cross-references to sibling Part reports, migration filenames, or matching plan files) — marked "(inferred)" below where not explicitly stated in the file itself.

## Status note

All 42 files from the 2026-08-06 batch move are treated as `finished` by default (dated plans with no open-task markers, and `*_REPORT.md`/`*_AUDIT_PLAN.md` files, which are inherently point-in-time and already acted on). This is a filename/convention-based classification done during the move, not a per-file content re-verification — if any of these turns out to actually be incomplete, flag it and it should move to `plans/next/` with status `pending` and a note explaining what's left. The 43rd and 44th files (both in `2026-08-08/`) are different: each was individually verified task-by-task (build + unit tests + inline diff review) rather than batch-classified, per their own entries below.

## Files by date

Each entry's **Related:** line is a wiki-link (`[[bare-filename]]`, no extension/path — resolve by searching this repo's `docs/superpowers/` tree) to the files it's most tightly connected to: a corresponding design in `specs/`, a sibling Part in the same report chain, or the report/plan it fixes or validates. Per [[FILE_CREATION_RULES]] rule 8, keep these links current when a file moves or a new related file is added.

**`2026-07-27/`** (1)
- `2026-07-27-forgot-password-restricted-role-http-rls-proof.md`

**`2026-07-28/`** (2)
- `2026-07-28-legal-document-rich-content.md`
- `2026-07-28-tenant-host-password-login-retirement.md`

**`2026-08-02/`** (1)
- `2026-08-02-dev-smoke-multi-tenant-seed-expansion.md`

**`2026-08-03/`** (13)
- `2026-08-03-doc-audit-and-process-setup.md` — Related: [[2026-08-03-doc-audit-and-process-setup-design]] (its spec in `specs/finished/2026-08-03/`)
- `2026-08-03-view-model-retrofit-phase1.md`
- `2026-08-03-work-management-foundation.md` (from `kajaa/`) — Related: [[2026-08-03-work-management-foundation-design]] (its spec)
- `DEPARTMENT_FOUNDATION_BACKEND_AUDIT_PLAN.md` (inferred — precedes Part 2A) — Related: [[DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT]], [[DEPARTMENT_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT]], [[DEPARTMENT_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT]], [[DEPARTMENT_FOUNDATION_PART2D_HTTPS_VALIDATION_REPORT]] (the audit's Part 2A-2D execution chain, in `finished/2026-08-03/` and `finished/2026-08-04/`)
- `DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md` (inferred — sibling of 2B/2C) — Related: [[DEPARTMENT_FOUNDATION_BACKEND_AUDIT_PLAN]], [[DEPARTMENT_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT]], [[DEPARTMENT_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT]], [[DEPARTMENT_FOUNDATION_PART2D_HTTPS_VALIDATION_REPORT]]
- `DEPARTMENT_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT.md` (explicit `**Date:**`) — Related: [[DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT]], [[DEPARTMENT_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT]]
- `DEPARTMENT_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT.md` (explicit `**Date:**`) — Related: [[DEPARTMENT_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT]], [[DEPARTMENT_FOUNDATION_PART2D_HTTPS_VALIDATION_REPORT]]
- `DEPARTMENT_FOUNDATION_PART2D_HTTPS_VALIDATION_REPORT.md` (inferred — validates the roles:read fix same day) — Related: [[DEPARTMENT_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT]], [[TENANT_PROVISIONING_ROLES_READ_FIX_REPORT]] (the fix it validates as green)
- `DEPARTMENT_HEAD_POSITION_ASSIGNMENT_REPORT.md` (explicit, found in content) — Related: [[DEPARTMENT_HEAD_POSITION_SCHEMA_CORRECTION_REPORT]]
- `DEPARTMENT_HEAD_POSITION_SCHEMA_CORRECTION_REPORT.md` (explicit `**Date:**`) — Related: [[DEPARTMENT_HEAD_POSITION_ASSIGNMENT_REPORT]]
- `LEGAL_ENTITY_BACKEND_GENERAL_SETTINGS_SCHEMA_RECONCILIATION_REPORT.md` (inferred — precedes the Legal Entity General Settings Part 2A-D chain in `workflow/`) — Related: `workflow/LEGAL_ENTITY_GENERAL_SETTINGS_BACKEND_AUDIT_PLAN.md`, `workflow/LEGAL_ENTITY_GENERAL_SETTINGS_PART2A_SCHEMA_REPOSITORY_REPORT.md` through `PART2D` (different folder — path-linked, not wiki-linked, since `workflow/` isn't split by date)
- `PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md` (inferred — self-describes as "today's Department Part 2A batch") — Related: [[DEPARTMENT_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT]], [[TENANT_PROVISIONING_ROLES_READ_FIX_REPORT]]
- `TENANT_PROVISIONING_ROLES_READ_FIX_REPORT.md` (inferred — explicitly says "part of today's Department Part 2A batch") — Related: [[PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT]], [[DEPARTMENT_FOUNDATION_PART2D_HTTPS_VALIDATION_REPORT]] (the report that validates this fix)

**`2026-08-04/`** (17)
- `2026-08-04-department-hardening-part1.md` — Related: [[2026-08-04-department-hardening-part2]], [[2026-08-04-department-hardening-part3]], [[DEPARTMENT_HARDENING_PART1_CODE_HIERARCHY_ARCHIVE_REPORT]]
- `2026-08-04-department-hardening-part2.md` — Related: [[2026-08-04-department-hardening-part1]], [[2026-08-04-department-hardening-part3]], [[DEPARTMENT_HARDENING_PART2_ARCHIVE_RESTORE_REPORT]]
- `2026-08-04-department-hardening-part3.md` — Related: [[2026-08-04-department-hardening-part1]], [[2026-08-04-department-hardening-part2]], [[DEPARTMENT_HARDENING_PART3_LIST_SEARCH_PAGINATION_REPORT]]
- `2026-08-04-position-foundation-part2b.md`
- `2026-08-04-test-suite-audit-and-invite-coverage.md`
- `2026-08-04-tray-employee-check-in.md`
- `2026-08-04-work-management-milestone-hierarchy.md` (from `kajaa/`) — Related: [[2026-08-04-work-management-milestone-hierarchy-design]] (its spec), [[2026-08-06-work-management-milestone-membership-and-achieve]] (the follow-up plan that closes gaps parked at this plan's final review, in `finished/2026-08-08/`)
- `2026-08-04-work-management-projects-edit-delete-view.md` (from `kajaa/`) — Related: [[2026-08-04-work-management-projects-edit-delete-view-design]] (its spec)
- `DEPARTMENT_HARDENING_PART1_CODE_HIERARCHY_ARCHIVE_REPORT.md` (explicit `**Date:**`) — Related: [[2026-08-04-department-hardening-part1]] (its plan), [[DEPARTMENT_HARDENING_PART2_ARCHIVE_RESTORE_REPORT]]
- `DEPARTMENT_HARDENING_PART2_ARCHIVE_RESTORE_REPORT.md` (explicit `**Date:**`) — Related: [[2026-08-04-department-hardening-part2]] (its plan), [[DEPARTMENT_HARDENING_PART1_CODE_HIERARCHY_ARCHIVE_REPORT]], [[DEPARTMENT_HARDENING_PART3_LIST_SEARCH_PAGINATION_REPORT]]
- `DEPARTMENT_HARDENING_PART3_LIST_SEARCH_PAGINATION_REPORT.md` (explicit, found in content) — Related: [[2026-08-04-department-hardening-part3]] (its plan), [[DEPARTMENT_HARDENING_PART2_ARCHIVE_RESTORE_REPORT]], [[DEPARTMENT_POSITION_POSTMAN_MANUAL_VALIDATION_CLEANUP_REPORT]] (Postman cleanup that followed this same day)
- `DEPARTMENT_POSITION_POSTMAN_MANUAL_VALIDATION_CLEANUP_REPORT.md` (inferred — reconciles Postman right after Department Part 3) — Related: [[DEPARTMENT_HARDENING_PART3_LIST_SEARCH_PAGINATION_REPORT]], [[POSITION_FOUNDATION_PART2D_HTTPS_HTTP_POSTMAN_VALIDATION_REPORT]]
- `POSITION_FOUNDATION_BACKEND_AUDIT_PLAN.md` (explicit `**Date:**`) — Related: [[POSITION_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT]], [[POSITION_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT]], [[POSITION_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT]], [[POSITION_FOUNDATION_PART2D_HTTPS_HTTP_POSTMAN_VALIDATION_REPORT]]
- `POSITION_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT.md` (explicit `**Date:**`) — Related: [[POSITION_FOUNDATION_BACKEND_AUDIT_PLAN]], [[POSITION_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT]]
- `POSITION_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT.md` (inferred — sibling of 2A/2C/2D) — Related: [[POSITION_FOUNDATION_PART2A_SCHEMA_REPOSITORY_REPORT]], [[POSITION_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT]]
- `POSITION_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT.md` (inferred — sibling of 2A/2B/2D) — Related: [[POSITION_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT]], [[POSITION_FOUNDATION_PART2D_HTTPS_HTTP_POSTMAN_VALIDATION_REPORT]]
- `POSITION_FOUNDATION_PART2D_HTTPS_HTTP_POSTMAN_VALIDATION_REPORT.md` (inferred — sibling of 2A/2B/2C) — Related: [[POSITION_FOUNDATION_PART2C_CONTROLLER_ENDPOINTS_REPORT]], [[DEPARTMENT_POSITION_POSTMAN_MANUAL_VALIDATION_CLEANUP_REPORT]]

**`2026-08-05/`** (5)
- `2026-08-05-activity-monitoring-keyboard-mouse-tracking.md` (explicit `**Date:**`)
- `2026-08-05-dev-smoke-employee-seeding.md` — Related: [[DEV_SMOKE_EMPLOYEE_SEED_RECONCILIATION_REPORT]] (its reconciliation report)
- `2026-08-05-remove-legacy-employee-job-title-manager-fields.md` — Related: [[EMPLOYEE_POSITION_MODEL_RECONCILIATION_REPORT]] (its reconciliation report)
- `DEV_SMOKE_EMPLOYEE_SEED_RECONCILIATION_REPORT.md` (inferred — matches the dev-smoke-employee-seeding plan) — Related: [[2026-08-05-dev-smoke-employee-seeding]]
- `EMPLOYEE_POSITION_MODEL_RECONCILIATION_REPORT.md` (inferred — matches the remove-legacy-employee-job-title-manager-fields plan) — Related: [[2026-08-05-remove-legacy-employee-job-title-manager-fields]]

**`2026-08-06/`** (3)
- `2026-08-06-legal-entity-permission-access-filter.md` — Related: [[LEGAL_ENTITY_PERMISSION_AND_ACCESS_FILTER_REPORT]] (its report)
- `DEV_SMOKE_ORG_MODULE_PERMISSION_SEED_FIX_REPORT.md` (mid-merge, today's work)
- `LEGAL_ENTITY_PERMISSION_AND_ACCESS_FILTER_REPORT.md` (explicit, matches its plan, mid-merge today) — Related: [[2026-08-06-legal-entity-permission-access-filter]]

**`2026-08-08/`** (2)
- `2026-08-06-work-management-milestone-membership-and-achieve.md` — Related: [[2026-08-06-work-management-milestone-membership-and-achieve-design]] (its spec, in `specs/finished/2026-08-08/`), [[2026-08-04-work-management-milestone-hierarchy]] (the plan whose final-review gaps this one closes), [[2026-08-08-work-management-my-project-milestones]] (the follow-up read endpoint built on top of this plan's membership model, same day). All 18 tasks executed 2026-08-08, one commit per task, full unit suite green after each (1621/1621 final). Integration test coverage (Task 16) initially failed on a pre-existing `403` on project creation (reproduced at the pre-Task-10 commit, confirmed not a regression) — root-caused same day to `DefaultRoleSeeder` never granting Work Management permissions because they're still tagged the legacy `work_management` module key, which no subscription plan includes since the 2026-08-04 module-taxonomy rename. Test fixture fixed (commit `5ec9a91`); all 27 integration tests now pass. The underlying module-tag mismatch is flagged as a likely real production issue, not fixed here.
- `2026-08-08-work-management-my-project-milestones.md` — Related: [[2026-08-08-work-management-my-project-milestones-design]] (its spec, in `specs/finished/2026-08-08/`), [[2026-08-06-work-management-milestone-membership-and-achieve]] (the plan whose repository methods this one reuses). New read endpoint, `GET /api/v1/work/projects/{projectId}/objectives/mine`. All 5 tasks executed 2026-08-08, one commit per task, full unit suite green after each (1629/1629 final).

**`2026-08-09/`** (1)
- `2026-08-08-work-management-frontend-blocking-endpoints.md` — Related: the frontend repo's `Hrms--Web-application---front-end---v1/docs/superpowers/specs/next/2026-08-08-work-management-projects-milestones-design.md` (the consuming design, §6). Two same-module additions requested by the frontend and shipped same-session on direct user request (no separate brainstorm/spec, since both items were already fully specified in the request itself): (1) new `GET /api/v1/work/project-categories` endpoint (`ProjectCategoriesController` + `ListProjectCategoriesQuery`/Handler + `IProjectCategoryRepository.GetAllForTenantAsync`), (2) `isAchieved`/`achievedAt` added to `GET /work/projects/mine`'s response (`ProjectListItemResponse`/`ProjectListItemViewModel` + mappers). `dotnet build` clean (0 warnings/errors); `ListProjectCategoriesQueryHandlerTests.cs` added, 160/160 WorkManagement unit tests passing. Postman docs updated: new `List Project Categories.md`, `isAchieved`/`achievedAt` backfilled onto `List Projects.md` and `Get Project.md` (the latter was already stale before this change — its endpoint already returned these fields).

**`2026-08-10/`** (2)
- `2026-08-10-milestone-ownership-and-subtree-access.md` — Related: [[2026-08-10-milestone-ownership-and-subtree-access-design]] (its spec, in `specs/finished/2026-08-10/`); the frontend repo's `Hrms--Web-application---front-end---v1/docs/superpowers/plans/finished/2026-08-10/2026-08-10-milestone-cards-and-tree-view/` (the consuming feature this unblocks). Backend half of a cross-repo feature: (1) `IsOwner` added to `GetMyProjectMilestones`'s response, computed server-side the same way `Project.IsLead` is; (2) `GetObjectiveSubtree` loosened from Head-only to the membership-fallback pattern `GetObjectiveById` already uses. Both tasks executed 2026-08-10, one commit per task, full unit suite green after each (1694/1694 final). Self-review during plan-writing caught a variable-name collision (`parent` reused in two scopes) in the drafted `GetObjectiveSubtreeQueryHandler` code before it was ever run — fixed in the plan doc itself, so the executed code never hit it.
- `2026-08-10-project-detail-milestone-tree-view-backend.md` — Related: the shared full-stack design doc lives in the frontend repo, `Hrms--Web-application---front-end---v1/docs/superpowers/specs/next/2026-08-10-project-detail-milestone-tree-view-design.md` (covers both repos); the frontend repo's `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-10-project-detail-milestone-tree-view.md` (the consuming feature this unblocks). Adds `OwnerName`/`ReportingManagerName`/`IsOwner` to both `GetObjectiveById` and `GetObjectiveSubtree`, plus `IsAchieved`/`AchievedAt` to `GetObjectiveSubtree`'s node shape (already present on `GetObjectiveById`) — resolved via `IEmployeeRepository.GetByUserIdsAsync`, same batch-lookup pattern `GetMyProjectMilestonesQueryHandler` already used. `ObjectiveMapper.ToDetail`/`ToSubtreeNode` gained two new **optional, trailing** parameters so `EditObjectiveCommandHandler`/`CreateObjectiveCommandHandler` (the mapper's other two callers) needed zero changes. All 3 tasks executed 2026-08-10, one commit per task, full `WorkManagement` unit suite green after each (170/170 final). Postman docs (`Get Objective.md`, `Get Objective Subtree.md`) updated same session.

**`2026-08-16/`** (1 folder)
- `2026-08-16-multi-legal-entity-employment-foundation/` — Related: [[2026-08-16-multi-legal-entity-employment-foundation-design]] (its spec, in `specs/finished/2026-08-16/`). Three parts: invitation capacity reservation (`invitations:manage`, atomic `TryReservePositionAssignmentAsync` counting `active`+`planned`), cross-legal-entity invitation (same person, two `Employee` rows, one `User`; unique index on `(user_id, legal_entity_id)`), session active-company permission recompute (`Session.ActiveEmployeeId`, `POST /api/v1/session/active-company`). **Executed 2026-08-16**: all 3 parts; unit suite 2110/2110. Full HTTP passwordless-accept path kept at unit level; two-employee-per-user persistence covered against Postgres in `SwitchActiveCompanyIntegrationTests` and `EmployeeExistsInLegalEntityAsyncTests`.

## Notable plans (kept from earlier summary revisions)

- `2026-08-03/2026-08-03-work-management-foundation.md` (from `kajaa/`) — Work Management Slice 1: schema, Domain entities, repositories, and the `POST /api/v1/work/projects` creation transaction.
- `2026-08-04/2026-08-04-work-management-milestone-hierarchy.md` (from `kajaa/`) — Work Management's Milestone/Objective hierarchy: `PermissionSeeder.cs` retrofit, `Objective.ReportingManagerId` + `objective_change_requests` schema, recursive tree-authorization, 8 endpoints. Executed 2026-08-05/06.
- `2026-08-04/2026-08-04-work-management-projects-edit-delete-view.md` (from `kajaa/`) — Work Management Slice 2: `PUT`/`DELETE`/`GET`-by-id/`GET mine`/`GET ?userId=` on `ProjectsController`. Executed 2026-08-05.

`plans/kajaa/` was dissolved into this structure on 2026-08-06 — `plans/` now has only `finished/` and `next/` as its two status folders, per user request.

## Open items

- `DEV_SMOKE_ORG_MODULE_PERMISSION_SEED_FIX_REPORT.md` and `LEGAL_ENTITY_PERMISSION_AND_ACCESS_FILTER_REPORT.md` were untracked files sitting mid-merge (uncommitted) in the repo at the time of this move — moving them was a plain filesystem relocation only, no git operations were run against the in-progress merge.
